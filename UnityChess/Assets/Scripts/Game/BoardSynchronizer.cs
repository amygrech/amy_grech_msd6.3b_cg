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
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe from events
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
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
    /// Called by clients to request a move validation from the server
    /// </summary>
    /// <summary>
/// Called by clients to request a move validation from the server
/// </summary>
[ServerRpc(RequireOwnership = false)]
public void ValidateMoveServerRpc(ulong clientId, string startSquare, string endSquare, string pieceType)
{
    if (!IsServer && !IsHost) return;
    
    Debug.Log($"[SERVER] Validating move from {startSquare} to {endSquare} for client {clientId}");
    
    // Convert string coordinates to Square objects
    Square start = SquareUtil.StringToSquare(startSquare);
    Square end = SquareUtil.StringToSquare(endSquare);
    
    // Get the current side to move
    Side currentSide = gameManager.SideToMove;
    
    // Get player info
    PlayerConnectionManager.PlayerInfo playerInfo = networkManager.GetPlayerConnectionManager().GetPlayerInfo(clientId);
    if (playerInfo == null)
    {
        Debug.LogError($"[SERVER] Player info not found for client {clientId}");
        RejectMoveClientRpc(clientId, startSquare, endSquare);
        return;
    }
    
    Debug.Log($"[SERVER] Move from client {clientId}, assigned side: {playerInfo.AssignedSide}, current turn: {currentSide}");
    
    // Check if this client is allowed to move
    if (playerInfo.AssignedSide != currentSide)
    {
        Debug.Log($"[SERVER] Move rejected - wrong player's turn. Current side: {currentSide}, Player side: {playerInfo.AssignedSide}");
        RejectMoveClientRpc(clientId, startSquare, endSquare);
        return;
    }
    
    // Validate the move is for the correct player's piece
    Piece movingPiece = gameManager.CurrentBoard[start];
    if (movingPiece == null || movingPiece.Owner != currentSide)
    {
        if (verbose) Debug.Log($"[SERVER] Move rejected - wrong player's piece. Piece owner: {(movingPiece != null ? movingPiece.Owner.ToString() : "null")}, Current side: {currentSide}");
        
        // Send rejection notification back to the client
        RejectMoveClientRpc(clientId, startSquare, endSquare);
        return;
    }
    
    // Try to validate and execute the move
    try {
        if (onPieceMovedMethod != null)
        {
            // Get the piece GameObject and end square transform
            GameObject pieceGO = boardManager.GetPieceGOAtPosition(start);
            if (pieceGO == null)
            {
                if (verbose) Debug.Log($"[SERVER] Move rejected - piece not found at {start}");
                RejectMoveClientRpc(clientId, startSquare, endSquare);
                return;
            }
            
            Transform pieceTransform = pieceGO.transform;
            Transform endSquareTransform = boardManager.GetSquareGOByPosition(end).transform;
            
            if (verbose) Debug.Log($"[SERVER] Invoking OnPieceMoved on GameManager");
            
            // Call the private OnPieceMoved method on GameManager via reflection
            onPieceMovedMethod.Invoke(gameManager, new object[] { 
                start, pieceTransform, endSquareTransform, null 
            });
            
            // After invoking, check if the move was valid by looking at the current board state
            // If the piece moved, it was valid
            Piece pieceAtEndSquare = gameManager.CurrentBoard[end];
            if (pieceAtEndSquare != null && pieceAtEndSquare.Owner == currentSide)
            {
                if (verbose) Debug.Log($"[SERVER] Move validated and executed");
                
                // The move execution will trigger OnMoveExecuted which will sync the state
                // No need to do anything else here
            }
            else
            {
                if (verbose) Debug.Log($"[SERVER] Move was invalid or rejected by game rules");
                RejectMoveClientRpc(clientId, startSquare, endSquare);
            }
        }
        else
        {
            Debug.LogError("[SERVER] Could not find OnPieceMoved method via reflection");
            RejectMoveClientRpc(clientId, startSquare, endSquare);
        }
    }
    catch (System.Exception ex) {
        Debug.LogError($"[SERVER] Error validating move: {ex.Message}");
        RejectMoveClientRpc(clientId, startSquare, endSquare);
    }
}
    
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