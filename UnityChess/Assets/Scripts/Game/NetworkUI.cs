using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

/// <summary>
/// Manages the user interface for network session management.
/// Provides controls for hosting, joining, and disconnecting from network sessions.
/// </summary>
public class NetworkUI : MonoBehaviour {
    [Header("UI References")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private TMP_InputField ipAddressInput;
    [SerializeField] private TMP_InputField portInput;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI playerRoleText;

    [Header("Network Configuration")]
    [SerializeField] private string defaultIpAddress = "127.0.0.1";
    [SerializeField] private ushort defaultPort = 7777;

    private void Start() {
        // Set up default values
        ipAddressInput.text = defaultIpAddress;
        portInput.text = defaultPort.ToString();

        // Add button listeners
        hostButton.onClick.AddListener(StartHost);
        clientButton.onClick.AddListener(StartClient);
        disconnectButton.onClick.AddListener(Disconnect);

        // Initially disable the disconnect button
        disconnectButton.interactable = false;
        UpdateStatusText("Not Connected");
        
        // Subscribe to network events
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
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
        // Update transport with current UI values
        UpdateNetworkTransport();
        
        // Start the host
        if (NetworkManager.Singleton.StartHost()) {
            UpdateStatusText("Starting Host...");
            SetButtonsInteractable(false, false, true);
            UpdatePlayerRoleText("White (Host)");
        } else {
            UpdateStatusText("Failed to start host");
        }
    }

    /// <summary>
    /// Joins an existing network session as a client.
    /// </summary>
    public void StartClient() {
        // Update transport with current UI values
        UpdateNetworkTransport();
        
        // Start the client
        if (NetworkManager.Singleton.StartClient()) {
            UpdateStatusText("Connecting...");
            SetButtonsInteractable(false, false, true);
            UpdatePlayerRoleText("Black (Client)");
        } else {
            UpdateStatusText("Failed to start client");
        }
    }

    /// <summary>
    /// Disconnects from the current network session.
    /// </summary>
    public void Disconnect() {
        NetworkManager.Singleton.Shutdown();
        UpdateStatusText("Disconnected");
        SetButtonsInteractable(true, true, false);
        UpdatePlayerRoleText("");
    }

    /// <summary>
    /// Updates the network transport with the current configuration from the UI.
    /// </summary>
    private void UpdateNetworkTransport() {
        string address = ipAddressInput.text;
        ushort port = ushort.Parse(portInput.text);
        
        // Get the Unity Transport component
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null) {
            transport.ConnectionData.Address = address;
            transport.ConnectionData.Port = port;
            Debug.Log($"Updated network transport: {address}:{port}");
        }
    }

    /// <summary>
    /// Updates the status text in the UI.
    /// </summary>
    private void UpdateStatusText(string message) {
        if (statusText != null) {
            statusText.text = $"Status: {message}";
        }
    }

    /// <summary>
    /// Updates the player role text in the UI.
    /// </summary>
    private void UpdatePlayerRoleText(string role) {
        if (playerRoleText != null) {
            playerRoleText.text = role;
        }
    }

    /// <summary>
    /// Sets the interactable state of the network buttons.
    /// </summary>
    private void SetButtonsInteractable(bool host, bool client, bool disconnect) {
        if (hostButton != null) hostButton.interactable = host;
        if (clientButton != null) clientButton.interactable = client;
        if (disconnectButton != null) disconnectButton.interactable = disconnect;
    }

    /// <summary>
    /// Callback for when a client connects to the network.
    /// </summary>
    private void OnClientConnected(ulong clientId) {
        if (clientId == NetworkManager.Singleton.LocalClientId) {
            // Local client connected
            string status = NetworkManager.Singleton.IsHost ? "Hosting" : "Connected to Host";
            UpdateStatusText(status);
        } else {
            // Remote client connected
            UpdateStatusText("Player Connected");
        }
    }

    /// <summary>
    /// Callback for when a client disconnects from the network.
    /// </summary>
    private void OnClientDisconnected(ulong clientId) {
        if (clientId == NetworkManager.Singleton.LocalClientId) {
            // Local client disconnected
            UpdateStatusText("Disconnected");
            SetButtonsInteractable(true, true, false);
            UpdatePlayerRoleText("");
        } else {
            // Remote client disconnected
            UpdateStatusText("Player Disconnected");
        }
    }
}