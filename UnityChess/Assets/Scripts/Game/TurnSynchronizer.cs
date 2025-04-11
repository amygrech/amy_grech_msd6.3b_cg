using UnityEngine;
using Unity.Netcode;
using UnityChess;

public class TurnSynchronizer : NetworkBehaviour
{
    // Network variable to track current turn (0 = White, 1 = Black)
    public NetworkVariable<int> currentTurn = new NetworkVariable<int>(0, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server);
    
    // Reference to necessary components
    private ChessNetworkManager networkManager;
    private BoardSynchronizer boardSynchronizer;
    
    [SerializeField] private bool verbose = true;
    
    private void Awake()
    {
        networkManager = FindObjectOfType<ChessNetworkManager>();
        boardSynchronizer = FindObjectOfType<BoardSynchronizer>();
        
        if (networkManager == null || boardSynchronizer == null)
        {
            Debug.LogError("[TurnSynchronizer] Missing required components!");
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Subscribe to network variable changes
        currentTurn.OnValueChanged += OnTurnChanged;
        
        // Subscribe to move execution events
        GameManager.MoveExecutedEvent += OnMoveExecuted;
        VisualPiece.VisualPieceMoved += OnVisualPieceMoved;
        
        // Initialize turn state
        if (IsServer || IsHost)
        {
            currentTurn.Value = 0; // White starts
            if (verbose) Debug.Log("[TurnSynchronizer] Initialized: White's turn");
        }
    }
    
    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        currentTurn.OnValueChanged -= OnTurnChanged;
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
        VisualPiece.VisualPieceMoved -= OnVisualPieceMoved;
    }
    
    private void OnTurnChanged(int previousValue, int newValue)
    {
        if (verbose) Debug.Log($"[TurnSynchronizer] Turn changed from {(previousValue == 0 ? "White" : "Black")} to {(newValue == 0 ? "White" : "Black")}");
        
        // Force refresh pieces interactivity
        if (networkManager != null)
        {
            networkManager.RefreshAllPiecesInteractivity();
        }
    }
    
    private void OnMoveExecuted()
    {
        if (IsServer || IsHost)
        {
            // Get current side from game state and update turn accordingly
            Side currentSide = GameManager.Instance.SideToMove;
            int newTurnValue = currentSide == Side.White ? 0 : 1;
            
            // Update turn value
            currentTurn.Value = newTurnValue;
            if (verbose) Debug.Log($"[TurnSynchronizer] Setting turn to {(newTurnValue == 0 ? "White" : "Black")} after move execution");
        }
    }
    
    private void OnVisualPieceMoved(Square startSquare, Transform pieceTransform, Transform endSquareTransform, Piece promotionPiece = null)
    {
        if (!IsServer && !IsHost) return;
        
        // Get the piece side
        VisualPiece visualPiece = pieceTransform.GetComponent<VisualPiece>();
        if (visualPiece == null) return;
        
        // Toggle the turn after a move
        int newTurn = visualPiece.PieceColor == Side.White ? 1 : 0;
        currentTurn.Value = newTurn;
        
        if (verbose) Debug.Log($"[TurnSynchronizer] {visualPiece.PieceColor} moved, changing turn to {(newTurn == 0 ? "White" : "Black")}");
    }
    
    [ClientRpc]
    public void SyncTurnStateClientRpc(int turnValue)
    {
        if (verbose) Debug.Log($"[TurnSynchronizer] Received turn sync: {(turnValue == 0 ? "White" : "Black")}");
        
        // Force refresh pieces interactivity
        if (networkManager != null)
        {
            networkManager.RefreshAllPiecesInteractivity();
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void RequestTurnChangeServerRpc(int fromTurn, int toTurn)
    {
        if (!IsServer && !IsHost) return;
        
        if (currentTurn.Value == fromTurn)
        {
            currentTurn.Value = toTurn;
            if (verbose) Debug.Log($"[TurnSynchronizer] Turn changed via request: {fromTurn} -> {toTurn}");
        }
        else
        {
            if (verbose) Debug.LogWarning($"[TurnSynchronizer] Invalid turn change request: current={currentTurn.Value}, from={fromTurn}, to={toTurn}");
        }
    }
    
    // Helper method to check if it's a player's turn
    public bool IsPlayerTurn(Side playerSide)
    {
        return (currentTurn.Value == 0 && playerSide == Side.White) ||
               (currentTurn.Value == 1 && playerSide == Side.Black);
    }
}