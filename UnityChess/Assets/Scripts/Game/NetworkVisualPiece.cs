using System.Collections.Generic;
using UnityChess;
using UnityEngine;
using Unity.Netcode;
using static UnityChess.SquareUtil;

/// <summary>
/// Network-aware chess piece component that handles networked gameplay.
/// This class doesn't inherit from VisualPiece but implements similar functionality
/// with added network support.
/// </summary>
public class NetworkVisualPiece : MonoBehaviour {
    // The network object component attached to this piece
    private NetworkObject networkObject;
    
    // Public property to match VisualPiece
    public Side PieceColor;
    
    // Field for tracking piece state
    private Camera boardCamera;
    private Vector3 piecePositionSS;
    private List<GameObject> potentialLandingSquares;
    private Transform thisTransform;
    
    // We need to define a delegate matching the one in VisualPiece
    public delegate void VisualPieceMovedAction(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null);
    
    /// <summary>
    /// Retrieves the current board square of the piece by converting its parent's name into a Square.
    /// </summary>
    public Square CurrentSquare => StringToSquare(transform.parent.name);
    
    /// <summary>
    /// The radius used to detect nearby board squares for collision detection.
    /// </summary>
    private const float SquareCollisionRadius = 9f;

    /// <summary>
    /// Initializes the network visual piece component.
    /// </summary>
    private void Start() {
        // Initialize fields
        potentialLandingSquares = new List<GameObject>();
        thisTransform = transform;
        boardCamera = Camera.main;
        
        // Get the NetworkObject component
        networkObject = GetComponent<NetworkObject>();
        
        // Try to determine piece color from the game object name
        string objName = gameObject.name.ToLower();
        if (objName.Contains("white")) {
            PieceColor = Side.White;
        } else if (objName.Contains("black")) {
            PieceColor = Side.Black;
        }
    }

    /// <summary>
    /// Called when the user presses the mouse button over the piece.
    /// Records the initial screen-space position of the piece.
    /// </summary>
    private void OnMouseDown() {
        // Check if this is the local player's piece before allowing movement
        if (enabled && ChessNetworkManager.Instance.CanMoveCurrentPiece(PieceColor)) {
            // Record the screen-space position of the piece
            piecePositionSS = boardCamera.WorldToScreenPoint(transform.position);
        }
    }

    /// <summary>
    /// Called while the user drags the piece with the mouse.
    /// Updates the piece's world position to follow the mouse cursor.
    /// </summary>
    private void OnMouseDrag() {
        // Check if this is the local player's piece before allowing movement
        if (enabled && ChessNetworkManager.Instance.CanMoveCurrentPiece(PieceColor)) {
            // Create a new screen-space position based on the current mouse position,
            // preserving the original depth (z-coordinate).
            Vector3 nextPiecePositionSS = new Vector3(Input.mousePosition.x, Input.mousePosition.y, piecePositionSS.z);
            // Convert the screen-space position back to world-space and update the piece's position.
            thisTransform.position = boardCamera.ScreenToWorldPoint(nextPiecePositionSS);
        }
    }

    /// <summary>
    /// Called when the user releases the mouse button after dragging the piece.
    /// Determines the closest board square to the piece and notifies the game manager.
    /// </summary>
    private void OnMouseUp() {
        // Check if this is the local player's piece before allowing movement
        if (enabled && ChessNetworkManager.Instance.CanMoveCurrentPiece(PieceColor)) {
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

            // Find the GameManager and call OnPieceMoved directly
            GameManager gameManager = GameManager.Instance;
            if (gameManager != null) {
                // We need to call OnPieceMoved using reflection since it's private
                System.Type gameManagerType = gameManager.GetType();
                System.Reflection.MethodInfo onPieceMovedMethod = gameManagerType.GetMethod("OnPieceMoved", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (onPieceMovedMethod != null) {
                    onPieceMovedMethod.Invoke(gameManager, new object[] { 
                        CurrentSquare, thisTransform, closestSquareTransform, null 
                    });
                    
                    // If we're in a networked game, synchronize it
                    if (NetworkManager.Singleton.IsConnectedClient) {
                        string serializedGameState = GameManager.Instance.SerializeGame();
                        ChessNetworkManager.Instance.BroadcastMoveClientRpc(serializedGameState);
                    }
                }
            }
        }
    }
}