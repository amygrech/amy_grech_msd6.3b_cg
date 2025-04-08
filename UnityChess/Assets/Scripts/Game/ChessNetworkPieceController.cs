using UnityEngine;
using UnityChess;
using Unity.Netcode;

/// <summary>
/// Controls chess piece interactivity based on network roles and turns.
/// Add this component to all chess piece prefabs.
/// </summary>
[RequireComponent(typeof(VisualPiece))]
public class ChessNetworkPieceController : MonoBehaviour
{
    private VisualPiece visualPiece;
    private bool isNetworked = false;
    private float checkInterval = 0.2f; // How often to check turn state (seconds)
    private float checkTimer = 0f;
    
    [SerializeField] private bool debugMode = true;
    
    void Awake() 
    {
        // Get the VisualPiece component
        visualPiece = GetComponent<VisualPiece>();
        if (visualPiece == null)
        {
            Debug.LogError("[ChessNetworkPieceController] Requires a VisualPiece component", this);
            enabled = false;
            return;
        }
    }
    
    void Start()
    {
        // Check if we're in a networked game
        CheckNetworkState();
        
        // Force update once at start
        UpdatePieceInteractivity();
    }
    
    void Update()
    {
        // Check network state periodically (in case we just connected)
        checkTimer += Time.deltaTime;
        if (checkTimer >= checkInterval)
        {
            checkTimer = 0f;
            
            // Check if network state changed
            CheckNetworkState();
            
            // Only check piece interactivity in networked games
            if (isNetworked)
            {
                UpdatePieceInteractivity();
            }
        }
    }
    
    /// <summary>
    /// Check if we're in a networked game
    /// </summary>
    private void CheckNetworkState()
    {
        bool wasNetworked = isNetworked;
        isNetworked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient;
        
        // If we just connected or disconnected, force an update
        if (wasNetworked != isNetworked)
        {
            if (debugMode) Debug.Log($"[ChessNetworkPieceController] Network state changed to {(isNetworked ? "connected" : "disconnected")} for {gameObject.name}");
            UpdatePieceInteractivity();
        }
    }
    
    /// <summary>
    /// Updates whether this piece should be interactive based on whose turn it is
    /// and which player we are in the network game
    /// </summary>
    private void UpdatePieceInteractivity()
    {
        // In single player, all pieces should be enabled by normal game rules
        if (!isNetworked)
        {
            return; // Let the game handle this normally
        }
        
        // In network mode, check with ChessNetworkManager if we can move this piece
        if (ChessNetworkManager.Instance != null && visualPiece != null)
        {
            bool canMove = ChessNetworkManager.Instance.CanMoveCurrentPiece(visualPiece.PieceColor);
            
            // Only update if needed to avoid constant enable/disable
            if (visualPiece.enabled != canMove)
            {
                visualPiece.enabled = canMove;
                
                if (debugMode) Debug.Log($"[ChessNetworkPieceController] Setting piece interactivity: {visualPiece.PieceColor} {gameObject.name} enabled={canMove}, Side={ChessNetworkManager.Instance.GetLocalPlayerSide()}, Current Turn={GameManager.Instance.SideToMove}");
            }
        }
        else 
        {
            if (ChessNetworkManager.Instance == null)
            {
                Debug.LogError("[ChessNetworkPieceController] ChessNetworkManager.Instance is null!");
            }
            if (visualPiece == null)
            {
                Debug.LogError("[ChessNetworkPieceController] visualPiece is null!");
            }
        }
    }
    
    /// <summary>
    /// Manually trigger a check for piece interactivity
    /// </summary>
    public void ForceUpdateInteractivity()
    {
        if (debugMode) Debug.Log($"[ChessNetworkPieceController] ForceUpdateInteractivity called for {gameObject.name}");
        UpdatePieceInteractivity();
    }
}
