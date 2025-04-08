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
    
    [SerializeField] private bool debugMode = false;
    
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
    private void UpdatePieceInteractivity() {
        // In single player, all pieces should be enabled by normal game rules
        if (!isNetworked) {
            return; // Let the game handle this normally
        }
    
        if (ChessNetworkManager.Instance == null) {
            Debug.LogError("[ChessNetworkPieceController] ChessNetworkManager.Instance is null!");
            return;
        }
    
        // Get local player's side
        Side localPlayerSide = ChessNetworkManager.Instance.GetLocalPlayerSide();
    
        // Get current turn from GameManager
        Side currentTurnSide = GameManager.Instance.SideToMove;
    
        // Debug output
        if (debugMode) Debug.Log($"[ChessNetworkPieceController] Checking piece {gameObject.name} - PieceColor: {visualPiece.PieceColor}, LocalSide: {localPlayerSide}, CurrentTurn: {currentTurnSide}");
    
        // Simple rule: Enable piece if it belongs to local player AND it's their turn
        bool canMove = (visualPiece.PieceColor == localPlayerSide) && (currentTurnSide == localPlayerSide);
    
        if (debugMode || visualPiece.enabled != canMove) {
            Debug.Log($"[ChessNetworkPieceController] {gameObject.name} ({visualPiece.PieceColor}) interactivity set to {canMove}. LocalSide={localPlayerSide}, CurrentTurn={currentTurnSide}");
        }
    
        // Only update if needed to avoid constant enable/disable
        if (visualPiece.enabled != canMove) {
            visualPiece.enabled = canMove;
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