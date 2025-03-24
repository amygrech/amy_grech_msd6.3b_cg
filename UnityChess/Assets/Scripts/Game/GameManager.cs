using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityChess;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Manages the overall chess game state and move execution in a networked environment.
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    // Events signaling game state changes.
    public static event Action NewGameStartedEvent;
    public static event Action GameEndedEvent;
    public static event Action GameResetToHalfMoveEvent;
    public static event Action MoveExecutedEvent;

    public Board CurrentBoard {
        get {
            game.BoardTimeline.TryGetCurrent(out Board currentBoard);
            return currentBoard;
        }
    }

    public Side SideToMove {
        get {
            game.ConditionsTimeline.TryGetCurrent(out GameConditions currentConditions);
            return currentConditions.SideToMove;
        }
    }

    public Side StartingSide => game.ConditionsTimeline[0].SideToMove;
    public Timeline<HalfMove> HalfMoveTimeline => game.HalfMoveTimeline;
    public int LatestHalfMoveIndex => game.HalfMoveTimeline.HeadIndex;
    public int FullMoveNumber => StartingSide switch {
        Side.White => LatestHalfMoveIndex / 2 + 1,
        Side.Black => (LatestHalfMoveIndex + 1) / 2 + 1,
        _ => -1
    };

    private bool isWhiteAI;
    private bool isBlackAI;
    public List<(Square, Piece)> CurrentPieces {
        get {
            currentPiecesBacking.Clear();
            for (int file = 1; file <= 8; file++) {
                for (int rank = 1; rank <= 8; rank++) {
                    Piece piece = CurrentBoard[file, rank];
                    if (piece != null) currentPiecesBacking.Add((new Square(file, rank), piece));
                }
            }
            return currentPiecesBacking;
        }
    }
    private readonly List<(Square, Piece)> currentPiecesBacking = new List<(Square, Piece)>();

    [SerializeField] private UnityChessDebug unityChessDebug;
    private Game game;
    private FENSerializer fenSerializer;
    private PGNSerializer pgnSerializer;
    private CancellationTokenSource promotionUITaskCancellationTokenSource;
    private ElectedPiece userPromotionChoice = ElectedPiece.None;
    private Dictionary<GameSerializationType, IGameSerializer> serializersByType;
    private GameSerializationType selectedSerializationType = GameSerializationType.FEN;

    private void Awake() {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    /// <summary>
    /// Initializes the chess game. Only the server should call this.
    /// </summary>
    public void StartNewGame() {
        if (!IsServer) return;
        serializersByType = new Dictionary<GameSerializationType, IGameSerializer> {
            [GameSerializationType.FEN] = new FENSerializer(),
            [GameSerializationType.PGN] = new PGNSerializer()
        };
        game = new Game();
        NewGameStartedEvent?.Invoke();
#if DEBUG_VIEW
        unityChessDebug.gameObject.SetActive(true);
        unityChessDebug.enabled = true;
#endif
        Debug.Log("GameManager: New game started.");
    }

    public string SerializeGame() {
        return serializersByType.TryGetValue(selectedSerializationType, out IGameSerializer serializer)
            ? serializer?.Serialize(game)
            : null;
    }

    public void LoadGame(string serializedGame) {
        game = serializersByType[selectedSerializationType].Deserialize(serializedGame);
        NewGameStartedEvent?.Invoke();
    }

    public void ResetGameToHalfMoveIndex(int halfMoveIndex) {
        if (!game.ResetGameToHalfMoveIndex(halfMoveIndex)) return;
        UIManager.Instance.SetActivePromotionUI(false);
        promotionUITaskCancellationTokenSource?.Cancel();
        GameResetToHalfMoveEvent?.Invoke();
    }

    private bool TryExecuteMove(Movement move) {
        if (!game.TryExecuteMove(move)) {
            return false;
        }
        HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
        if (latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate) {
            BoardManager.Instance.SetActiveAllPieces(false);
            GameEndedEvent?.Invoke();
        } else {
            BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(SideToMove);
        }
        MoveExecutedEvent?.Invoke();
        return true;
    }

    private async Task<bool> TryHandleSpecialMoveBehaviourAsync(SpecialMove specialMove) {
        switch (specialMove) {
            case CastlingMove castlingMove:
                BoardManager.Instance.CastleRook(castlingMove.RookSquare, castlingMove.GetRookEndSquare());
                return true;
            case EnPassantMove enPassantMove:
                BoardManager.Instance.TryDestroyVisualPiece(enPassantMove.CapturedPawnSquare);
                return true;
            case PromotionMove { PromotionPiece: null } promotionMove:
                UIManager.Instance.SetActivePromotionUI(true);
                BoardManager.Instance.SetActiveAllPieces(false);
                promotionUITaskCancellationTokenSource?.Cancel();
                promotionUITaskCancellationTokenSource = new CancellationTokenSource();
                ElectedPiece choice = await Task.Run(GetUserPromotionPieceChoice, promotionUITaskCancellationTokenSource.Token);
                UIManager.Instance.SetActivePromotionUI(false);
                BoardManager.Instance.SetActiveAllPieces(true);
                if (promotionUITaskCancellationTokenSource == null || promotionUITaskCancellationTokenSource.Token.IsCancellationRequested)
                    return false;
                promotionMove.SetPromotionPiece(PromotionUtil.GeneratePromotionPiece(choice, SideToMove));
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.Start);
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.End);
                BoardManager.Instance.CreateAndPlacePieceGO(promotionMove.PromotionPiece, promotionMove.End);
                promotionUITaskCancellationTokenSource = null;
                return true;
            case PromotionMove promotionMove:
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.Start);
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.End);
                BoardManager.Instance.CreateAndPlacePieceGO(promotionMove.PromotionPiece, promotionMove.End);
                return true;
            default:
                return false;
        }
    }

    private ElectedPiece GetUserPromotionPieceChoice() {
        while (userPromotionChoice == ElectedPiece.None) { }
        ElectedPiece result = userPromotionChoice;
        userPromotionChoice = ElectedPiece.None;
        return result;
    }

    public void ElectPiece(ElectedPiece choice) {
        userPromotionChoice = choice;
    }

    /// <summary>
    /// Returns whether the given piece has any legal moves.
    /// </summary>
    public bool HasLegalMoves(Piece piece) {
        return game.TryGetLegalMovesForPiece(piece, out _);
    }

    /// <summary>
    /// ServerRpc to request execution of a chess move.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestChessMoveServerRpc(string startSquareName, string targetSquareName, ulong pieceNetworkId, ServerRpcParams rpcParams = default) {
        if (!IsServer) return;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(pieceNetworkId, out NetworkObject pieceObj)) {
            Debug.LogWarning("GameManager: Piece with NetworkObjectId " + pieceNetworkId + " not found.");
            return;
        }
        Transform pieceTransform = pieceObj.transform;
        Square startSquare = new Square(startSquareName);
        Square targetSquare = new Square(targetSquareName);

        GameObject targetSquareGO = BoardManager.Instance.GetSquareGOByPosition(targetSquare);
        if (targetSquareGO == null) {
            Debug.LogWarning("GameManager: Target square GameObject for " + targetSquareName + " not found.");
            return;
        }
        Transform targetSquareTransform = targetSquareGO.transform;

        OnPieceMoved(startSquare, pieceTransform, targetSquareTransform, null);

        Vector3 newPosition = targetSquareTransform.position;
        UpdatePiecePositionClientRpc(pieceNetworkId, newPosition);
    }

    [ClientRpc]
    public void UpdatePiecePositionClientRpc(ulong pieceNetworkId, Vector3 newPosition, ClientRpcParams rpcParams = default) {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(pieceNetworkId, out NetworkObject pieceObj)) {
            pieceObj.transform.position = newPosition;
        }
    }

    private async void OnPieceMoved(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null) {
        Square endSquare = new Square(closestBoardSquareTransform.name);
        if (!game.TryGetLegalMove(movedPieceInitialSquare, endSquare, out Movement move)) {
            movedPieceTransform.position = movedPieceTransform.parent.position;
#if DEBUG_VIEW
        Piece movedPiece = CurrentBoard[movedPieceInitialSquare];
        game.TryGetLegalMovesForPiece(movedPiece, out ICollection<Movement> legalMoves);
        UnityChessDebug.ShowLegalMovesInLog(legalMoves);
#endif
            return;
        }
        if (move is PromotionMove promotionMove) {
            promotionMove.SetPromotionPiece(promotionPiece);
        }

        bool specialMoveHandled = true;
        if (move is SpecialMove specialMove) {
            specialMoveHandled = await TryHandleSpecialMoveBehaviourAsync(specialMove);
        } else {
            BoardManager.Instance.TryDestroyVisualPiece(move.End);
        }

        if (specialMoveHandled && TryExecuteMove(move)) {
            if (move is PromotionMove) {
                movedPieceTransform = BoardManager.Instance.GetPieceGOAtPosition(move.End).transform;
            }
            if (movedPieceTransform.GetComponent<NetworkObject>().IsSpawned) {
                movedPieceTransform.SetParent(closestBoardSquareTransform);
                movedPieceTransform.localPosition = Vector3.zero;
            } else {
                Debug.LogWarning("GameManager: NetworkObject is not spawned yet.");
            }
        }
    }
}
