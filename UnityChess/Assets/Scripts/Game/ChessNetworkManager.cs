using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityChess;

/// <summary>
/// Manages the network connectivity for the chess game.
/// Handles hosting, joining, and disconnecting from network sessions.
/// Uses game state synchronization instead of NetworkObject components.
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
    
    // Network variables
    private bool gameStarted = false;
    private float syncTimer = 0f;
    private const float syncInterval = 0.5f; // Sync every half second
    private string lastSyncedState = string.Empty;

    private void Start() {
        // Set up event listeners
        if (hostButton != null) hostButton.onClick.AddListener(StartHost);
        if (clientButton != null) clientButton.onClick.AddListener(StartClient);
        if (disconnectButton != null) disconnectButton.onClick.AddListener(Disconnect);

        // Subscribe to network events
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        // Ensure the disconnect button is initially disabled
        if (disconnectButton != null) disconnectButton.enabled = false;
        
        // Display the network panel on start
        if (networkPanel != null) networkPanel.SetActive(true);

        UpdateConnectionStatus("Not Connected");
    }
    
    private void Update() {
        // Only the host needs to periodically sync game state
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost && gameStarted) {
            syncTimer += Time.deltaTime;
            if (syncTimer >= syncInterval) {
                syncTimer = 0f;
                
                // Get current game state
                string currentState = GameManager.Instance.SerializeGame();
                
                // Only send if state has changed
                if (currentState != lastSyncedState) {
                    lastSyncedState = currentState;
                    SyncGameStateClientRpc(currentState);
                    Debug.Log("Game state synchronized to clients");
                }
            }
        }
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
        if (NetworkManager.Singleton.StartHost()) {
            localPlayerSide = Side.White;
            
            // Hide the network panel after successful connection
            if (networkPanel != null) networkPanel.SetActive(false);
            if (disconnectButton != null) disconnectButton.enabled = true;
            if (hostButton != null) hostButton.enabled = false;
            if (clientButton != null) clientButton.enabled = false;
            
            UpdateConnectionStatus("Hosting");
            
            // Start a new game
            GameManager.Instance.StartNewGame();
            gameStarted = true;
            
            // Initial sync
            lastSyncedState = GameManager.Instance.SerializeGame();
        } else {
            UpdateConnectionStatus("Failed to start host");
        }
    }

    /// <summary>
    /// Joins an existing network session as a client.
    /// </summary>
    public void StartClient() {
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
    /// Disconnects from the current network session.
    /// </summary>
    public void Disconnect() {
        gameStarted = false;
        
        NetworkManager.Singleton.Shutdown();
        
        // Show the network panel again
        if (networkPanel != null) networkPanel.SetActive(true);
        if (disconnectButton != null) disconnectButton.enabled = false;
        if (hostButton != null) hostButton.enabled = true;
        if (clientButton != null) clientButton.enabled = true;
        
        UpdateConnectionStatus("Disconnected");
    }

    /// <summary>
    /// Callback when a client connects to the network.
    /// </summary>
    private void OnClientConnected(ulong clientId) {
        if (clientId == NetworkManager.Singleton.LocalClientId) {
            UpdateConnectionStatus(NetworkManager.Singleton.IsHost ? "Hosting" : "Connected as Client");
            
            // If we're not the host, we need to initialize client-side state
            if (!NetworkManager.Singleton.IsHost) {
                // Client will receive game state via SyncGameStateClientRpc
                gameStarted = true;
            }
        } else {
            // A remote client connected
            UpdateConnectionStatus("Player Connected");
            
            // If we're the host, sync the game state to the new client
            if (NetworkManager.Singleton.IsHost) {
                string serializedGame = GameManager.Instance.SerializeGame();
                SyncGameStateClientRpc(serializedGame);
                Debug.Log("Sent initial game state to new client");
            }
        }
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
    /// This is the core method that ensures synchronization between host and clients.
    /// </summary>
    [ClientRpc]
    public void SyncGameStateClientRpc(string serializedGameState) {
        if (!NetworkManager.Singleton.IsHost) {
            Debug.Log("Received game state from host");
            
            // Load the serialized game state from the host
            GameManager.Instance.LoadGame(serializedGameState);
            
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
    /// Notifies the host that a client has made a move.
    /// Called by client when they make a move.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void NotifyMoveServerRpc(string move, ServerRpcParams serverRpcParams = default) {
        // Check if we're the host
        if (NetworkManager.Singleton.IsHost) {
            Debug.Log($"Host received move notification from client. Move: {move}");
            
            // In a real implementation, you'd validate and apply the move
            // For now, we'll just rely on the periodic sync
            // The move details are included for potential future validation
        }
    }

    /// <summary>
    /// Directly broadcasts the current game state to all clients.
    /// Called after a player makes a move.
    /// </summary>
    public void BroadcastCurrentGameState() {
        if (NetworkManager.Singleton.IsHost) {
            string serializedGame = GameManager.Instance.SerializeGame();
            lastSyncedState = serializedGame; // Update last synced state
            SyncGameStateClientRpc(serializedGame);
            Debug.Log("Broadcasting game state after move");
        } else if (NetworkManager.Singleton.IsClient) {
            // If we're a client, notify the host about our move
            NotifyMoveServerRpc(GameManager.Instance.SerializeGame());
        }
    }
    
    /// <summary>
    /// Backwards compatibility method for old code that used BroadcastMoveClientRpc
    /// </summary>
    [ClientRpc]
    public void BroadcastMoveClientRpc(string serializedMove) {
        if (!NetworkManager.Singleton.IsHost) {
            // This is the old method, redirect to the new approach
            GameManager.Instance.LoadGame(serializedMove);
            BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(localPlayerSide);
        }
    }
}