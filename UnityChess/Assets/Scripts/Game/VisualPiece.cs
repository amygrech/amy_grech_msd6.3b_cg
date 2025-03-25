using System.Collections.Generic;
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
    public delegate void VisualPieceMovedAction(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null);

    // Static event raised when a visual piece is moved.
    public static event VisualPieceMovedAction VisualPieceMoved;

    // The colour (side) of the piece (White or Black).
    public Side PieceColor;

    /// <summary>
    /// Retrieves the current board square of the piece.
    /// It checks the parent's name if available, otherwise falls back to the stored initial square.
    /// </summary>
    public Square CurrentSquare {
        get {
            if (transform.parent != null)
                return StringToSquare(transform.parent.name);
            else
                return initialSquare;
        }
    }

    // Stores the initial square from which the piece was picked up.
    private Square initialSquare;
    // Stores the initial parent transform.
    private Transform initialParent;

    // The radius used to detect nearby board squares for collision detection.
    private const float SquareCollisionRadius = 9f;

    // The camera used to view the board.
    private Camera boardCamera;
    // The screen-space position of the piece when it is first picked up.
    private Vector3 piecePositionSS;
    // A reference to the piece's SphereCollider (if required for collision handling).
    private SphereCollider pieceBoundingSphere;
    // A list to hold potential board square GameObjects that the piece might land on.
    private List<GameObject> potentialLandingSquares;
    // A cached reference to the transform of this piece.
    private Transform thisTransform;

    /// <summary>
    /// Initialises the visual piece. Sets up necessary variables and obtains a reference to the main camera.
    /// </summary>
    private void Start() {
        potentialLandingSquares = new List<GameObject>();
        thisTransform = transform;
        boardCamera = Camera.main;
    }

    /// <summary>
    /// Called when the user presses the mouse button over the piece.
    /// Records the initial screen-space position and stores the piece's original parent and square.
    /// </summary>
    public void OnMouseDown() {
        if (enabled) {
            piecePositionSS = boardCamera.WorldToScreenPoint(transform.position);
            if (transform.parent != null) {
                initialParent = transform.parent;
                initialSquare = StringToSquare(transform.parent.name);
            }
            else {
                Debug.LogError("VisualPiece: OnMouseDown - transform.parent is null.");
            }
        }
    }

    /// <summary>
    /// Called while the user drags the piece with the mouse.
    /// Updates the piece's world position to follow the mouse cursor.
    /// </summary>
    private void OnMouseDrag() {
        if (enabled) {
            Vector3 nextPiecePositionSS = new Vector3(Input.mousePosition.x, Input.mousePosition.y, piecePositionSS.z);
            thisTransform.position = boardCamera.ScreenToWorldPoint(nextPiecePositionSS);
        }
    }

    /// <summary>
    /// Called when the user releases the mouse button after dragging the piece.
    /// Determines the closest board square to the piece and raises an event with the move.
    /// </summary>
    public void OnMouseUp() {
        if (enabled) {
            potentialLandingSquares.Clear();
            // Obtain all square GameObjects within the collision radius of the piece's current position.
            BoardManager.Instance.GetSquareGOsWithinRadius(potentialLandingSquares, thisTransform.position, SquareCollisionRadius);

            // If no squares are found, assume the piece was moved off the board and reset its position.
            if (potentialLandingSquares.Count == 0) {
                if (initialParent != null) {
                    transform.parent = initialParent;
                    thisTransform.position = initialParent.position;
                }
                else {
                    thisTransform.position = Vector3.zero;
                }
                return;
            }
    
            // Determine the closest square.
            Transform closestSquareTransform = potentialLandingSquares[0].transform;
            float shortestDistanceFromPieceSquared = (closestSquareTransform.position - thisTransform.position).sqrMagnitude;
            
            for (int i = 1; i < potentialLandingSquares.Count; i++) {
                GameObject potentialLandingSquare = potentialLandingSquares[i];
                float distanceFromPieceSquared = (potentialLandingSquare.transform.position - thisTransform.position).sqrMagnitude;
                if (distanceFromPieceSquared < shortestDistanceFromPieceSquared) {
                    shortestDistanceFromPieceSquared = distanceFromPieceSquared;
                    closestSquareTransform = potentialLandingSquare.transform;
                }
            }

            // Raise the VisualPieceMoved event using the stored initial square.
            VisualPieceMoved?.Invoke(initialSquare, thisTransform, closestSquareTransform);
        }
    }
}
