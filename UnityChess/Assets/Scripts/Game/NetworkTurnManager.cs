using Unity.Netcode;
using UnityChess;
using UnityEngine;

public class NetworkTurnManager : NetworkBehaviour
{
    // Network variable to track whose turn it is (0 for White, 1 for Black)
    // Making this public allows direct inspection in the Unity Editor
    public NetworkVariable<int> currentTurn = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Network variable to control if movement is allowed
    public NetworkVariable<bool> canMove = new NetworkVariable<bool>(true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Make sure we have a reference to the ChessNetworkManager
    private ChessNetworkManager networkManager;

    [SerializeField] private bool debugMode = true;

    // Reference to the BoardSynchronizer component
    private BoardSynchronizer boardSynchronizer;

    private void Awake()
    {
        Debug.Log("[NetworkTurnManager] Awake called");
        networkManager = FindObjectOfType<ChessNetworkManager>();

        if (networkManager == null)
        {
            Debug.LogError("[NetworkTurnManager] Could not find ChessNetworkManager in the scene!");
        }

        // Get reference to BoardSynchronizer
        boardSynchronizer = FindObjectOfType<BoardSynchronizer>();
        if (boardSynchronizer == null)
        {
            Debug.LogWarning("[NetworkTurnManager] BoardSynchronizer not found. Turn synchronization may be affected.");
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        Debug.Log("[NetworkTurnManager] OnNetworkSpawn called. IsServer: " + (IsServer || IsHost));

        if (IsServer || IsHost)
        {
            Debug.Log("[NetworkTurnManager] Initializing on server: White's turn");
            currentTurn.Value = 0; // White starts
            canMove.Value = true;
        }

        // Subscribe to NetworkVariable changes
        currentTurn.OnValueChanged += OnTurnChanged;
        canMove.OnValueChanged += OnCanMoveChanged;

        // Subscribe to game move events to update the turn automatically
        GameManager.MoveExecutedEvent += OnGameMoveExecuted;

        // Subscribe to visual piece moved events to handle turn changes
        VisualPiece.VisualPieceMoved += OnVisualPieceMoved;

        // Force initial refresh
        if (networkManager != null)
        {
            networkManager.RefreshAllPiecesInteractivity();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Unsubscribe from events
        currentTurn.OnValueChanged -= OnTurnChanged;
        canMove.OnValueChanged -= OnCanMoveChanged;
        GameManager.MoveExecutedEvent -= OnGameMoveExecuted;
        VisualPiece.VisualPieceMoved -= OnVisualPieceMoved;
    }

    private void OnTurnChanged(int previousValue, int newValue)
    {
        Debug.Log(
            $"[NetworkTurnManager] Turn changed from {(previousValue == 0 ? "White" : "Black")} to {(newValue == 0 ? "White" : "Black")}");

        // Force immediate refresh of all pieces to ensure correct interactivity
        if (networkManager != null)
        {
            networkManager.RefreshAllPiecesInteractivity();
        }
    }

    private void OnCanMoveChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[NetworkTurnManager] CanMove changed from {previousValue} to {newValue}");

        // Update piece interactivity whenever movement permissions change
        if (networkManager != null)
        {
            networkManager.RefreshAllPiecesInteractivity();
        }
    }

    /// <summary>
    /// Monitors when a visual piece is moved and handles turn changes
    /// </summary>
    private void OnVisualPieceMoved(Square startSquare, Transform pieceTransform, Transform endSquareTransform,
        Piece promotionPiece = null)
    {
        if (debugMode) Debug.Log($"[NetworkTurnManager] Piece moved from {startSquare} to {endSquareTransform.name}");

        // Handle automatic turn changing on successful moves
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
        {
            VisualPiece visualPiece = pieceTransform.GetComponent<VisualPiece>();
            if (visualPiece != null)
            {
                Side moveSide = visualPiece.PieceColor;

                // FIX: Always process turn changes consistently
                // If host moved White piece
                if (IsHost && moveSide == Side.White)
                {
                    if (debugMode) Debug.Log("[NetworkTurnManager] Host moved White, changing turn to Black");
                    // Check if the move is legal using game manager
                    Square endSquare = new Square(endSquareTransform.name);
                    if (GameManager.Instance.TryGetLegalMove(startSquare, endSquare, out _))
                    {
                        // Change turn after a short delay to allow move to complete
                        Invoke("SwitchToBlackTurn", 0.5f);
                    }
                }
                // If client moved Black piece
                else if (!IsHost && !IsServer && moveSide == Side.Black)
                {
                    if (debugMode)
                        Debug.Log("[NetworkTurnManager] Client moved Black, requesting turn change to White");
                    // Request turn change to server
                    RequestTurnChangeServerRpc(1, 0); // Switch from Black (1) to White (0)
                }
            }
        }
    }

    /// <summary>
    /// Called when a move is executed in the game
    /// </summary>
    private void OnGameMoveExecuted()
    {
        if (IsServer || IsHost)
        {
            // Update the current turn based on the game state
            UpdateTurnFromGameState();
        }
    }

    /// <summary>
    /// Updates the turn based on the current game state
    /// </summary>
    private void UpdateTurnFromGameState()
    {
        if (IsServer || IsHost)
        {
            Side currentSide = GameManager.Instance.SideToMove;
            int newTurnValue = currentSide == Side.White ? 0 : 1;

            // FIX: Always update turn to match game state, regardless of current value
            Debug.Log($"[NetworkTurnManager] Updating turn from game state: {currentSide}");
            currentTurn.Value = newTurnValue;

            // Notify clients about the turn change
            UpdateTurnStateClientRpc(newTurnValue);
        }
    }

    /// <summary>
    /// Switches the turn to Black (used by Invoke)
    /// </summary>
    private void SwitchToBlackTurn()
    {
        if (IsHost || IsServer)
        {
            Debug.Log("[NetworkTurnManager] Switching turn to Black");
            currentTurn.Value = 1; // Black's turn
            UpdateTurnStateClientRpc(1);
        }
    }

    /// <summary>
    /// Switches the turn to White (used by Invoke)
    /// </summary>
    private void SwitchToWhiteTurn()
    {
        if (IsHost || IsServer)
        {
            Debug.Log("[NetworkTurnManager] Switching turn to White");
            currentTurn.Value = 0; // White's turn
            UpdateTurnStateClientRpc(0);
        }
    }

    /// <summary>
    /// Client requests server to change turns - with improved error handling
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestTurnChangeServerRpc(int fromTurn, int toTurn)
    {
        if (!IsServer && !IsHost) return;

        Debug.Log($"[NetworkTurnManager] Server received turn change request: {fromTurn} to {toTurn}");

        // Verify the current game state
        Side currentGameSide = GameManager.Instance.SideToMove;
        int currentGameTurn = currentGameSide == Side.White ? 0 : 1;

        // If the game state and network turn are out of sync, trust the game state
        if (currentTurn.Value != currentGameTurn)
        {
            Debug.LogWarning(
                $"[NetworkTurnManager] Network turn ({currentTurn.Value}) doesn't match game state ({currentGameTurn}). Correcting...");
            currentTurn.Value = currentGameTurn;
        }

        // FIX: More permissive turn change logic - respect the client request
        // as long as it makes logical sense in the game context
        if (toTurn != currentTurn.Value)
        {
            // Change turn
            Debug.Log($"[NetworkTurnManager] Changing turn to {(toTurn == 0 ? "White" : "Black")}");
            currentTurn.Value = toTurn;
            canMove.Value = true;
            UpdateTurnStateClientRpc(toTurn);
        }
        else
        {
            Debug.Log($"[NetworkTurnManager] Turn is already {(toTurn == 0 ? "White" : "Black")}, no change needed");
        }
    }

    // This is the critical method that determines if a player can move
    public bool CanPlayerMove(Side playerSide)
    {
        if (!canMove.Value)
        {
            if (debugMode) Debug.Log($"[NetworkTurnManager] Movement locked for all players");
            return false;
        }

        bool isPlayersTurn = (currentTurn.Value == 0 && playerSide == Side.White) ||
                             (currentTurn.Value == 1 && playerSide == Side.Black);

        if (debugMode)
            Debug.Log(
                $"[NetworkTurnManager] Turn check: {playerSide} can move: {isPlayersTurn} (Current turn: {(currentTurn.Value == 0 ? "White" : "Black")})");
        return isPlayersTurn;
    }

    // Lock movement during transitions or game end
    public void LockMovement()
    {
        if (IsServer || IsHost)
        {
            Debug.Log("[NetworkTurnManager] Movement has been locked by host/server");
            canMove.Value = false;
        }
        else
        {
            LockMovementServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void LockMovementServerRpc()
    {
        Debug.Log("[NetworkTurnManager] Movement has been locked via ServerRpc");
        canMove.Value = false;
    }

    // Unlock movement (usually after state sync)
    public void UnlockMovement()
    {
        if (IsServer || IsHost)
        {
            Debug.Log("[NetworkTurnManager] Movement has been unlocked by host/server");
            canMove.Value = true;
        }
        else
        {
            UnlockMovementServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void UnlockMovementServerRpc()
    {
        Debug.Log("[NetworkTurnManager] Movement has been unlocked via ServerRpc");
        canMove.Value = true;
    }

    // End the current turn and switch to the other player
    public void EndTurn()
    {
        Debug.Log("[NetworkTurnManager] EndTurn called. IsServer: " + (IsServer || IsHost));

        if (IsServer || IsHost)
        {
            // Direct server-side call
            ChangeCurrentTurn();
        }
        else
        {
            // Client call via RPC
            EndTurnServerRpc();
        }
    }

    // Server-side implementation of turn change
    private void ChangeCurrentTurn()
    {
        Debug.Log(
            $"[NetworkTurnManager] Changing turn: {(currentTurn.Value == 0 ? "White" : "Black")} -> {(currentTurn.Value == 0 ? "Black" : "White")}");

        // Toggle turn
        currentTurn.Value = currentTurn.Value == 0 ? 1 : 0;
        canMove.Value = true;

        // Notify clients
        UpdateTurnStateClientRpc(currentTurn.Value);
    }

    [ServerRpc(RequireOwnership = false)]
    public void EndTurnServerRpc()
    {
        Debug.Log("[NetworkTurnManager] EndTurnServerRpc called");
        if (!IsServer && !IsHost) return;

        ChangeCurrentTurn();
    }

    [ClientRpc]
    private void UpdateTurnStateClientRpc(int newTurn)
    {
        // Update UI or game state based on new turn
        string currentPlayer = newTurn == 0 ? "White" : "Black";
        Debug.Log($"[NetworkTurnManager] Turn changed to: {currentPlayer}");

        // Perform an immediate refresh of piece interactivity
        if (ChessNetworkManager.Instance != null)
        {
            ChessNetworkManager.Instance.RefreshAllPiecesInteractivity();
        }
    }

    // Force-refresh all pieces (can be called from anywhere)
    [ClientRpc]
    public void ForceRefreshPiecesClientRpc()
    {
        Debug.Log("[NetworkTurnManager] ForceRefreshPiecesClientRpc called");
        if (networkManager != null)
        {
            networkManager.RefreshAllPiecesInteractivity();
        }
    }

    /// <summary>
    /// Synchronizes the turn state with the game manager
    /// </summary>
    public void SyncWithGameState()
    {
        if (IsServer || IsHost)
        {
            Side currentSide = GameManager.Instance.SideToMove;
            currentTurn.Value = currentSide == Side.White ? 0 : 1;
            ForceRefreshPiecesClientRpc();
        }
    }

    public void ChangeCurrentTurn(int turnValue)
    {
        if (!IsServer && !IsHost) return;

        Debug.Log($"[NetworkTurnManager] Directly changing turn to {(turnValue == 0 ? "White" : "Black")}");

        // FIX: Always update turn when requested, to ensure proper turn cycling
        // Set the new turn value
        currentTurn.Value = turnValue;
        canMove.Value = true;

        // Notify clients
        UpdateTurnStateClientRpc(turnValue);

        // Make sure pieces are refreshed
        ForceRefreshPiecesClientRpc();
    }
}