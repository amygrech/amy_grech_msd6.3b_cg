using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityChess;

/// <summary>
/// Manages the network connectivity for the chess game.
/// Handles hosting, joining, and disconnecting from network sessions.
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

    private void Start() {
        // Set up event listeners
        if (hostButton != null) hostButton.onClick.AddListener(StartHost);
        if (clientButton != null) clientButton.onClick.AddListener(StartClient);
        if (disconnectButton != null) disconnectButton.onClick.AddListener(Disconnect);

        // Subscribe to network events
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        // Ensure the disconnect button is initially disabled
        if (disconnectButton != null) disconnectButton.enabled = false;
        
        // Display the network panel on start
        if (networkPanel != null) networkPanel.SetActive(true);

        UpdateConnectionStatus("Not Connected");
    }

    /// <summary>
    /// Starts a network session as the host.
    /// </summary>
    public void StartHost()
    {
        // Clean up NetworkObjects before starting host
        CleanupNetworkObjectsBeforeStart();
    
        if (NetworkManager.Singleton.StartHost())
        {
            localPlayerSide = Side.White;
        
            // Hide the network panel after successful connection
            if (networkPanel != null) networkPanel.SetActive(false);
            if (disconnectButton != null) disconnectButton.enabled = true;
            if (hostButton != null) hostButton.enabled = false;
            if (clientButton != null) clientButton.enabled = false;
        
            UpdateConnectionStatus("Hosting");
        
            // Start a new game
            GameManager.Instance.StartNewGame();
        }
    }

    public void StartClient()
    {
        // Clean up NetworkObjects before starting client
        CleanupNetworkObjectsBeforeStart();
    
        if (NetworkManager.Singleton.StartClient())
        {
            localPlayerSide = Side.Black;
        
            // Hide the network panel after attempting connection
            if (networkPanel != null) networkPanel.SetActive(false);
            if (disconnectButton != null) disconnectButton.enabled = true;
            if (hostButton != null) hostButton.enabled = false;
            if (clientButton != null) clientButton.enabled = false;
        
            UpdateConnectionStatus("Connecting...");
        }
    }

    private void CleanupNetworkObjectsBeforeStart()
    {
        // Find all NetworkObjects on chess pieces
        VisualPiece[] pieces = FindObjectsOfType<VisualPiece>();
        foreach (VisualPiece piece in pieces)
        {
            NetworkObject netObj = piece.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                // Disable NetworkObject components to prevent auto-registration
                netObj.enabled = false;
                Debug.Log($"Disabled NetworkObject on {piece.name} before starting network");
            }
        }
    }

    /// <summary>
    /// Disconnects from the current network session.
    /// </summary>
    public void Disconnect() {
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
        } else {
            // A remote client connected
            UpdateConnectionStatus("Player Connected");
            
            // If we're the host, sync the game state to the new client
            if (NetworkManager.Singleton.IsHost) {
                string serializedGame = GameManager.Instance.SerializeGame();
                SyncGameStateClientRpc(serializedGame);
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
    public void SyncGameStateClientRpc(string serializedGameState) {
        if (!NetworkManager.Singleton.IsHost) {
            // Load the state passed from the host
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
        if (!NetworkManager.Singleton.IsConnectedClient) {
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