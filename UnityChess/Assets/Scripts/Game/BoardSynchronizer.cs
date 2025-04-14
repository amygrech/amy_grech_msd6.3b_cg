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
    private ImprovedTurnSystem turnSystem;

    // Time between board state updates (seconds)
    [SerializeField] private float syncInterval = 0.1f;
    private float syncTimer = 0f;
    
    // Used to track last synchronized state
    private string lastSyncedState = string.Empty;
    private int lastSyncedMoveCount = -1;
    
    [SerializeField] private bool verbose = true;
    
    // Reference to the OnPieceMoved method in GameManager using reflection
    private MethodInfo onPieceMovedMethod;
    
    // Track which moves we've already processed to prevent double processing
    private HashSet<string> processedMoves = new HashSet<string>();

    private void Awake()
    {
        gameManager = GameManager.Instance;
        boardManager = BoardManager.Instance;
        networkManager = ChessNetworkManager.Instance;
        
        // Find the improved turn system
        turnSystem = FindObjectOfType<ImprovedTurnSystem>();
        if (turnSystem == null)
        {
            Debug.LogWarning("[BoardSynchronizer] ImprovedTurnSystem not found. Using fallback turn management.");
        }
    
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
        // Create a unique identifier for this move to track if we've processed it
        string moveId = $"{startSquare}-{endSquareTransform.name}";
        
        // Skip if we've already processed this exact move recently
        if (processedMoves.Contains(moveId))
        {
            if (verbose) Debug.Log($"[BoardSynchronizer] Skipping already processed move: {moveId}");
            return;
        }
        
        // Add to processed moves
        processedMoves.Add(moveId);
        
        // Remove old processed moves if the list gets too large
        if (processedMoves.Count > 20)
        {
            processedMoves.Clear();
        }
        
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
                
                // Begin tracking the move in ImprovedTurnSystem
                if (turnSystem != null)
                {
                    turnSystem.BeginMove();
                }
                
                // End the move after delay
                StartCoroutine(EndMoveAfterDelay(0.5f));
            }
            
            // If host (White player) made a move
            if (IsHost && pieceSide == Side.White)
            {
                // Send direct move sync to the client
                NotifyClientOfMoveClientRpc(startSquare.ToString(), endSquare.ToString());
                
                // Begin tracking the move in ImprovedTurnSystem
                if (turnSystem != null)
                {
                    turnSystem.BeginMove();
                }
                
                // End the move after delay
                StartCoroutine(EndMoveAfterDelay(0.5f));
            }
        }
    }
    
    /// <summary>
    /// Helper method to end a move with a delay to ensure synchronization
    /// </summary>
    private System.Collections.IEnumerator EndMoveAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (turnSystem != null)
        {
            // End the move, which will trigger turn synchronization
            turnSystem.EndMove();
            
            // Force refresh all pieces after a move completes
            networkManager.RefreshAllPiecesInteractivity();
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
            
            // Also sync turn state with game state
            if (turnSystem != null) {
                Side currentSide = gameManager.SideToMove;
                int newTurnValue = currentSide == Side.White ? 0 : 1;
                turnSystem.SetTurn(newTurnValue);
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
            // Create a unique identifier for this move
            string moveId = $"{startSquareStr}-{endSquareStr}";
            
            // Skip if we've already processed this exact move recently
            if (processedMoves.Contains(moveId))
            {
                if (verbose) Debug.Log($"[DIRECT MOVE] Skipping already processed move: {moveId}");
                return;
            }
            
            // Add to processed moves
            processedMoves.Add(moveId);
            
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
            
            // FIXED: Also update the game state to match the visual representation
            // Check if the move is valid in the game logic
            if (gameManager.TryGetLegalMove(startSquare, endSquare, out Movement move))
            {
                // Execute the move in the game logic
                gameManager.ExecuteMove(move);
                Debug.Log($"[DIRECT MOVE] Applied move in game logic from {startSquareStr} to {endSquareStr}");
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
            
            // FIXED: Explicitly update turn state to match the game state
            if (turnSystem != null)
            {
                Side currentSide = gameManager.SideToMove;
                int newTurnValue = currentSide == Side.White ? 0 : 1;
                
                if (verbose) Debug.Log($"[CLIENT] Updating turn to match game state: {currentSide} ({newTurnValue})");
                turnSystem.currentTurn.Value = newTurnValue;
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
            
            // FIXED: Ensure proper turn state after receiving a move from client
            if (turnSystem != null)
            {
                Debug.Log("[SERVER] Client (Black) moved, changing turn to White's turn");
                // Lock movement briefly to prevent any race conditions
                turnSystem.LockPiecesForTransition(0.2f);
                // After client (Black) moves, it should be White's turn
                turnSystem.SetTurn(0); // Change to White (0)
                
                // Force refresh pieces on both sides
                turnSystem.ForceRefreshPiecesClientRpc();
            }
            
            // Force board state sync to ensure client and host are in sync
            string currentState = gameManager.SerializeGame();
            SyncBoardStateClientRpc(currentState);
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
            
            // FIXED: Ensure turn changes on client after receiving a move from host
            if (turnSystem != null)
            {
                Debug.Log("[CLIENT] Host (White) moved, changing turn to Black's turn");
                // After host (White) moves, it should be Black's turn
                turnSystem.SetTurn(1); // Change to Black (1)
                
                // Request turn change on server
                turnSystem.RequestTurnChangeServerRpc(0, 1);
            }
        }
    }
}