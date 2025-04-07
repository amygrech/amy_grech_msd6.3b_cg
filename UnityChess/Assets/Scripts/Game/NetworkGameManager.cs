using System;
using System.Collections.Generic;
using UnityChess;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Network-enabled extension of the GameManager that handles synchronizing
/// chess game state across the network.
/// </summary>
public class NetworkGameManager : NetworkBehaviour {
    // Reference to the original game manager
    private GameManager baseGameManager;

    // Network variables to track game state
    private NetworkVariable<int> networkHalfMoveIndex = new NetworkVariable<int>();
    private NetworkVariable<bool> networkGameActive = new NetworkVariable<bool>();

    // Server-side buffer of move history
    private NetworkList<MoveData> moveHistory;

    // Simple data structure to represent moves in the network
    public struct MoveData : INetworkSerializable, IEquatable<MoveData> {
        public int StartSquareFile;
        public int StartSquareRank;
        public int EndSquareFile;
        public int EndSquareRank;
        public int PieceType;  // Use an enum value or integer to represent piece type
        public int OwnerSide;  // 0 for white, 1 for black

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
            serializer.SerializeValue(ref StartSquareFile);
            serializer.SerializeValue(ref StartSquareRank);
            serializer.SerializeValue(ref EndSquareFile);
            serializer.SerializeValue(ref EndSquareRank);
            serializer.SerializeValue(ref PieceType);
            serializer.SerializeValue(ref OwnerSide);
        }

        public bool Equals(MoveData other) {
            return StartSquareFile == other.StartSquareFile &&
                   StartSquareRank == other.StartSquareRank &&
                   EndSquareFile == other.EndSquareFile &&
                   EndSquareRank == other.EndSquareRank &&
                   PieceType == other.PieceType &&
                   OwnerSide == other.OwnerSide;
        }
    }

    private void Awake() {
        // Get a reference to the base GameManager
        baseGameManager = GetComponent<GameManager>();
        
        // Initialize network lists
        moveHistory = new NetworkList<MoveData>();
    }

    public override void OnNetworkSpawn() {
        base.OnNetworkSpawn();

        // Set up event handlers for network variables
        networkHalfMoveIndex.OnValueChanged += OnHalfMoveIndexChanged;
        networkGameActive.OnValueChanged += OnGameActiveChanged;
        moveHistory.OnListChanged += OnMoveHistoryChanged;

        // If we're the host, initialize the game
        if (IsHost) {
            // Start with a new game when the network session begins
            baseGameManager.StartNewGame();
            networkGameActive.Value = true;
        }
    }

    /// <summary>
    /// Called when the half-move index changes on the network.
    /// </summary>
    private void OnHalfMoveIndexChanged(int previousValue, int newValue) {
        if (!IsHost) {
            // Clients should update their game state to match the host
            baseGameManager.ResetGameToHalfMoveIndex(newValue);
        }
    }

    /// <summary>
    /// Called when the game active state changes on the network.
    /// </summary>
    private void OnGameActiveChanged(bool previousValue, bool newValue) {
        if (newValue && !previousValue) {
            // Game was just activated
            Debug.Log("Network game started");
        } else if (!newValue && previousValue) {
            // Game was just deactivated
            Debug.Log("Network game ended");
        }
    }

    /// <summary>
    /// Called when the move history list changes on the network.
    /// </summary>
    private void OnMoveHistoryChanged(NetworkListEvent<MoveData> changeEvent) {
        if (!IsHost) {
            // If a move was added, apply it on the client side
            if (changeEvent.Type == NetworkListEvent<MoveData>.EventType.Add) {
                Debug.Log("Received new move from network");
                // In a complete implementation, we would apply the move from the network data
                // For now, we'll rely on the half-move index synchronization
            }
        }
    }

    /// <summary>
    /// Starts a new networked game.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void StartNewGameServerRpc() {
        if (IsServer) {
            // Clear move history
            moveHistory.Clear();
            
            // Start a new game on the server
            baseGameManager.StartNewGame();
            
            // Update network variables
            networkGameActive.Value = true;
            networkHalfMoveIndex.Value = 0;
            
            // Notify clients to start a new game
            StartNewGameClientRpc();
        }
    }

    /// <summary>
    /// Notifies clients to start a new game.
    /// </summary>
    [ClientRpc]
    private void StartNewGameClientRpc() {
        if (!IsHost) {
            baseGameManager.StartNewGame();
        }
    }

    /// <summary>
    /// Executes a move on the server and synchronizes it to clients.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ExecuteMoveServerRpc(MoveData moveData) {
        if (IsServer) {
            // In a complete implementation, we would convert the MoveData to a Movement
            // and execute it in the game logic
            
            // Add the move to the history
            moveHistory.Add(moveData);
            
            // Update the half-move index
            networkHalfMoveIndex.Value = baseGameManager.LatestHalfMoveIndex;
            
            // Notify clients about the move
            ExecuteMoveClientRpc(moveData);
        }
    }

    /// <summary>
    /// Notifies clients about a move that was executed on the server.
    /// </summary>
    [ClientRpc]
    private void ExecuteMoveClientRpc(MoveData moveData) {
        if (!IsHost) {
            // Convert MoveData to Movement and execute
            // ... existing movement execution code ...

            // Notify ChessNetworkManager about the move
            ChessNetworkManager.Instance.HandleSuccessfulMove();
        
            // Update the UI and piece interactivity
            ChessNetworkManager.Instance.RefreshAllPiecesInteractivity();
        }
    }

    /// <summary>
    /// Resets the game to a specific half-move.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ResetGameToHalfMoveServerRpc(int halfMoveIndex) {
        if (IsServer) {
            baseGameManager.ResetGameToHalfMoveIndex(halfMoveIndex);
            networkHalfMoveIndex.Value = halfMoveIndex;
        }
    }

    /// <summary>
    /// Converts a Movement to a network-friendly MoveData structure.
    /// </summary>
    public static MoveData ConvertToMoveData(Movement move) {
        // We need to get the piece from the current board
        Board currentBoard = GameManager.Instance.CurrentBoard;
        Piece piece = currentBoard[move.Start];
        
        return new MoveData {
            StartSquareFile = move.Start.File,
            StartSquareRank = move.Start.Rank,
            EndSquareFile = move.End.File,
            EndSquareRank = move.End.Rank,
            PieceType = GetPieceTypeAsInt(piece),
            OwnerSide = piece.Owner == Side.White ? 0 : 1
        };
    }

    /// <summary>
    /// Converts a piece type to an integer representation.
    /// </summary>
    private static int GetPieceTypeAsInt(Piece piece) {
        if (piece is Pawn) return 0;
        if (piece is Knight) return 1;
        if (piece is Bishop) return 2;
        if (piece is Rook) return 3;
        if (piece is Queen) return 4;
        if (piece is King) return 5;
        return -1;  // Unknown piece type
    }
}