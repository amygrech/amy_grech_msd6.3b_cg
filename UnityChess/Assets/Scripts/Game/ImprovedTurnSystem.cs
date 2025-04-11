using UnityEngine;
using Unity.Netcode;
using UnityChess;

/// <summary>
/// Manages turn-based logic in the networked chess game, ensuring only the correct player can move.
/// Coordinates with other network components for proper turn cycling.
/// </summary>
public class ImprovedTurnSystem : NetworkBehaviour
{
    // Network variable to track whose turn it is (0 for White, 1 for Black)
    public NetworkVariable<int> currentTurn = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Network variable to control if movement is allowed (e.g., during transitions)
    public NetworkVariable<bool> canMove = new NetworkVariable<bool>(true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // References to key components
    private ChessNetworkManager networkManager;
    private BoardSynchronizer boardSynchronizer;
    
    [SerializeField] private bool verbose = true;
    
    public NetworkVariable<bool> moveInProgress = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
        
    // Lock interactivity during move transitions
    public NetworkVariable<bool> lockInteractivity = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
        
    // Add a minimum time between allowed moves (in seconds)
    [SerializeField] private float moveCooldown = 0.5f;
    
    // Temporarily lock pieces during transitions
    public void LockPiecesForTransition(float duration = 0.5f)
    {
        if (!IsServer && !IsHost) return;
        
        lockInteractivity.Value = true;
        StartCoroutine(UnlockPiecesAfterDelay(duration));
    }

    private void Awake()
    {
        Debug.Log("[ImprovedTurnSystem] Awake called");
        networkManager = FindObjectOfType<ChessNetworkManager>();
        boardSynchronizer = FindObjectOfType<BoardSynchronizer>();

        if (networkManager == null)
        {
            Debug.LogError("[ImprovedTurnSystem] ChessNetworkManager not found!");
        }
        
        if (boardSynchronizer == null)
        {
            Debug.LogWarning("[ImprovedTurnSystem] BoardSynchronizer not found. Synchronization may be affected.");
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        Debug.Log("[ImprovedTurnSystem] OnNetworkSpawn called. IsServer: " + (IsServer || IsHost));

        if (IsServer || IsHost)
        {
            // Initialize turn state - White starts
            currentTurn.Value = 0;
            canMove.Value = true;
            Debug.Log("[ImprovedTurnSystem] Server initialized: White's turn");
        }

        // Subscribe to network variable changes
        currentTurn.OnValueChanged += OnTurnChanged;
        canMove.OnValueChanged += OnCanMoveChanged;

        // Subscribe to game events
        GameManager.MoveExecutedEvent += OnGameMoveExecuted;
        VisualPiece.VisualPieceMoved += OnVisualPieceMoved;

        // Force initial refresh
        if (networkManager != null)
        {
            StartCoroutine(DelayedRefresh(0.2f));
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Unsubscribe from all events
        currentTurn.OnValueChanged -= OnTurnChanged;
        canMove.OnValueChanged -= OnCanMoveChanged;
        GameManager.MoveExecutedEvent -= OnGameMoveExecuted;
        VisualPiece.VisualPieceMoved -= OnVisualPieceMoved;
    }

    /// <summary>
    /// Delayed refresh to ensure all components are initialized
    /// </summary>
    private System.Collections.IEnumerator DelayedRefresh(float delay)
    {
        yield return new WaitForSeconds(delay);
        RefreshInteractivity();
    }

    /// <summary>
    /// Called when the turn value changes
    /// </summary>
    private void OnTurnChanged(int previousValue, int newValue)
    {
        string prevTurn = previousValue == 0 ? "White" : "Black";
        string newTurn = newValue == 0 ? "White" : "Black";
        Debug.Log($"[ImprovedTurnSystem] Turn changed from {prevTurn} to {newTurn}");

        // Update all piece interactivity
        RefreshInteractivity();
    }

    /// <summary>
    /// Called when the canMove value changes
    /// </summary>
    private void OnCanMoveChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[ImprovedTurnSystem] CanMove changed from {previousValue} to {newValue}");
        RefreshInteractivity();
    }

    /// <summary>
    /// Refreshes piece interactivity based on current turn state
    /// </summary>
    private void RefreshInteractivity()
    {
        if (networkManager != null)
        {
            networkManager.RefreshAllPiecesInteractivity();
        }
    }

    /// <summary>
    /// Called when a visual piece is moved
    /// </summary>
    private void OnVisualPieceMoved(Square startSquare, Transform pieceTransform, Transform endSquareTransform, Piece promotionPiece = null)
    {
        if (!IsServer && !IsHost) return;

        // Get the piece side
        VisualPiece visualPiece = pieceTransform.GetComponent<VisualPiece>();
        if (visualPiece == null) return;

        // Get the piece side (White or Black)
        Side pieceSide = visualPiece.PieceColor;

        if (verbose)
            Debug.Log($"[ImprovedTurnSystem] Piece moved: {pieceSide} from {startSquare} to {endSquareTransform.name}");

        // Toggle turn based on which side just moved
        if (pieceSide == Side.White)
        {
            // If White moved, change to Black's turn
            SetTurn(1); // Black's turn
        }
        else
        {
            // If Black moved, change to White's turn
            SetTurn(0); // White's turn
        }
    }

    /// <summary>
    /// Called when a move is executed in the game
    /// </summary>
    private void OnGameMoveExecuted()
    {
        if (!IsServer && !IsHost) return;

        // Ensure turn state matches GameManager
        SyncWithGameState();
    }

    /// <summary>
    /// Synchronizes the turn state with the GameManager
    /// </summary>
    public void SyncWithGameState()
    {
        if (!IsServer && !IsHost) return;

        Side currentSide = GameManager.Instance.SideToMove;
        int newTurnValue = currentSide == Side.White ? 0 : 1;
        
        if (currentTurn.Value != newTurnValue)
        {
            Debug.Log($"[ImprovedTurnSystem] Syncing turn state - Setting to {(newTurnValue == 0 ? "White" : "Black")}");
            currentTurn.Value = newTurnValue;
            SyncTurnStateClientRpc(newTurnValue);
        }
    }

    /// <summary>
    /// Sets the current turn
    /// </summary>
    public void SetTurn(int turnValue)
    {
        if (!IsServer && !IsHost) return;

        Debug.Log($"[ImprovedTurnSystem] Setting turn to {(turnValue == 0 ? "White" : "Black")}");
        
        // Lock pieces briefly during turn change
        LockPiecesForTransition(moveCooldown);
        
        // Update turn value
        currentTurn.Value = turnValue;
        
        // Notify clients
        SyncTurnStateClientRpc(turnValue);
    }

    /// <summary>
    /// Client requests the server to change the turn
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestTurnChangeServerRpc(int fromTurn, int toTurn)
    {
        if (!IsServer && !IsHost) return;

        Debug.Log($"[ImprovedTurnSystem] Turn change requested: {fromTurn} → {toTurn}");

        // Only allow valid turn transitions
        if (currentTurn.Value == fromTurn)
        {
            SetTurn(toTurn);
        }
        else
        {
            Debug.LogWarning($"[ImprovedTurnSystem] Invalid turn change request. Current={currentTurn.Value}, From={fromTurn}, To={toTurn}");
        }
    }

    /// <summary>
    /// Notifies clients about turn state changes
    /// </summary>
    [ClientRpc]
    public void SyncTurnStateClientRpc(int turnValue)
    {
        if (verbose)
            Debug.Log($"[ImprovedTurnSystem] Received turn sync: {(turnValue == 0 ? "White" : "Black")}");

        // Non-host clients update UI and interactivity
        if (!IsHost && !IsServer)
        {
            RefreshInteractivity();
        }
    }

    /// <summary>
    /// Checks if a player can move based on their side and current turn
    /// </summary>
    public bool CanPlayerMove(Side playerSide)
    {
        // If movement is locked or a move is in progress, no one can move
        if (lockInteractivity.Value || moveInProgress.Value)
        {
            if (verbose) Debug.Log($"[ImprovedTurnSystem] Movement locked - lockInteractivity:{lockInteractivity.Value}, moveInProgress:{moveInProgress.Value}");
            return false;
        }

        // Otherwise use the normal turn logic
        bool isPlayersTurn = (currentTurn.Value == 0 && playerSide == Side.White) ||
                             (currentTurn.Value == 1 && playerSide == Side.Black);

        if (verbose && !isPlayersTurn)
        {
            Debug.Log($"[ImprovedTurnSystem] Not {playerSide}'s turn (Current turn: {(currentTurn.Value == 0 ? "White" : "Black")})");
        }

        return isPlayersTurn;
    }
    
    private System.Collections.IEnumerator UnlockPiecesAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        lockInteractivity.Value = false;
        
        // Force refresh pieces
        ForceRefreshPiecesClientRpc();
    }

    /// <summary>
    /// Forces a refresh of all piece interactivity
    /// </summary>
    [ClientRpc]
    public void ForceRefreshPiecesClientRpc()
    {
        Debug.Log("[ImprovedTurnSystem] Forcing refresh of all pieces");
        RefreshInteractivity();
    }
    
    public void BeginMove()
    {
        if (!IsServer && !IsHost) 
        {
            BeginMoveServerRpc();
            return;
        }
        
        moveInProgress.Value = true;
    }
    
    public void EndMove()
    {
        if (!IsServer && !IsHost) 
        {
            EndMoveServerRpc();
            return;
        }
        
        moveInProgress.Value = false;
        
        // FIX: Explicitly synchronize turns with the game state after a move completes
        SyncWithGameState();
        
        // Add a brief lock to prevent immediate move after turn change
        LockPiecesForTransition(moveCooldown);
        
        // Force refresh all pieces on both sides to ensure proper interactivity
        ForceRefreshPiecesClientRpc();
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void BeginMoveServerRpc()
    {
        moveInProgress.Value = true;
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void EndMoveServerRpc()
    {
        moveInProgress.Value = false;
        
        // Add a brief lock to prevent immediate move after turn change
        LockPiecesForTransition(moveCooldown);
    }
}
