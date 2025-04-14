using System.Collections.Generic;
using Unity.Netcode;
using UnityChess;
using UnityEngine;
using static UnityChess.SquareUtil;

/// <summary>
/// Represents a visual chess piece in the game. This component handles user interaction,
/// such as dragging and dropping pieces, and determines the closest square on the board
/// where the piece should land. It also raises an event when a piece has been moved.
/// </summary>
public class VisualPiece : MonoBehaviour {
	// Delegate for handling the event when a visual piece has been moved.
	// Parameters: the initial square of the piece, its transform, the closest square's transform,
	// and an optional promotion piece.
	public delegate void VisualPieceMovedAction(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null);
	
	// Static event raised when a visual piece is moved.
	public static event VisualPieceMovedAction VisualPieceMoved;

    // Flag to indicate if we should cancel default move processing (for network games)
    public static bool CancelMoveProcessing = false;
	
	// The colour (side) of the piece (White or Black).
	public Side PieceColor;
	
	// Retrieves the current board square of the piece by converting its parent's name into a Square.
	public Square CurrentSquare => StringToSquare(transform.parent.name);
	
	// The radius used to detect nearby board squares for collision detection.
	private const float SquareCollisionRadius = 9f;
	
	// The camera used to view the board.
	private Camera boardCamera;
	// The screen-space position of the piece when it is first picked up.
	private Vector3 piecePositionSS;
	// A list to hold potential board square GameObjects that the piece might land on.
	private List<GameObject> potentialLandingSquares;
	// A cached reference to the transform of this piece.
	private Transform thisTransform;
    // Track whether the piece is being dragged
    private bool isDragging = false;
    // Original parent of the piece
    private Transform originalParent;
    
    // Debug flag
    [SerializeField] private bool debugMode = false;
    
    // Reference to the BoardSynchronizer
    private BoardSynchronizer boardSynchronizer;
    // Reference to the NetworkTurnManager
    private ImprovedTurnSystem turnSystem;
    
    // Track if a move is in progress
    private bool moveInProgress = false;
    
    // Track moves we've already processed to prevent duplicates
    private static HashSet<string> processedMoves = new HashSet<string>();

	/// <summary>
	/// Initialises the visual piece. Sets up necessary variables and obtains a reference to the main camera.
	/// </summary>
	private void Start() {
		// Initialise the list to hold potential landing squares.
		potentialLandingSquares = new List<GameObject>();
		// Cache the transform of this GameObject for efficiency.
		thisTransform = transform;
		// Obtain the main camera from the scene.
		boardCamera = Camera.main;
		
		// Find the BoardSynchronizer
        boardSynchronizer = FindObjectOfType<BoardSynchronizer>();
        if (boardSynchronizer == null && debugMode) {
            Debug.LogWarning("BoardSynchronizer not found in scene. Network synchronization might not work properly.");
        }
        
        // Find the NetworkTurnManager
        turnSystem = FindObjectOfType<ImprovedTurnSystem>();
        if (turnSystem == null && debugMode) {
            Debug.LogWarning("NetworkTurnManager not found in scene. Turn management might not work properly.");
        }
	}

	/// <summary>
	/// Called when the user presses the mouse button over the piece.
	/// Records the initial screen-space position of the piece.
	/// </summary>
	public void OnMouseDown() {
		if (enabled) {
			// Check if we're in a networked game
			if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient) {
				// Only allow dragging if it's this player's turn and piece
				if (!ChessNetworkManager.Instance.CanMoveCurrentPiece(PieceColor)) {
					if (debugMode) Debug.Log($"Cannot move piece: {PieceColor} - not your turn or piece");
					return;
				}
				
				// FIXED: Also check if a move is in progress
				if (turnSystem != null && (turnSystem.moveInProgress.Value || turnSystem.lockInteractivity.Value)) {
                    if (debugMode) Debug.Log($"Cannot move piece: {PieceColor} - move in progress or locked");
                    return;
                }
                
                // FIXED: If we're already processing a move, don't allow starting another
                if (moveInProgress) {
                    if (debugMode) Debug.Log($"Cannot move piece: {PieceColor} - local move in progress");
                    return;
                }
			}
        
			// Convert the world position of the piece to screen-space and store it.
			piecePositionSS = boardCamera.WorldToScreenPoint(transform.position);
            isDragging = true;
            originalParent = transform.parent;
            
            if (debugMode) Debug.Log($"Starting to drag {PieceColor} piece from {CurrentSquare}");
		}
	}

	/// <summary>
	/// Called while the user drags the piece with the mouse.
	/// Updates the piece's world position to follow the mouse cursor.
	/// </summary>
	private void OnMouseDrag() {
		if (enabled && isDragging) {
			// Create a new screen-space position based on the current mouse position,
			// preserving the original depth (z-coordinate).
			Vector3 nextPiecePositionSS = new Vector3(Input.mousePosition.x, Input.mousePosition.y, piecePositionSS.z);
			// Convert the screen-space position back to world-space and update the piece's position.
			thisTransform.position = boardCamera.ScreenToWorldPoint(nextPiecePositionSS);
		}
	}

	/// <summary>
	/// Called when the user releases the mouse button after dragging the piece.
	/// Determines the closest board square to the piece and raises an event with the move.
	/// </summary>
	public void OnMouseUp() {
		if (enabled && isDragging) {
            // Reset dragging state
            isDragging = false;
            CancelMoveProcessing = false;
        
			// Clear any previous potential landing square candidates.
			potentialLandingSquares.Clear();
			// Obtain all square GameObjects within the collision radius of the piece's current position.
			BoardManager.Instance.GetSquareGOsWithinRadius(potentialLandingSquares, thisTransform.position, SquareCollisionRadius);

			// If no squares are found, assume the piece was moved off the board and reset its position.
			if (potentialLandingSquares.Count == 0) { // piece moved off board
				thisTransform.position = thisTransform.parent.position;
				return;
			}
	
			// Determine the closest square from the list of potential landing squares.
			Transform closestSquareTransform = potentialLandingSquares[0].transform;
			// Calculate the square of the distance between the piece and the first candidate square.
			float shortestDistanceFromPieceSquared = (closestSquareTransform.position - thisTransform.position).sqrMagnitude;
			
			// Iterate through remaining potential squares to find the closest one.
			for (int i = 1; i < potentialLandingSquares.Count; i++) {
				GameObject potentialLandingSquare = potentialLandingSquares[i];
				// Calculate the squared distance from the piece to the candidate square.
				float distanceFromPieceSquared = (potentialLandingSquare.transform.position - thisTransform.position).sqrMagnitude;

				// If the current candidate is closer than the previous closest, update the closest square.
				if (distanceFromPieceSquared < shortestDistanceFromPieceSquared) {
					shortestDistanceFromPieceSquared = distanceFromPieceSquared;
					closestSquareTransform = potentialLandingSquare.transform;
				}
			}

            Square startSquare = CurrentSquare;
            Square endSquare = new Square(closestSquareTransform.name);
            
            // Generate a unique ID for this move
            string moveId = $"{startSquare}-{endSquare}";
            
            // Check if we've already processed this move
            if (processedMoves.Contains(moveId))
            {
                if (debugMode) Debug.Log($"Skipping already processed move: {moveId}");
                thisTransform.position = originalParent.position;
                return;
            }
            
            // Track this move to prevent double processing
            processedMoves.Add(moveId);
            
            // Limit the size of the processed moves set
            if (processedMoves.Count > 20)
            {
                processedMoves.Clear();
            }
            
            if (debugMode) Debug.Log($"Attempting to move {PieceColor} piece from {startSquare} to {endSquare}");
            
            // Check if the move is legal using GameManager
            bool isLegalMove = GameManager.Instance.TryGetLegalMove(startSquare, endSquare, out Movement move);
            
            if (!isLegalMove) {
                // Reset piece position if move is not legal
                thisTransform.position = originalParent.position;
                return;
            }
            
            // FIXED: Mark that a move is in progress locally
            moveInProgress = true;
            
            // CRITICAL FIX: If we're in a networked game, directly notify about the move
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient && boardSynchronizer != null) {
                if (NetworkManager.Singleton.IsHost) {
                    // If we're the host (White), notify clients directly about our move
                    if (debugMode) Debug.Log($"[HOST] Directly notifying clients about move from {startSquare} to {endSquare}");
                    boardSynchronizer.NotifyClientOfMoveClientRpc(startSquare.ToString(), endSquare.ToString());
                    
                    // Explicitly change turn to Black after White's move
                    if (turnSystem != null && PieceColor == Side.White) {
                        StartCoroutine(DelayedTurnChange(0.5f, 1)); // Change to Black (1) after delay
                    }
                } else {
                    // If we're a client (Black), notify the host directly about our move
                    if (debugMode) Debug.Log($"[CLIENT] Directly notifying host about move from {startSquare} to {endSquare}");
                    boardSynchronizer.NotifyHostOfMoveServerRpc(startSquare.ToString(), endSquare.ToString());
                    
                    // CRITICAL FIX: Explicitly request turn change to White after Black's move
                    if (turnSystem != null && PieceColor == Side.Black) {
                        StartCoroutine(DelayedTurnChange(0.5f, 0)); // Request change to White (0) after delay
                    }
                }
                
                // CRITICAL FIX: Execute the move in the GameManager to update game state
                GameManager.Instance.ExecuteMove(move);
            }

			// Raise the VisualPieceMoved event with the initial square, the piece's transform, and the closest square transform.
			VisualPieceMoved?.Invoke(startSquare, thisTransform, closestSquareTransform);
            
            // Move the piece directly to ensure it's visible on both sides
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient) {
                // Directly update our own visual position to avoid any sync issues
                Transform targetSquareTransform = BoardManager.Instance.GetSquareGOByPosition(endSquare).transform;
                thisTransform.SetParent(targetSquareTransform);
                thisTransform.localPosition = Vector3.zero;
            }
            
            // Clear the move in progress after a short delay
            StartCoroutine(ClearMoveInProgressAfterDelay(1.0f));
		}
	}
    
    /// <summary>
    /// Helper coroutine to clear the move in progress state
    /// </summary>
    private System.Collections.IEnumerator ClearMoveInProgressAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        moveInProgress = false;
    }
    
    /// <summary>
    /// Helper coroutine to change turn after a delay
    /// </summary>
    private System.Collections.IEnumerator DelayedTurnChange(float delay, int newTurn) {
        yield return new WaitForSeconds(delay);
        
        if (turnSystem != null) {
            if (NetworkManager.Singleton.IsHost) {
                // If we're the host, change turn directly
                turnSystem.SetTurn(newTurn);
                Debug.Log($"[HOST] Changed turn to {(newTurn == 0 ? "White" : "Black")} after delay");
            } else {
                // If we're the client, request turn change from server
                int currentTurn = turnSystem.currentTurn.Value;
                turnSystem.RequestTurnChangeServerRpc(currentTurn, newTurn);
                Debug.Log($"[CLIENT] Requested turn change to {(newTurn == 0 ? "White" : "Black")} after delay");
            }
            
            // Force refresh piece interactivity
            ChessNetworkManager.Instance.RefreshAllPiecesInteractivity();
        }
    }
}