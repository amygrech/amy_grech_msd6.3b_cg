using System;
using System.Collections.Generic;
using UnityChess;
using UnityEngine;
using Unity.Netcode;
using static UnityChess.SquareUtil;

/// <summary>
/// Updated NetworkBoardManager that handles the duplicate object issue.
/// </summary>
public class NetworkBoardManager : NetworkBehaviour {
    private BoardManager baseBoardManager;
    private NetworkObject networkObject;

    // Maps square positions to spawned piece network objects
    private Dictionary<Square, NetworkObject> networkPieceObjects = new Dictionary<Square, NetworkObject>();

    // Cache of piece prefabs for network spawning
    [SerializeField] private GameObject whitePawnPrefab;
    [SerializeField] private GameObject whiteKnightPrefab;
    [SerializeField] private GameObject whiteBishopPrefab;
    [SerializeField] private GameObject whiteRookPrefab;
    [SerializeField] private GameObject whiteQueenPrefab;
    [SerializeField] private GameObject whiteKingPrefab;
    [SerializeField] private GameObject blackPawnPrefab;
    [SerializeField] private GameObject blackKnightPrefab;
    [SerializeField] private GameObject blackBishopPrefab;
    [SerializeField] private GameObject blackRookPrefab;
    [SerializeField] private GameObject blackQueenPrefab;
    [SerializeField] private GameObject blackKingPrefab;

    private bool initialSetupComplete = false;

    private void Awake() {
        // Get reference to the base BoardManager
        baseBoardManager = GetComponent<BoardManager>();
        networkObject = GetComponent<NetworkObject>();
    }

    public override void OnNetworkSpawn() {
        base.OnNetworkSpawn();
        
        // Subscribe to game events
        GameManager.NewGameStartedEvent += OnNewGameStarted;
        GameManager.GameResetToHalfMoveEvent += OnGameResetToHalfMove;
    }

    public override void OnNetworkDespawn() {
        base.OnNetworkDespawn();
        
        // Unsubscribe from game events
        GameManager.NewGameStartedEvent -= OnNewGameStarted;
        GameManager.GameResetToHalfMoveEvent -= OnGameResetToHalfMove;
    }

    /// <summary>
    /// Called when a new game is started.
    /// Spawns all pieces over the network.
    /// </summary>
    private void OnNewGameStarted() {
        // Only the host spawns pieces
        if (!IsHost) return;
        
        // Make sure we don't set up pieces multiple times during the initial game setup
        if (initialSetupComplete) {
            // Clear any existing network pieces
            ClearNetworkPieces();
            
            // Spawn new pieces for all current positions
            SpawnAllNetworkPieces();
        } else {
            // For the first setup, just flag that we're using existing pieces
            initialSetupComplete = true;
            
            // For the initial setup, we'll use the existing scene objects
            // and just sync their positions
            SyncExistingPiecePositions();
        }
    }

    /// <summary>
    /// Called when the game is reset to a specific half-move.
    /// Updates piece positions over the network.
    /// </summary>
    private void OnGameResetToHalfMove() {
        // Only the host updates pieces
        if (!IsHost) return;
        
        // Clear existing network pieces
        ClearNetworkPieces();
        
        // Spawn pieces for the current board state
        SpawnAllNetworkPieces();
    }

    /// <summary>
    /// Synchronizes the positions of existing scene pieces without spawning new ones.
    /// </summary>
    private void SyncExistingPiecePositions() {
        // Get all visual pieces in the scene
        VisualPiece[] scenePieces = FindObjectsOfType<VisualPiece>();
        
        // Create a mapping of pieces by position
        Dictionary<Square, GameObject> piecesByPosition = new Dictionary<Square, GameObject>();
        foreach (VisualPiece visualPiece in scenePieces) {
            Square position = visualPiece.CurrentSquare;
            piecesByPosition[position] = visualPiece.gameObject;
            
            // Store NetworkObject if it exists
            NetworkObject netObj = visualPiece.GetComponent<NetworkObject>();
            if (netObj != null) {
                networkPieceObjects[position] = netObj;
            }
        }
        
        // Sync positions with the current game state
        foreach ((Square square, Piece piece) in GameManager.Instance.CurrentPieces) {
            if (piecesByPosition.TryGetValue(square, out GameObject pieceGO)) {
                // The piece is already at the correct position
                NetworkObject netObj = pieceGO.GetComponent<NetworkObject>();
                if (netObj != null && !netObj.IsSpawned) {
                    netObj.Spawn();
                    
                    // Assign ownership based on piece color
                    if (piece.Owner == Side.White) {
                        // White pieces owned by host (client ID 0)
                        netObj.ChangeOwnership(0);
                    } else {
                        // Black pieces owned by client
                        AssignBlackPieceOwnership(netObj);
                    }
                }
            }
        }
        
        // Inform clients about the initial state
        SyncInitialStateClientRpc();
    }

    /// <summary>
    /// RPC to inform clients about the initial game state.
    /// </summary>
    [ClientRpc]
    private void SyncInitialStateClientRpc() {
        if (!IsHost) {
            // The client should update its game state to match the host
            // This could involve deserializing the game state
            Debug.Log("Client received initial game state sync");
        }
    }

    /// <summary>
    /// Spawns all network pieces based on the current game state.
    /// </summary>
    private void SpawnAllNetworkPieces()
    {
        // First, make sure we don't have any active NetworkObjects from chess pieces
        VisualPiece[] existingPieces = FindObjectsOfType<VisualPiece>();
        foreach (VisualPiece piece in existingPieces)
        {
            NetworkObject netObj = piece.GetComponent<NetworkObject>();
            if (netObj != null && netObj.enabled)
            {
                // Disable the NetworkObject component to prevent conflicts
                netObj.enabled = false;
                Debug.Log($"Disabled conflicting NetworkObject on {piece.name}");
            }
        }
    
        // Now spawn new pieces with proper network registration
        foreach ((Square square, Piece piece) in GameManager.Instance.CurrentPieces)
        {
            SpawnNetworkPiece(piece, square);
        }
    }

    /// <summary>
    /// Spawns a network-synchronized chess piece.
    /// </summary>
    /// <param name="piece">The chess piece to spawn.</param>
    /// <param name="position">The board position for the piece.</param>
    public void SpawnNetworkPiece(Piece piece, Square position) {
        if (!IsHost) return;
        
        // Check if there's already a piece at this position with a NetworkObject
        GameObject existingPieceGO = baseBoardManager.GetPieceGOAtPosition(position);
        if (existingPieceGO != null) {
            NetworkObject existingNetObj = existingPieceGO.GetComponent<NetworkObject>();
            if (existingNetObj != null) {
                // Use the existing NetworkObject instead of spawning a new one
                if (!existingNetObj.IsSpawned) {
                    existingNetObj.Spawn();
                }
                
                networkPieceObjects[position] = existingNetObj;
                
                // Assign ownership based on piece color
                if (piece.Owner == Side.White) {
                    // White pieces owned by host (client ID 0)
                    existingNetObj.ChangeOwnership(0);
                } else {
                    // Black pieces owned by client
                    AssignBlackPieceOwnership(existingNetObj);
                }
                
                return;
            }
        }
        
        // Get the appropriate prefab for the piece
        GameObject prefab = GetPiecePrefab(piece);
        if (prefab == null) {
            Debug.LogError($"No prefab found for piece: {piece.GetType().Name}, Side: {piece.Owner}");
            return;
        }
        
        // Get the square GameObject position
        GameObject squareGO = baseBoardManager.GetSquareGOByPosition(position);
        if (squareGO == null) {
            Debug.LogError($"Square not found for position: {position}");
            return;
        }
        
        // First destroy any existing piece at this position
        baseBoardManager.TryDestroyVisualPiece(position);
        
        // Spawn the piece over the network
        GameObject pieceGO = Instantiate(
            prefab,
            squareGO.transform.position,
            Quaternion.identity,
            squareGO.transform
        );
        
        // Set up the network object
        NetworkObject networkPieceObj = pieceGO.GetComponent<NetworkObject>();
        if (networkPieceObj != null) {
            networkPieceObj.Spawn();
            
            // Store reference to the network object
            networkPieceObjects[position] = networkPieceObj;
            
            // Assign ownership based on piece color
            if (piece.Owner == Side.White) {
                // White pieces owned by host (client ID 0)
                networkPieceObj.ChangeOwnership(0);
            } else {
                // Black pieces owned by client
                AssignBlackPieceOwnership(networkPieceObj);
            }
        }
    }

    /// <summary>
    /// Assigns ownership of a black piece to the first non-host client.
    /// </summary>
    private void AssignBlackPieceOwnership(NetworkObject netObj) {
        // Find the first connected client that isn't the host
        if (NetworkManager.Singleton.ConnectedClientsIds.Count > 1) {
            ulong clientId = 0;
            foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds) {
                if (id != NetworkManager.ServerClientId) {
                    clientId = id;
                    break;
                }
            }
            netObj.ChangeOwnership(clientId);
        }
    }

    /// <summary>
    /// Clears all network-spawned pieces.
    /// </summary>
    private void ClearNetworkPieces() {
        if (!IsHost) return;
        
        foreach (NetworkObject netObj in networkPieceObjects.Values) {
            if (netObj != null && netObj.IsSpawned) {
                netObj.Despawn();
            }
        }
        
        networkPieceObjects.Clear();
    }

    /// <summary>
    /// Moves a networked piece from one square to another.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void MovePieceServerRpc(int startFile, int startRank, int endFile, int endRank) {
        if (!IsHost) return;
        
        Square startSquare = new Square(startFile, startRank);
        Square endSquare = new Square(endFile, endRank);
        
        // Find the network object for the piece
        if (networkPieceObjects.TryGetValue(startSquare, out NetworkObject pieceObj)) {
            // Get the destination square GameObject
            GameObject endSquareGO = baseBoardManager.GetSquareGOByPosition(endSquare);
            if (endSquareGO != null) {
                // Update the piece's parent and position
                pieceObj.transform.SetParent(endSquareGO.transform);
                pieceObj.transform.localPosition = Vector3.zero;
                
                // Update the dictionary to reflect the new position
                networkPieceObjects.Remove(startSquare);
                networkPieceObjects[endSquare] = pieceObj;
                
                // Notify clients about the move
                MovePieceClientRpc(startFile, startRank, endFile, endRank);
            }
        }
    }

    /// <summary>
    /// Notifies clients about a piece movement.
    /// </summary>
    [ClientRpc]
    private void MovePieceClientRpc(int startFile, int startRank, int endFile, int endRank) {
        if (IsHost) return; // Host already moved the piece
        
        Square startSquare = new Square(startFile, startRank);
        Square endSquare = new Square(endFile, endRank);
        
        // Get the piece GameObject
        GameObject pieceGO = baseBoardManager.GetPieceGOAtPosition(startSquare);
        if (pieceGO != null) {
            // Get the destination square GameObject
            GameObject endSquareGO = baseBoardManager.GetSquareGOByPosition(endSquare);
            if (endSquareGO != null) {
                // Update the piece's parent and position
                pieceGO.transform.SetParent(endSquareGO.transform);
                pieceGO.transform.localPosition = Vector3.zero;
            }
        }
    }

    /// <summary>
    /// Gets the appropriate prefab for a piece based on its type and owner.
    /// </summary>
    private GameObject GetPiecePrefab(Piece piece) {
        if (piece.Owner == Side.White) {
            if (piece is Pawn) return whitePawnPrefab;
            if (piece is Knight) return whiteKnightPrefab;
            if (piece is Bishop) return whiteBishopPrefab;
            if (piece is Rook) return whiteRookPrefab;
            if (piece is Queen) return whiteQueenPrefab;
            if (piece is King) return whiteKingPrefab;
        } else {
            if (piece is Pawn) return blackPawnPrefab;
            if (piece is Knight) return blackKnightPrefab;
            if (piece is Bishop) return blackBishopPrefab;
            if (piece is Rook) return blackRookPrefab;
            if (piece is Queen) return blackQueenPrefab;
            if (piece is King) return blackKingPrefab;
        }
        
        return null;
    }
}

