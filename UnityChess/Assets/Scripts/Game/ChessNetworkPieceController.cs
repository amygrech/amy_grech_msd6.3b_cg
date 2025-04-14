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
    
    // Reference to turn manager
    private ImprovedTurnSystem turnSystem;
    
    // Track the last interactive state to avoid constant toggling
    private bool lastInteractiveState = false;
    
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
        // Find turn manager reference
        turnSystem = FindObjectOfType<ImprovedTurnSystem>();
        
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
    
        // Get current piece color
        Side pieceColor = visualPiece.PieceColor;
        
        // Determine if piece should be interactive
        bool canMove = false;
        
        // First check: Is this piece owned by the local player?
        bool isPlayersPiece = (pieceColor == localPlayerSide);
        
        // Second check: Is it this player's turn? (Using the improved turn manager)
        bool isPlayersTurn = false;
        
        if (turnSystem != null)
        {
            isPlayersTurn = turnSystem.CanPlayerMove(localPlayerSide);
            
            // If turn interactivity is locked, no piece can move
            if (turnSystem.lockInteractivity.Value || turnSystem.moveInProgress.Value)
            {
                isPlayersTurn = false;
            }
        }
        else
        {
            // Fallback to GameManager if turn manager not found
            isPlayersTurn = (GameManager.Instance.SideToMove == localPlayerSide);
        }
        
        // Only allow move if both conditions are true
        canMove = isPlayersPiece && isPlayersTurn;
    
        // Only log and update if the state has changed or in debug mode
        if (lastInteractiveState != canMove || debugMode) 
        {
            if (debugMode)
                Debug.Log($"[ChessNetworkPieceController] {gameObject.name} ({pieceColor}) interactivity set to {canMove}. " +
                      $"IsPlayersPiece={isPlayersPiece}, IsPlayersTurn={isPlayersTurn}, LocalSide={localPlayerSide}");
            
            // Update the enabled state
            visualPiece.enabled = canMove;
            
            // Update tracking variable
            lastInteractiveState = canMove;
        }
    }
    
    /// <summary>
    /// Manually trigger a check for piece interactivity
    /// </summary>
    public void ForceUpdateInteractivity()
    {
        if (debugMode) Debug.Log($"[ChessNetworkPieceController] ForceUpdateInteractivity called for {gameObject.name}");
        
        // Reset the tracking variable to ensure update occurs
        lastInteractiveState = !lastInteractiveState;
        
        // Update piece interactivity
        UpdatePieceInteractivity();
    }
}