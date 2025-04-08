using UnityEngine;
using Unity.Netcode;
using UnityChess;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// Handles synchronization of chess board state between server and clients.
/// Ensures the server is authoritative for validating moves and updating game state.
/// </summary>
public class BoardSynchronizer : NetworkBehaviour
{
    private GameManager gameManager;
    private BoardManager boardManager;
    private ChessNetworkManager networkManager;
    private NetworkTurnManager turnManager;

    // Time between board state updates (seconds)
    [SerializeField] private float syncInterval = 0.1f;
    private float syncTimer = 0f;
    
    // Used to track last synchronized state
    private string lastSyncedState = string.Empty;
    private int lastSyncedMoveCount = -1;
    
    [SerializeField] private bool verbose = true;
    
    // Reference to the OnPieceMoved method in GameManager using reflection
    private MethodInfo onPieceMovedMethod;

    private void Awake()
    {
        gameManager = GameManager.Instance;
        boardManager = BoardManager.Instance;
        networkManager = ChessNetworkManager.Instance;
        turnManager = FindObjectOfType<NetworkTurnManager>();
    
        if (gameManager == null || boardManager == null || networkManager == null) {
            Debug.LogError("[BoardSynchronizer] Missing required component references!");
            enabled = false;
            return;
        }
    
        // Get the OnPieceMoved method via reflection since it's private
        onPieceMovedMethod = typeof(GameManager).GetMethod("OnPieceMoved", 
            BindingFlags.NonPublic | BindingFlags.Instance);
    
        if (onPieceMovedMethod == null) {
            Debug.LogError("[BoardSynchronizer] Could not find OnPieceMoved method via reflection!");
            enabled = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
    
        Debug.Log("BoardSynchronizer spawned with NetworkObjectId: " + NetworkObjectId);
    
        if (IsServer || IsHost)
        {
            // Subscribe to move executed event to sync the board after valid moves
            GameManager.MoveExecutedEvent += OnMoveExecuted;
        }
    
        // Subscribe to visual piece moved events to ensure proper visual updates
        VisualPiece.VisualPieceMoved += OnVisualPieceMoved;
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
        VisualPiece.VisualPieceMoved -= OnVisualPieceMoved;
    }

    /// <summary>
    /// Intercepts visual piece movement to update both clients
    /// </summary>
    private void OnVisualPieceMoved(Square startSquare, Transform pieceTransform, Transform endSquareTransform, Piece promotionPiece = null)
    {
        Debug.Log($"[BoardSynchronizer] OnVisualPieceMoved - From: {startSquare}, To: {endSquareTransform.name}, IsHost: {IsHost}, IsServer: {IsServer}");
        
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
        {
            // Get the piece and piece side
            VisualPiece visualPiece = pieceTransform.GetComponent<VisualPiece>();
            if (visualPiece == null) return;
            
            Side pieceSide = visualPiece.PieceColor;
            Square endSquare = new Square(endSquareTransform.name);
            
            // If client (Black player) made a move
            if (!IsHost && !IsServer && pieceSide == Side.Black)
            {
                // Send direct move sync to the host
                NotifyHostOfMoveServerRpc(startSquare.ToString(), endSquare.ToString());
                
                // Request turn change after move completes
                if (turnManager != null)
                {
                    // Allow a small delay for move completion
                    Invoke("RequestTurnChangeToWhite", 0.5f);
                }
            }
            
            // If host (White player) made a move
            if (IsHost && pieceSide == Side.White)
            {
                // Send direct move sync to the client
                NotifyClientOfMoveClientRpc(startSquare.ToString(), endSquare.ToString());
                
                // Change turn after move completes
                if (turnManager != null)
                {
                    // Allow a small delay for move completion
                    Invoke("ChangeTurnToBlack", 0.5f);
                }
            }
        }
    }
    
    /// <summary>
    /// Helper method to request turn change to White (called via Invoke)
    /// </summary>
    private void RequestTurnChangeToWhite()
    {
        // Get current game state
        Side currentGameSide = gameManager.SideToMove;
        int currentGameTurn = currentGameSide == Side.White ? 0 : 1;
    
        // Only request turn change if truly needed
        if (currentGameTurn == 1) // If game thinks it's Black's turn
        {
            if (turnManager != null)
            {
                Debug.Log("[BoardSynchronizer] Requesting turn change from Black to White");
                turnManager.RequestTurnChangeServerRpc(1, 0); // From Black (1) to White (0)
            }
        }
        else
        {
            Debug.Log("[BoardSynchronizer] Turn already White in game state, no request needed");
        }
    }
    
    /// <summary>
    /// Helper method to change turn to Black (called via Invoke)
    /// </summary>
    private void ChangeTurnToBlack()
    {
        if (turnManager != null && (IsHost || IsServer))
        {
            turnManager.ChangeCurrentTurn(1); // Change to Black (1)
        }
    }

    private void Update()
    {
        // Only the server periodically synchronizes board state
        if (IsServer || IsHost)
        {
            syncTimer += Time.deltaTime;
            if (syncTimer >= syncInterval)
            {
                syncTimer = 0f;
                
                // Check if state has changed
                string currentState = gameManager.SerializeGame();
                int currentMoveCount = gameManager.LatestHalfMoveIndex;
                
                if (currentState != lastSyncedState || currentMoveCount != lastSyncedMoveCount)
                {
                    lastSyncedState = currentState;
                    lastSyncedMoveCount = currentMoveCount;
                    SyncBoardStateClientRpc(currentState);
                    if (verbose) Debug.Log("[SERVER] Board state synchronized to clients");
                }
            }
        }
    }

    /// <summary>
    /// Called when a move is executed in the game
    /// </summary>
    private void OnMoveExecuted()
    {
        if (IsServer || IsHost)
        {
            // Immediately sync the board state after a move
            string currentState = gameManager.SerializeGame();
            lastSyncedState = currentState;
            lastSyncedMoveCount = gameManager.LatestHalfMoveIndex;
            SyncBoardStateClientRpc(currentState);
            if (verbose) Debug.Log("[SERVER] Move executed, board state synchronized");
            
            // FIX: Also sync turn state with game state
            if (turnManager != null) {
                Side currentSide = gameManager.SideToMove;
                int newTurnValue = currentSide == Side.White ? 0 : 1;
                turnManager.ChangeCurrentTurn(newTurnValue);
                Debug.Log($"[SERVER] Turn synchronized to match game state: {currentSide}");
            }
        }
    }

    /// <summary>
    /// Directly updates the visual position of a piece (critical fix)
    /// </summary>
    private void DirectlyMovePiece(string startSquareStr, string endSquareStr)
    {
        Debug.Log($"[DIRECT MOVE] Moving piece from {startSquareStr} to {endSquareStr}");
        
        try
        {
            // Convert squares
            Square startSquare = SquareUtil.StringToSquare(startSquareStr);
            Square endSquare = SquareUtil.StringToSquare(endSquareStr);
            
            // Get the piece at the start square
            GameObject pieceGO = boardManager.GetPieceGOAtPosition(startSquare);
            if (pieceGO != null)
            {
                // Capture any piece at the destination
                boardManager.TryDestroyVisualPiece(endSquare);
                
                // Move the piece directly
                Transform endSquareTransform = boardManager.GetSquareGOByPosition(endSquare).transform;
                pieceGO.transform.SetParent(endSquareTransform);
                pieceGO.transform.localPosition = Vector3.zero;
                
                Debug.Log($"[DIRECT MOVE] Successfully moved piece from {startSquareStr} to {endSquareStr}");
            }
            else
            {
                // If the piece wasn't found at the start position, recreate it at the end position
                Debug.LogWarning($"[DIRECT MOVE] Could not find piece at {startSquareStr}, checking if we need to create at {endSquareStr}");
                
                // Get the piece information from the current board state
                Piece piece = gameManager.CurrentBoard[endSquare];
                if (piece != null)
                {
                    // Remove any existing piece at the destination
                    boardManager.TryDestroyVisualPiece(endSquare);
                    
                    // Create a new piece at the destination
                    boardManager.CreateAndPlacePieceGO(piece, endSquare);
                    Debug.Log($"[DIRECT MOVE] Created new piece at {endSquareStr}");
                }
            }
            
            // Force refresh piece interactivity
            networkManager.RefreshAllPiecesInteractivity();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[DIRECT MOVE] Error moving piece: {ex.Message}");
        }
    }

    /// <summary>
    /// Synchronizes the board state from server to clients
    /// </summary>
    [ClientRpc]
    public void SyncBoardStateClientRpc(string serializedGameState) {
        if (!IsServer && !IsHost) {
            if (verbose) Debug.Log("[CLIENT] Received board state from server");
        
            // Apply the serialized state to update the client's game
            gameManager.LoadGame(serializedGameState);
        
            // IMPORTANT: Force refresh piece interactivity
            if (ChessNetworkManager.Instance != null) {
                // Use a short delay to ensure the game state is fully updated
                StartCoroutine(DelayedRefresh());
            }
        }
    }

    private System.Collections.IEnumerator DelayedRefresh() {
        // Small delay to ensure game state is fully updated
        yield return new WaitForSeconds(0.1f);
        ChessNetworkManager.Instance.RefreshAllPiecesInteractivity();
    }

    /// <summary>
    /// Client (Black) notifies Host about a move
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void NotifyHostOfMoveServerRpc(string startSquare, string endSquare)
    {
        Debug.Log($"[SERVER] Received direct move notification: {startSquare} to {endSquare}");
        
        if (IsHost)
        {
            // Directly update the piece position on the host
            DirectlyMovePiece(startSquare, endSquare);
            
            // FIX: Ensure turn changes after receiving a move from client
            if (turnManager != null)
            {
                // After client (Black) moves, it should be White's turn
                StartCoroutine(DelayedTurnChange(0.5f, 0)); // Change to White (0) after delay
            }
        }
    }
    
    /// <summary>
    /// Host (White) notifies Client about a move
    /// </summary>
    [ClientRpc]
    public void NotifyClientOfMoveClientRpc(string startSquare, string endSquare)
    {
        Debug.Log($"[CLIENT] Received direct move notification: {startSquare} to {endSquare}");
        
        if (!IsHost && !IsServer)
        {
            // Directly update the piece position on the client
            DirectlyMovePiece(startSquare, endSquare);
            
            // FIX: Ensure turn changes on client after receiving a move from host
            if (turnManager != null)
            {
                // After host (White) moves, it should be Black's turn
                StartCoroutine(DelayedTurnChange(0.5f, 1)); // Change to Black (1) after delay
            }
        }
    }
    
    /// <summary>
    /// Helper coroutine to change turn after a delay
    /// </summary>
    private System.Collections.IEnumerator DelayedTurnChange(float delay, int newTurn)
    {
        yield return new WaitForSeconds(delay);
        
        if (IsHost || IsServer)
        {
            // If we're the host/server, change turn directly
            if (turnManager != null)
            {
                Debug.Log($"[SERVER] Changing turn to {(newTurn == 0 ? "White" : "Black")} after delay");
                turnManager.ChangeCurrentTurn(newTurn);
            }
        }
        else
        {
            // If we're the client, request turn change from server
            if (turnManager != null)
            {
                Debug.Log($"[CLIENT] Requesting turn change to {(newTurn == 0 ? "White" : "Black")} after delay");
                int currentTurn = turnManager.currentTurn.Value;
                turnManager.RequestTurnChangeServerRpc(currentTurn, newTurn);
            }
        }
        
        // Always refresh piece interactivity after turn change
        if (networkManager != null)
        {
            networkManager.RefreshAllPiecesInteractivity();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestMoveValidationServerRpc(ulong clientId, string startSquare, string endSquare, string serializedGameState)
    {
        if (!IsServer && !IsHost) return;
        
        if (verbose) Debug.Log($"[SERVER] Received move validation request from client {clientId}: {startSquare} to {endSquare}");
        
        // Get player info
        PlayerConnectionManager.PlayerInfo playerInfo = networkManager.GetPlayerConnectionManager().GetPlayerInfo(clientId);
        if (playerInfo == null)
        {
            Debug.LogError($"[SERVER] Player info not found for client {clientId}");
            RejectMoveClientRpc(clientId, startSquare, endSquare);
            return;
        }
        
        // Get the current side to move from the CURRENT server state
        Side currentSide = gameManager.SideToMove;
        
        if (verbose) Debug.Log($"[SERVER] Current turn: {currentSide}, Player side: {playerInfo.AssignedSide}");
        
        // Verify it's the client's turn
        if (playerInfo.AssignedSide != currentSide)
        {
            Debug.Log($"[SERVER] Move rejected - wrong player's turn. Current side: {currentSide}, Player side: {playerInfo.AssignedSide}");
            RejectMoveClientRpc(clientId, startSquare, endSquare);
            return;
        }
        
        // Save current state in case we need to revert
        string previousState = gameManager.SerializeGame();
        
        try {
            // Apply the client's move to the server state
            gameManager.LoadGame(serializedGameState);
            
            // Check if the move is valid by checking if the side to move has changed
            if (gameManager.SideToMove != currentSide) {
                // The move changed the side to move, which indicates it was applied
                if (verbose) Debug.Log("[SERVER] Client move validated and applied");
                
                // EMERGENCY FIX: Apply the move visually on the host
                DirectlyMovePiece(startSquare, endSquare);
                
                // The new state will be synchronized to all clients
                OnMoveExecuted();
                
                // Notify the client that the move was accepted
                AcceptMoveClientRpc(clientId);
                
                // Update turn after successful move
                if (turnManager != null && currentSide == Side.Black)
                {
                    turnManager.RequestTurnChangeServerRpc(1, 0); // From Black to White
                }
            } else {
                // The side to move didn't change, which means the move wasn't applied properly
                Debug.LogError("[SERVER] Client move was not applied correctly");
                gameManager.LoadGame(previousState); // Restore previous state
                RejectMoveClientRpc(clientId, startSquare, endSquare);
            }
        }
        catch (System.Exception ex) {
            // If there's an error applying the move, reject it
            Debug.LogError($"[SERVER] Error validating move: {ex.Message}");
            gameManager.LoadGame(previousState); // Restore previous state
            RejectMoveClientRpc(clientId, startSquare, endSquare);
        }
    }

    /// <summary>
    /// Notifies a client that their move was accepted
    /// </summary>
    [ClientRpc]
    public void AcceptMoveClientRpc(ulong clientId)
    {
        // Only process this on the client that sent the move
        if (clientId != NetworkManager.Singleton.LocalClientId) return;
        
        if (verbose) Debug.Log($"[CLIENT] Move was accepted by server");
    }

    // Add this event for notifying when a move has been executed
    public static event System.Action MoveExecutedEvent;
    
    /// <summary>
    /// Notifies a client that their move was rejected
    /// </summary>
    [ClientRpc]
    public void RejectMoveClientRpc(ulong clientId, string startSquare, string endSquare)
    {
        // Only process this on the client that sent the move
        if (clientId != NetworkManager.Singleton.LocalClientId) return;
        
        if (verbose) Debug.Log($"[CLIENT] Move from {startSquare} to {endSquare} was rejected");
        
        // Return the piece to its original position
        Square start = SquareUtil.StringToSquare(startSquare);
        GameObject pieceGO = boardManager.GetPieceGOAtPosition(start);
        
        if (pieceGO != null)
        {
            // Reset the piece position
            Transform squareTransform = boardManager.GetSquareGOByPosition(start).transform;
            pieceGO.transform.SetParent(squareTransform);
            pieceGO.transform.localPosition = Vector3.zero;
        }
        
        // Ensure the full board gets synchronized
        SyncBoardStateClientRpc(gameManager.SerializeGame());
    }
}