using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityChess;

/// <summary>
/// Displays network status information during a networked chess game.
/// Shows player information, connection status, and game state.
/// </summary>
public class NetworkGameStatusUI : MonoBehaviour
{
    [Header("Status Elements")]
    [SerializeField] private GameObject statusContainer;
    [SerializeField] private TextMeshProUGUI connectionStatusText;
    [SerializeField] private TextMeshProUGUI localPlayerText;
    [SerializeField] private TextMeshProUGUI opponentText;
    [SerializeField] private TextMeshProUGUI gameStatusText;
    [SerializeField] private TextMeshProUGUI pingText;
    [SerializeField] private TextMeshProUGUI joinCodeText;
    
    [Header("Status Icons")]
    [SerializeField] private Image localPlayerStatusIcon;
    [SerializeField] private Image opponentStatusIcon;
    [SerializeField] private Image connectionStatusIcon;
    
    [Header("Settings")]
    [SerializeField] private float updateInterval = 1.0f;
    [SerializeField] private Color connectedColor = Color.green;
    [SerializeField] private Color disconnectedColor = Color.red;
    [SerializeField] private Color waitingColor = Color.yellow;
    
    // References
    private ChessNetworkManager networkManager;
    private PlayerConnectionManager playerConnectionManager;
    private float updateTimer = 0f;
    
    private void Start()
    {
        // Get references
        networkManager = ChessNetworkManager.Instance;
        
        if (networkManager != null)
        {
            playerConnectionManager = networkManager.GetPlayerConnectionManager();
        }
        
        // Subscribe to events
        if (playerConnectionManager != null)
        {
            PlayerConnectionManager.ConnectionStatusChangedEvent += OnConnectionStatusChanged;
            PlayerConnectionManager.PlayerConnectedEvent += OnPlayerConnected;
            PlayerConnectionManager.PlayerDisconnectedEvent += OnPlayerDisconnected;
            PlayerConnectionManager.PlayerReconnectedEvent += OnPlayerReconnected;
        }
        
        // Hide if not in a networked game
        if (statusContainer != null)
        {
            statusContainer.SetActive(networkManager != null && NetworkManager.Singleton.IsConnectedClient);
        }
        
        // Initial update
        UpdateUI();
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (playerConnectionManager != null)
        {
            PlayerConnectionManager.ConnectionStatusChangedEvent -= OnConnectionStatusChanged;
            PlayerConnectionManager.PlayerConnectedEvent -= OnPlayerConnected;
            PlayerConnectionManager.PlayerDisconnectedEvent -= OnPlayerDisconnected;
            PlayerConnectionManager.PlayerReconnectedEvent -= OnPlayerReconnected;
        }
    }
    
    private void Update()
    {
        // Only update UI if in a networked game
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
        {
            if (statusContainer != null && statusContainer.activeSelf)
            {
                statusContainer.SetActive(false);
            }
            return;
        }
        
        // Show status container
        if (statusContainer != null && !statusContainer.activeSelf)
        {
            statusContainer.SetActive(true);
        }
        
        // Update UI periodically
        updateTimer += Time.deltaTime;
        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            UpdateUI();
        }
    }
    
    /// <summary>
    /// Updates all UI elements
    /// </summary>
    private void UpdateUI()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
        {
            return;
        }
        
        // Update connection status
        UpdateConnectionStatus();
        
        // Update player information
        UpdatePlayerInfo();
        
        // Update game status
        UpdateGameStatus();
        
        // Update ping
        UpdatePingStatus();
        
        // Update join code
        UpdateJoinCode();
    }
    
    /// <summary>
    /// Updates the connection status text and icon
    /// </summary>
    private void UpdateConnectionStatus()
    {
        if (connectionStatusText != null)
        {
            string status = NetworkManager.Singleton.IsHost ? "Hosting" : "Connected";
            connectionStatusText.text = $"Status: {status}";
        }
        
        if (connectionStatusIcon != null)
        {
            connectionStatusIcon.color = connectedColor;
        }
    }
    
    /// <summary>
    /// Updates player information
    /// </summary>
    private void UpdatePlayerInfo()
    {
        if (networkManager == null) return;
        
        Side localSide = networkManager.GetLocalPlayerSide();
        string localPlayerName = "You";
        
        if (playerConnectionManager != null)
        {
            // Get local player info
            var players = playerConnectionManager.GetConnectedPlayers();
            foreach (var player in players)
            {
                if (player.AssignedSide == localSide)
                {
                    localPlayerName = player.PlayerName;
                    break;
                }
            }
        }
        
        // Update local player text
        if (localPlayerText != null)
        {
            localPlayerText.text = $"{localPlayerName} ({localSide})";
        }
        
        // Update local player status icon
        if (localPlayerStatusIcon != null)
        {
            localPlayerStatusIcon.color = connectedColor;
        }
        
        // Update opponent info
        Side opponentSide = localSide == Side.White ? Side.Black : Side.White;
        string opponentName = "Opponent";
        bool opponentConnected = false;
        
        if (playerConnectionManager != null)
        {
            // Look for opponent in connected players
            var connectedPlayers = playerConnectionManager.GetConnectedPlayers();
            foreach (var player in connectedPlayers)
            {
                if (player.AssignedSide == opponentSide)
                {
                    opponentName = player.PlayerName;
                    opponentConnected = true;
                    break;
                }
            }
            
            // If not found in connected, check disconnected
            if (!opponentConnected)
            {
                var disconnectedPlayers = playerConnectionManager.GetDisconnectedPlayers();
                foreach (var player in disconnectedPlayers)
                {
                    if (player.AssignedSide == opponentSide)
                    {
                        opponentName = player.PlayerName + " (Disconnected)";
                        break;
                    }
                }
            }
        }
        
        // Update opponent text
        if (opponentText != null)
        {
            opponentText.text = $"{opponentName} ({opponentSide})";
        }
        
        // Update opponent status icon
        if (opponentStatusIcon != null)
        {
            opponentStatusIcon.color = opponentConnected ? connectedColor : disconnectedColor;
        }
    }
    
    /// <summary>
    /// Updates game status information
    /// </summary>
    private void UpdateGameStatus()
    {
        if (gameStatusText == null || GameManager.Instance == null) return;
        
        string status = "";
        
        // Get current turn
        status += $"Current Turn: {GameManager.Instance.SideToMove}\n";
        
        // Get move count
        status += $"Move Count: {GameManager.Instance.LatestHalfMoveIndex}\n";
        
        // Get game state
        if (GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove))
        {
            if (latestHalfMove.CausedCheck)
            {
                status += "Check!\n";
            }
            
            if (latestHalfMove.CausedCheckmate)
            {
                status += "Checkmate! Game Over.\n";
            }
            else if (latestHalfMove.CausedStalemate)
            {
                status += "Stalemate! Game Over.\n";
            }
        }
        
        // Check if waiting for reconnection
        if (networkManager != null && networkManager.IsWaitingForReconnection())
        {
            status += "Waiting for player to reconnect...\n";
        }
        
        gameStatusText.text = status;
    }
    
    /// <summary>
    /// Updates ping information
    /// </summary>
    private void UpdatePingStatus()
    {
        if (pingText == null) return;
        
        // In a real implementation, you'd get the actual ping from your transport
        // This is just a placeholder
        int fakePing = Random.Range(20, 100);
        pingText.text = $"Ping: {fakePing} ms";
        
        // Color code based on ping quality
        if (fakePing < 50)
            pingText.color = Color.green;
        else if (fakePing < 100)
            pingText.color = Color.yellow;
        else
            pingText.color = Color.red;
    }
    
    /// <summary>
    /// Updates the join code display
    /// </summary>
    private void UpdateJoinCode()
    {
        if (joinCodeText == null) return;
        
        // Only show join code if we're the host
        if (NetworkManager.Singleton.IsHost)
        {
            string joinCode = "";
            
            // Get join code from session manager if available
            if (SessionManager.Instance != null)
            {
                joinCode = SessionManager.Instance.GetJoinCode();
            }
            else if (networkManager != null)
            {
                // Fallback to connection data from transport
                var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                if (transport != null)
                {
                    joinCode = $"{transport.ConnectionData.Address}:{transport.ConnectionData.Port}";
                }
            }
            
            joinCodeText.text = $"Join Code: {joinCode}";
            joinCodeText.gameObject.SetActive(true);
        }
        else
        {
            joinCodeText.gameObject.SetActive(false);
        }
    }
    
    #region Event Handlers
    
    /// <summary>
    /// Handles connection status changes
    /// </summary>
    private void OnConnectionStatusChanged(string status)
    {
        if (connectionStatusText != null)
        {
            connectionStatusText.text = $"Status: {status}";
        }
    }
    
    /// <summary>
    /// Handles player connections
    /// </summary>
    private void OnPlayerConnected(PlayerConnectionManager.PlayerInfo player)
    {
        UpdateUI();
    }
    
    /// <summary>
    /// Handles player disconnections
    /// </summary>
    private void OnPlayerDisconnected(PlayerConnectionManager.PlayerInfo player)
    {
        UpdateUI();
    }
    
    /// <summary>
    /// Handles player reconnections
    /// </summary>
    private void OnPlayerReconnected(PlayerConnectionManager.PlayerInfo player)
    {
        UpdateUI();
    }
    
    #endregion
}