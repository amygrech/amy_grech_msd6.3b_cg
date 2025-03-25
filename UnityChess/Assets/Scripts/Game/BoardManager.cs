using System;
using System.Collections.Generic;
using UnityChess;
using UnityEngine;
using static UnityChess.SquareUtil;

/// <summary>
/// Manages the visual representation of the chess board and piece placement.
/// Inherits from MonoBehaviourSingleton to ensure only one instance exists.
/// </summary>
public class BoardManager : MonoBehaviourSingleton<BoardManager> {
    // Array holding references to all square GameObjects (64 squares for an 8x8 board).
    private readonly GameObject[] allSquaresGO = new GameObject[64];
    // Dictionary mapping board squares to their corresponding GameObjects.
    private Dictionary<Square, GameObject> positionMap;
    // Constant representing the side length of the board plane.
    private const float BoardPlaneSideLength = 14f;
    // Half the side length.
    private const float BoardPlaneSideHalfLength = BoardPlaneSideLength * 0.5f;
    // The vertical offset for placing the board.
    private const float BoardHeight = 1.6f;

    private void Awake() {
        // Subscribe to game events to update the board when a new game starts or is reset.
        GameManager.NewGameStartedEvent += OnNewGameStarted;
        GameManager.GameResetToHalfMoveEvent += OnGameResetToHalfMove;
        
        // Initialise the dictionary mapping squares to GameObjects.
        positionMap = new Dictionary<Square, GameObject>(64);
        Transform boardTransform = transform;
        Vector3 boardPosition = boardTransform.position;
        
        for (int file = 1; file <= 8; file++) {
            for (int rank = 1; rank <= 8; rank++) {
                GameObject squareGO = new GameObject(SquareToString(file, rank)) {
                    transform = {
                        position = new Vector3(
                            boardPosition.x + FileOrRankToSidePosition(file),
                            boardPosition.y + BoardHeight,
                            boardPosition.z + FileOrRankToSidePosition(rank)
                        ),
                        parent = boardTransform
                    },
                    tag = "Square"
                };

                positionMap.Add(new Square(file, rank), squareGO);
                allSquaresGO[(file - 1) * 8 + (rank - 1)] = squareGO;
            }
        }
    }

    private void OnNewGameStarted() {
        ClearBoard();
        foreach ((Square square, Piece piece) in GameManager.Instance.CurrentPieces) {
            CreateAndPlacePieceGO(piece, square);
        }
        EnsureOnlyPiecesOfSideAreEnabled(GameManager.Instance.SideToMove);
    }

    private void OnGameResetToHalfMove() {
        ClearBoard();
        foreach ((Square square, Piece piece) in GameManager.Instance.CurrentPieces) {
            CreateAndPlacePieceGO(piece, square);
        }
        GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
        if (latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate)
            SetActiveAllPieces(false);
        else
            EnsureOnlyPiecesOfSideAreEnabled(GameManager.Instance.SideToMove);
    }

    public void CastleRook(Square rookPosition, Square endSquare) {
        GameObject rookGO = GetPieceGOAtPosition(rookPosition);
        rookGO.transform.parent = GetSquareGOByPosition(endSquare).transform;
        rookGO.transform.localPosition = Vector3.zero;
    }

    /// <summary>
    /// Instantiates and places a piece on the board. Now spawns it as a network object.
    /// </summary>
    public void CreateAndPlacePieceGO(Piece piece, Square position) {
        string modelName = $"{piece.Owner} {piece.GetType().Name}";
        GameObject piecePrefab = Resources.Load("PieceSets/Marble/" + modelName) as GameObject;
        if (piecePrefab == null) {
            Debug.LogWarning("Piece prefab not found for " + modelName);
            return;
        }
        GameObject pieceGO = Instantiate(piecePrefab, positionMap[position].transform);
        pieceGO.transform.localPosition = Vector3.zero;
    }

    public void GetSquareGOsWithinRadius(List<GameObject> squareGOs, Vector3 positionWS, float radius) {
        float radiusSqr = radius * radius;
        foreach (GameObject squareGO in allSquaresGO) {
            if ((squareGO.transform.position - positionWS).sqrMagnitude < radiusSqr)
                squareGOs.Add(squareGO);
        }
    }

    public void SetActiveAllPieces(bool active) {
        VisualPiece[] visualPieces = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece piece in visualPieces)
            piece.enabled = active;
    }

    public void EnsureOnlyPiecesOfSideAreEnabled(Side side) {
        VisualPiece[] visualPieces = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece piece in visualPieces) {
            Piece chessPiece = GameManager.Instance.CurrentBoard[piece.CurrentSquare];
            piece.enabled = piece.PieceColor == side && GameManager.Instance.HasLegalMoves(chessPiece);
        }
    }

    public void TryDestroyVisualPiece(Square position) {
        VisualPiece visualPiece = positionMap[position].GetComponentInChildren<VisualPiece>();
        if (visualPiece != null)
            DestroyImmediate(visualPiece.gameObject);
    }
    
    public GameObject GetPieceGOAtPosition(Square position) {
        GameObject square = GetSquareGOByPosition(position);
        return square.transform.childCount == 0 ? null : square.transform.GetChild(0).gameObject;
    }
    
    private static float FileOrRankToSidePosition(int index) {
        float t = (index - 1) / 7f;
        return Mathf.Lerp(-BoardPlaneSideHalfLength, BoardPlaneSideHalfLength, t);
    }
    
    private void ClearBoard() {
        VisualPiece[] visualPieces = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece piece in visualPieces)
            DestroyImmediate(piece.gameObject);
    }

    public GameObject GetSquareGOByPosition(Square position) =>
        Array.Find(allSquaresGO, go => go.name == SquareToString(position));
}
