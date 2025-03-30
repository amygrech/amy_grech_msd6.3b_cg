using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityChess;

/// <summary>
/// Fixed version of ChessNetworkManager that handles the initial network setup properly.
/// </summary>
public class ChessNetworkManager : MonoBehaviourSingleton<ChessNetworkManager> {
    [Header("Network UI")]
    [SerializeField] private GameObject networkPanel;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private InputField joinCodeInputField;
    [SerializeField] private Text connectionStatusText;

    [Header("Game References")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private NetworkManager networkManager;

    // Track the local player's side
    private Side localPlayerSide = Side.White;
    private bool gameStarted = false;

    private void Start() {
        // Set up event listeners
        if (hostButton != null) hostButton.onClick.AddListener(StartHost);
        if (clientButton != null) clientButton.onClick.AddListener(StartClient);
        if (disconnectButton != null) disconnectButton.onClick.AddListener(Disconnect);

        // Subscribe to network events
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        } else {
            Debug.LogError("NetworkManager.Singleton is null. Make sure the NetworkManager is in the scene.");
        }

        // Ensure the disconnect button is initially disabled
        if (disconnectButton != null) disconnectButton.enabled = false;
        
        // Display the network panel on start
        if (networkPanel != null) networkPanel.SetActive(true);

        UpdateConnectionStatus("Not Connected");
    }

    private void OnDestroy() {
        // Unsubscribe from network events
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    /// <summary>
    /// Starts a network session as the host.
    /// </summary>
    public void StartHost() {
        if (NetworkManager.Singleton == null) {
            Debug.LogError("NetworkManager.Singleton is null. Cannot start host.");
            return;
        }

        // Make sure scene objects are registered before starting the host
        RegisterSceneNetworkObjects();

        // Start the host
        if (NetworkManager.Singleton.StartHost()) {
            localPlayerSide = Side.White;
            
            // Hide the network panel after successful connection
            if (networkPanel != null) networkPanel.SetActive(false);
            if (disconnectButton != null) disconnectButton.enabled = true;
            if (hostButton != null) hostButton.enabled = false;
            if (clientButton != null) clientButton.enabled = false;
            
            UpdateConnectionStatus("Hosting");
            
            // Start a new game after a short delay
            Invoke("StartNetworkGame", 0.5f);
        } else {
            UpdateConnectionStatus("Failed to start host");
        }
    }

    /// <summary>
    /// Joins an existing network session as a client.
    /// </summary>
    public void StartClient() {
        if (NetworkManager.Singleton == null) {
            Debug.LogError("NetworkManager.Singleton is null. Cannot start client.");
            return;
        }

        // Make sure scene objects are registered before starting the client
        RegisterSceneNetworkObjects();

        // Start the client
        if (NetworkManager.Singleton.StartClient()) {
            localPlayerSide = Side.Black;
            
            // Hide the network panel after attempting connection
            if (networkPanel != null) networkPanel.SetActive(false);
            if (disconnectButton != null) disconnectButton.enabled = true;
            if (hostButton != null) hostButton.enabled = false;
            if (clientButton != null) clientButton.enabled = false;
            
            UpdateConnectionStatus("Connecting...");
        } else {
            UpdateConnectionStatus("Failed to start client");
        }
    }

    /// <summary>
    /// Registers all scene network objects to prevent duplicate ID issues.
    /// </summary>
    private void RegisterSceneNetworkObjects() {
        // Get all NetworkObjects in the scene
        NetworkObject[] networkObjects = FindObjectsOfType<NetworkObject>();
        
        Debug.Log($"Found {networkObjects.Length} NetworkObjects in scene");
        
        // Make sure they all have unique GlobalObjectIdHash values
        HashSet<uint> seenIds = new HashSet<uint>();
        foreach (NetworkObject netObj in networkObjects) {
            if (seenIds.Contains(netObj.GlobalObjectIdHash)) {
                Debug.LogWarning($"Duplicate GlobalObjectIdHash found: {netObj.name} has ID {netObj.GlobalObjectIdHash}");
            } else {
                seenIds.Add(netObj.GlobalObjectIdHash);
            }
        }
    }

    /// <summary>
    /// Starts the network game after connection is established.
    /// </summary>
    private void StartNetworkGame() {
        if (!gameStarted && NetworkManager.Singleton.IsHost) {
            gameStarted = true;
            GameManager.Instance.StartNewGame();
        }
    }

    /// <summary>
    /// Disconnects from the current network session.
    /// </summary>
    public void Disconnect() {
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.Shutdown();
        }
        
        // Show the network panel again
        if (networkPanel != null) networkPanel.SetActive(true);
        if (disconnectButton != null) disconnectButton.enabled = false;
        if (hostButton != null) hostButton.enabled = true;
        if (clientButton != null) clientButton.enabled = true;
        
        UpdateConnectionStatus("Disconnected");
        gameStarted = false;
    }

    /// <summary>
    /// Callback when a client connects to the network.
    /// </summary>
    private void OnClientConnected(ulong clientId) {
        if (clientId == NetworkManager.Singleton.LocalClientId) {
            UpdateConnectionStatus(NetworkManager.Singleton.IsHost ? "Hosting" : "Connected as Client");
            
            // If we're the client, we should disable all the pieces initially
            if (!NetworkManager.Singleton.IsHost) {
                DisableAllPiecesTemporarily();
            }
        } else {
            // A remote client connected
            UpdateConnectionStatus("Player Connected");
            
            // If we're the host, sync the game state to the new client
            if (NetworkManager.Singleton.IsHost) {
                SyncGameStateClientRpc();
            }
        }
    }

    /// <summary>
    /// Temporarily disables all pieces until game state is synchronized.
    /// </summary>
    private void DisableAllPiecesTemporarily() {
        BoardManager.Instance.SetActiveAllPieces(false);
        // Re-enable pieces after the game state is synced
        Invoke("EnableCorrectPieces", 1.0f);
    }

    /// <summary>
    /// Enables only the pieces that the client should be able to move.
    /// </summary>
    private void EnableCorrectPieces() {
        BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(localPlayerSide);
    }

    /// <summary>
    /// Callback when a client disconnects from the network.
    /// </summary>
    private void OnClientDisconnected(ulong clientId) {
        if (clientId == NetworkManager.Singleton.LocalClientId) {
            // We disconnected
            UpdateConnectionStatus("Disconnected");
            gameStarted = false;
        } else {
            // Remote client disconnected
            UpdateConnectionStatus("Player Disconnected");
        }
    }

    /// <summary>
    /// Updates the connection status text in the UI.
    /// </summary>
    private void UpdateConnectionStatus(string status) {
        if (connectionStatusText != null) {
            connectionStatusText.text = $"Status: {status}";
        }
        Debug.Log($"Network Status: {status}");
    }

    /// <summary>
    /// Gets the side of the local player (White or Black).
    /// </summary>
    public Side GetLocalPlayerSide() {
        return localPlayerSide;
    }

    /// <summary>
    /// Syncs the current game state to all clients.
    /// </summary>
    [ClientRpc]
    public void SyncGameStateClientRpc() {
        if (!NetworkManager.Singleton.IsHost) {
            // Load the serialized game state from the host
            string serializedGame = GameManager.Instance.SerializeGame();
            GameManager.Instance.LoadGame(serializedGame);
            
            // Make sure only the correct pieces are enabled
            BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(localPlayerSide);
        }
    }

    /// <summary>
    /// Checks if the current player can move the specified piece.
    /// </summary>
    public bool CanMoveCurrentPiece(Side pieceSide) {
        // In single player mode, allow moving any piece
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient) {
            return true;
        }

        // In network mode, only allow moving pieces of your designated side
        return pieceSide == localPlayerSide;
    }

    /// <summary>
    /// Sends a move to all clients when a player makes a move.
    /// </summary>
    [ClientRpc]
    public void BroadcastMoveClientRpc(string serializedMove) {
        if (!NetworkManager.Singleton.IsHost) {
            // Apply the move on the client side
            GameManager.Instance.LoadGame(serializedMove);
            
            // Make sure only the correct pieces are enabled after the move
            BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(localPlayerSide);
        }
    }
}