using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityChess;
using System.Collections.Generic;
using System.Collections;
using System;

/// <summary>
/// Manages the network connectivity for the chess game.
/// Handles hosting, joining, and disconnecting from network sessions.
/// Uses game state synchronization instead of NetworkObject components.
/// Enhanced with connection management features for player joining, leaving, and reconnection.
/// </summary>
public class ChessNetworkManager : MonoBehaviourSingleton<ChessNetworkManager> {
    private NetworkTurnManager turnManager;
    
    [Header("Network UI")]
    [SerializeField] private GameObject networkPanel;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private InputField joinCodeInputField;
    [SerializeField] private Text connectionStatusText;
    [SerializeField] private InputField playerNameInputField;

    [Header("Connection Status UI")]
    [SerializeField] private GameObject statusPanel;
    [SerializeField] private Text playerListText;
    [SerializeField] private Text gameStatusText;
    [SerializeField] private float statusUpdateInterval = 1.0f;

    [Header("Game References")]
    [SerializeField] private GameManager gameManager;

    [Header("Reconnection Settings")]
    [SerializeField] private bool allowReconnection = true;
    [SerializeField] private float reconnectionTimeout = 30f;
    [SerializeField] private Text reconnectionStatusText;
    [SerializeField] private GameObject reconnectionPanel;
    [SerializeField] private Button reconnectButton;

    [Header("Connection Error Handling")]
    [SerializeField] private Text connectionErrorText;
    [SerializeField] private Button retryConnectionButton;
    [SerializeField] private float connectionTimeout = 15f;

    [Header("Integration Settings")]
    [SerializeField] private bool useSessionManager = true;
    [SerializeField] private bool useNetworkErrorHandler = true;
    [SerializeField] private bool useReconnectionManager = true;

    // Track the local player's side
    private Side localPlayerSide = Side.White;
    
    // Network variables
    private bool gameStarted = false;
    private float syncTimer = 0f;
    private const float syncInterval = 0.1f; // Sync 10 times per second for better responsiveness
    private string lastSyncedState = string.Empty;
    private float statusUpdateTimer = 0f;
    
    // Reconnection variables
    private string lastKnownConnectionData = string.Empty;
    private float disconnectTime = 0f;
    private bool wasConnected = false;
    private bool attemptingReconnection = false;
    private bool intentionalDisconnect = false;

    // Coroutine references for cancellation
    private Coroutine connectionTimeoutCoroutine;
    private Coroutine reconnectionTimeoutCoroutine;

    // Reference to player connection manager
    private PlayerConnectionManager playerConnectionManager;
    private PlayerConnectionManager playerConnectionManagerRef;

    // For tracking move synchronization
    private int lastSyncedMoveCount = -1;

    public static event Action<string> ConnectionStatusChangedEvent;

    // Debug logging
    [SerializeField] private bool verbose = true;

    private void Start() {
        // Set up event listeners
        if (hostButton != null) hostButton.onClick.AddListener(StartHost);
        if (clientButton != null) clientButton.onClick.AddListener(StartClient);
        if (disconnectButton != null) disconnectButton.onClick.AddListener(Disconnect);
        if (reconnectButton != null) reconnectButton.onClick.AddListener(AttemptReconnection);
        if (retryConnectionButton != null) retryConnectionButton.onClick.AddListener(RetryConnection);

        // Subscribe to network events
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        } else {
            Debug.LogError("NetworkManager.Singleton is null. Make sure NetworkManager is in the scene.");
        }

        // Get player connection manager
        playerConnectionManager = GetComponent<PlayerConnectionManager>();
        if (playerConnectionManager == null) {
            playerConnectionManager = gameObject.AddComponent<PlayerConnectionManager>();
            Debug.Log("Added PlayerConnectionManager component");
        }

        // Subscribe to player connection events
        PlayerConnectionManager.ConnectionStatusChangedEvent += OnConnectionStatusChanged;
        PlayerConnectionManager.PlayerConnectedEvent += OnPlayerConnected;
        PlayerConnectionManager.PlayerDisconnectedEvent += OnPlayerDisconnected;
        PlayerConnectionManager.PlayerReconnectedEvent += OnPlayerReconnected;

        // Ensure the disconnect button is initially disabled
        if (disconnectButton != null) disconnectButton.interactable = false;
        
        // Display the network panel on start
        if (networkPanel != null) networkPanel.SetActive(true);
        if (statusPanel != null) statusPanel.SetActive(false);
        if (reconnectionPanel != null) reconnectionPanel.SetActive(false);

        // Hide error text initially
        if (connectionErrorText != null) connectionErrorText.gameObject.SetActive(false);

        UpdateConnectionStatus("Not Connected");
        
        // Subscribe to game events
        GameManager.MoveExecutedEvent += OnMoveExecuted;
        
        // Set default player name if empty
        if (playerNameInputField != null && string.IsNullOrEmpty(playerNameInputField.text)) {
            playerNameInputField.text = "Player" + UnityEngine.Random.Range(1000, 9999);
        }

        // Subscribe to network error events if using NetworkErrorHandler
        if (useNetworkErrorHandler && NetworkErrorHandler.Instance != null) {
            NetworkErrorHandler.OnNetworkError += OnNetworkError;
        }
        
        // Create the turn manager if it doesn't exist
        turnManager = GetComponent<NetworkTurnManager>();
        if (turnManager == null) {
            turnManager = gameObject.AddComponent<NetworkTurnManager>();
        }
    }
    
    public void HandleSuccessfulMove() {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient) return;

        if (verbose) Debug.Log("HandleSuccessfulMove called - locking movement and broadcasting state");

        try {
            // Lock movement immediately after a move
            if (turnManager != null) {
                turnManager.LockMovement();
            } else {
                Debug.LogWarning("turnManager is null, cannot lock movement");
            }
        
            // Broadcast the current game state
            BroadcastCurrentGameState();
        
            // End the turn - use the safer method
            if (turnManager != null) {
                turnManager.EndTurn();
            } else {
                Debug.LogWarning("turnManager is null, cannot end turn");
            }
        } catch (System.Exception e) {
            Debug.LogError($"Error in HandleSuccessfulMove: {e.Message}\n{e.StackTrace}");
        
            // Ensure the game state is still broadcast even if there's an error
            try {
                BroadcastCurrentGameState();
            } catch (System.Exception e2) {
                Debug.LogError($"Error in fallback BroadcastCurrentGameState: {e2.Message}");
            }
        }
    }
    
    private void OnDestroy() {
        // Unsubscribe from network events
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        
        // Unsubscribe from game events
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
        
        // Unsubscribe from player connection events
        PlayerConnectionManager.ConnectionStatusChangedEvent -= OnConnectionStatusChanged;
        PlayerConnectionManager.PlayerConnectedEvent -= OnPlayerConnected;
        PlayerConnectionManager.PlayerDisconnectedEvent -= OnPlayerDisconnected;
        PlayerConnectionManager.PlayerReconnectedEvent -= OnPlayerReconnected;

        // Unsubscribe from network error events
        if (useNetworkErrorHandler && NetworkErrorHandler.Instance != null) {
            NetworkErrorHandler.OnNetworkError -= OnNetworkError;
        }

        // Cancel any coroutines
        if (connectionTimeoutCoroutine != null) {
            StopCoroutine(connectionTimeoutCoroutine);
        }
        
        if (reconnectionTimeoutCoroutine != null) {
            StopCoroutine(reconnectionTimeoutCoroutine);
        }
    }
    
    private void Update() {
        // Handle game state sync from host to clients
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost && gameStarted) {
            syncTimer += Time.deltaTime;
            if (syncTimer >= syncInterval) {
                syncTimer = 0f;
                
                // Get current game state
                string currentState = GameManager.Instance.SerializeGame();
                int currentMoveCount = GameManager.Instance.LatestHalfMoveIndex;
                
                // Only send if state has changed or move count has changed
                if (currentState != lastSyncedState || currentMoveCount != lastSyncedMoveCount) {
                    lastSyncedState = currentState;
                    lastSyncedMoveCount = currentMoveCount;
                    SyncGameStateClientRpc(currentState);
                    if (verbose) Debug.Log("[HOST] Game state synchronized to clients: " + currentState.Substring(0, Mathf.Min(30, currentState.Length)) + "...");
                }
            }
        }
        
        // Update status panel periodically
        if (statusPanel != null && statusPanel.activeSelf) {
            statusUpdateTimer += Time.deltaTime;
            if (statusUpdateTimer >= statusUpdateInterval) {
                statusUpdateTimer = 0f;
                UpdateStatusPanel();
            }
        }
        
        // Track reconnection state
        if (allowReconnection && NetworkManager.Singleton != null && !NetworkManager.Singleton.IsConnectedClient && wasConnected && !attemptingReconnection && !intentionalDisconnect) {
            // We were connected but now we're not
            if (disconnectTime == 0f) {
                disconnectTime = Time.time;
                
                // Show reconnection panel
                if (reconnectionPanel != null) {
                    reconnectionPanel.SetActive(true);
                }
            }
            
            // Update reconnection status
            if (reconnectionStatusText != null) {
                float timeLeft = reconnectionTimeout - (Time.time - disconnectTime);
                if (timeLeft > 0) {
                    reconnectionStatusText.text = $"Disconnected. You can reconnect for {timeLeft:F0} seconds.";
                } else {
                    reconnectionStatusText.text = "Reconnection timeout expired.";
                    // Disable reconnect button after timeout
                    if (reconnectButton != null) {
                        reconnectButton.interactable = false;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Called when the connection status changes
    /// </summary>
    private void OnConnectionStatusChanged(string status) {
        UpdateConnectionStatus(status);
    }
    
    /// <summary>
    /// Called when a player connects
    /// </summary>
    private void OnPlayerConnected(PlayerConnectionManager.PlayerInfo player) {
        UpdateStatusPanel();
    }
    
    /// <summary>
    /// Called when a player disconnects
    /// </summary>
    private void OnPlayerDisconnected(PlayerConnectionManager.PlayerInfo player) {
        UpdateStatusPanel();
    }
    
    /// <summary>
    /// Called when a player reconnects
    /// </summary>
    private void OnPlayerReconnected(PlayerConnectionManager.PlayerInfo player) {
        UpdateStatusPanel();
    }

    /// <summary>
    /// Handles network errors from NetworkErrorHandler
    /// </summary>
    private void OnNetworkError(NetworkErrorHandler.NetworkErrorType errorType, string message) {
        // Update UI with error message
        UpdateConnectionStatus($"Error: {message}");
        
        // Handle specific error types
        switch (errorType) {
            case NetworkErrorHandler.NetworkErrorType.ConnectionFailed:
            case NetworkErrorHandler.NetworkErrorType.ConnectionTimeout:
                // Show retry UI
                ShowConnectionError(message);
                break;
                
            case NetworkErrorHandler.NetworkErrorType.Disconnected:
                // Handle unexpected disconnection
                if (!intentionalDisconnect && useReconnectionManager && NetworkReconnectionManager.Instance != null) {
                    // Start reconnection process if not an intentional disconnect
                    StartCoroutine(ShowReconnectionUI());
                }
                break;
                
            case NetworkErrorHandler.NetworkErrorType.ReconnectionFailed:
                // Return to main menu
                if (networkPanel != null) networkPanel.SetActive(true);
                if (statusPanel != null) statusPanel.SetActive(false);
                if (reconnectionPanel != null) reconnectionPanel.SetActive(false);
                break;
        }
    }
    
    /// <summary>
    /// Shows the reconnection UI after a short delay
    /// </summary>
    private IEnumerator ShowReconnectionUI() {
        // Wait a moment before showing reconnection UI
        yield return new WaitForSeconds(1f);
        
        if (reconnectionPanel != null) {
            reconnectionPanel.SetActive(true);
        }
        
        if (statusPanel != null) {
            statusPanel.SetActive(false);
        }
        
        // Update reconnection status text
        if (reconnectionStatusText != null && NetworkReconnectionManager.Instance != null) {
            float timeLeft = NetworkReconnectionManager.Instance.GetRemainingReconnectionTime();
            reconnectionStatusText.text = $"Disconnected. You can reconnect for {timeLeft:F0} seconds.";
        }
    }

    /// <summary>
    /// Updates the status panel with current player and game information
    /// </summary>
    private void UpdateStatusPanel() {
        if (playerListText == null || gameStatusText == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient) 
            return;

        // Update player list
        string playerListString = "Players:\n";
        List<PlayerConnectionManager.PlayerInfo> connectedPlayers = playerConnectionManager.GetConnectedPlayers();
        List<PlayerConnectionManager.PlayerInfo> disconnectedPlayers = playerConnectionManager.GetDisconnectedPlayers();
        
        foreach (var player in connectedPlayers) {
            playerListString += $"- {player.PlayerName} ({player.AssignedSide}) [Connected]\n";
        }
        
        foreach (var player in disconnectedPlayers) {
            playerListString += $"- {player.PlayerName} ({player.AssignedSide}) [Disconnected]\n";
        }
        
        playerListText.text = playerListString;
        
        // Update game status
        string gameStatusString = "Game Status:\n";
        if (gameStarted) {
            gameStatusString += $"Current Turn: {GameManager.Instance.SideToMove}\n";
            gameStatusString += $"Move Count: {GameManager.Instance.LatestHalfMoveIndex}\n";
            
            // Get the latest half-move
            if (GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove)) {
                if (latestHalfMove.CausedCheck) {
                    gameStatusString += "Check!\n";
                }
                if (latestHalfMove.CausedCheckmate) {
                    gameStatusString += "Checkmate! Game Over.\n";
                } else if (latestHalfMove.CausedStalemate) {
                    gameStatusString += "Stalemate! Game Over.\n";
                }
            }

            // Check if waiting for reconnection
            if (IsWaitingForReconnection()) {
                gameStatusString += "Waiting for player to reconnect...\n";
            }
        } else {
            gameStatusString += "Game not started\n";
        }
        
        gameStatusText.text = gameStatusString;
    }

    /// <summary>
    /// Called when a move is executed in the game
    /// </summary>
    private void OnMoveExecuted() {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient) return;
        
        if (verbose) Debug.Log("Move executed - syncing state");
        BroadcastCurrentGameState();
        
        // Update pieces interactive state for all pieces
        RefreshAllPiecesInteractivity();
        
        // Update the status panel
        UpdateStatusPanel();
    }
    
    /// <summary>
    /// Shows a connection error message
    /// </summary>
    private void ShowConnectionError(string errorMessage) {
        if (connectionErrorText != null) {
            connectionErrorText.text = errorMessage;
            connectionErrorText.gameObject.SetActive(true);
        }
        
        if (retryConnectionButton != null) {
            retryConnectionButton.gameObject.SetActive(true);
        }
        
        UpdateConnectionStatus($"Error: {errorMessage}");
    }
    
    /// <summary>
    /// Hides the connection error message
    /// </summary>
    private void HideConnectionError() {
        if (connectionErrorText != null) {
            connectionErrorText.gameObject.SetActive(false);
        }
        
        if (retryConnectionButton != null) {
            retryConnectionButton.gameObject.SetActive(false);
        }
    }
    
    /// <summary>
    /// Retries the last connection attempt
    /// </summary>
    public void RetryConnection() {
        HideConnectionError();
        
        if (lastKnownConnectionData.Contains("host")) {
            // We were trying to host
            StartHost();
        } else {
            // We were trying to join
            StartClient();
        }
    }
    
    /// <summary>
    /// Validates a join code before attempting to connect
    /// </summary>
    private bool ValidateJoinCode(string joinCode) {
        // Special case for "chessgame"
        if (joinCode.ToLower() == "chessgame") {
            Debug.Log("Special code 'chessgame' validated internally");
            return true;
        }
        
        // Use NetworkErrorHandler if available
        if (useNetworkErrorHandler && NetworkErrorHandler.Instance != null) {
            return NetworkErrorHandler.Instance.ValidateJoinCode(joinCode);
        }
        
        // Simple validation - make sure it's not empty
        if (string.IsNullOrEmpty(joinCode)) {
            ShowConnectionError("Join code cannot be empty");
            return false;
        }
        
        // For this implementation, we'll assume join codes are IP:port
        if (!joinCode.Contains(":")) {
            ShowConnectionError("Invalid join code format. Expected format: IP:PORT");
            return false;
        }
        
        // Parse the join code to verify format
        string[] parts = joinCode.Split(':');
        if (parts.Length != 2 || !ushort.TryParse(parts[1], out _)) {
            ShowConnectionError("Invalid join code format. Port must be a number.");
            return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Attempts to reconnect to the previous session
    /// </summary>
    public void AttemptReconnection() {
        try {
            HideConnectionError();
            
            // If using NetworkReconnectionManager, delegate to it
            if (useReconnectionManager && NetworkReconnectionManager.Instance != null) {
                if (NetworkReconnectionManager.Instance.AttemptReconnection()) {
                    attemptingReconnection = true;
                    
                    // Update status
                    UpdateConnectionStatus("Attempting to reconnect...");
                    
                    // Hide reconnection panel
                    if (reconnectionPanel != null) {
                        reconnectionPanel.SetActive(false);
                    }
                    
                    return;
                }
            }
            
            // Fallback to original reconnection logic
            if (string.IsNullOrEmpty(lastKnownConnectionData) || Time.time - disconnectTime > reconnectionTimeout) {
                ShowConnectionError("Cannot reconnect: timeout expired");
                return;
            }
            
            attemptingReconnection = true;
            
            // Cancel any ongoing reconnection attempt
            if (reconnectionTimeoutCoroutine != null) {
                StopCoroutine(reconnectionTimeoutCoroutine);
                reconnectionTimeoutCoroutine = null;
            }
            
            // Set up the transport with the last known connection data
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport != null && !string.IsNullOrEmpty(lastKnownConnectionData)) {
                // Format is expected to be "address:port"
                string[] parts = lastKnownConnectionData.Split(':');
                if (parts.Length == 2 && ushort.TryParse(parts[1], out ushort port)) {
                    transport.ConnectionData.Address = parts[0];
                    transport.ConnectionData.Port = port;
                    Debug.Log($"Attempting to reconnect to {parts[0]}:{port}");
                    
                    // Try to reconnect
                    if (NetworkManager.Singleton.StartClient()) {
                        Debug.Log("Reconnection attempt started");
                        UpdateConnectionStatus("Attempting to reconnect...");
                        if (reconnectionPanel != null) {
                            reconnectionPanel.SetActive(false);
                        }
                        
                        // Start a timeout coroutine to handle failed reconnection
                        reconnectionTimeoutCoroutine = StartCoroutine(ReconnectionTimeoutCoroutine(10f));
                    } else {
                        Debug.LogError("Failed to start client for reconnection");
                        ShowConnectionError("Reconnection failed");
                        attemptingReconnection = false;
                    }
                } else {
                    Debug.LogError("Invalid connection data format");
                    ShowConnectionError("Reconnection failed - invalid data");
                    attemptingReconnection = false;
                }
            } else {
                Debug.LogError("Transport not found or invalid connection data");
                ShowConnectionError("Reconnection failed - transport error");
                attemptingReconnection = false;
            }
        } catch (Exception ex) {
            ShowConnectionError($"Reconnection error: {ex.Message}");
            Debug.LogError($"Reconnection error: {ex}");
            attemptingReconnection = false;
        }
    }
    
    /// <summary>
    /// Handles reconnection timeout
    /// </summary>
    private IEnumerator ReconnectionTimeoutCoroutine(float timeoutSeconds) {
        float reconnectionStartTime = Time.time;
        bool reconnected = false;
        
        while (!reconnected && Time.time - reconnectionStartTime < timeoutSeconds) {
            reconnected = NetworkManager.Singleton.IsConnectedClient;
            yield return new WaitForSeconds(0.5f);
        }
        
        reconnectionTimeoutCoroutine = null;
        
        if (!reconnected) {
            UpdateConnectionStatus("Reconnection timed out");
            ShowConnectionError("Reconnection timed out");
            attemptingReconnection = false;
            
            // Show reconnection panel again
            if (reconnectionPanel != null) {
                reconnectionPanel.SetActive(true);
            }
        }
    }
    
    /// <summary>
    /// Handles connection timeout when a client fails to connect within a certain time
    /// </summary>
    private IEnumerator ConnectionTimeoutCoroutine() {
        float connectionStartTime = Time.time;
        bool connected = false;
        
        while (!connected && Time.time - connectionStartTime < connectionTimeout) {
            connected = NetworkManager.Singleton.IsConnectedClient;
            yield return new WaitForSeconds(0.5f);
        }
        
        connectionTimeoutCoroutine = null;
        
        if (!connected) {
            UpdateConnectionStatus("Connection timed out");
            ShowConnectionError("Connection timed out. The host may not be available.");
            Disconnect();
        }
    }
    
    /// <summary>
    /// Callback when reconnection fails
    /// </summary>
    public void OnReconnectionFailed() {
        // Show reconnection failed UI or return to main menu
        if (networkPanel != null) networkPanel.SetActive(true);
        if (statusPanel != null) statusPanel.SetActive(false);
        if (reconnectionPanel != null) reconnectionPanel.SetActive(false);
        
        UpdateConnectionStatus("Reconnection failed");
    }
    
    /// <summary>
    /// Starts a network session as the host.
    /// </summary>
    public void StartHost() {
        if (playerNameInputField == null)
        {
            Debug.LogWarning("playerNameInputField is null, using default name");
            // Continue with a default value
        }
        
        try {
            // Reset intentional disconnect flag
            intentionalDisconnect = false;
            
            // Cancel any ongoing connection attempts
            if (connectionTimeoutCoroutine != null) {
                StopCoroutine(connectionTimeoutCoroutine);
                connectionTimeoutCoroutine = null;
            }
            
            HideConnectionError();
            DisableAllNetworkObjectsInScene();
            
            // Use SessionManager if available
            if (useSessionManager && SessionManager.Instance != null) {
                string playerName = playerNameInputField != null ? playerNameInputField.text : "Host";
                SessionManager.Instance.CreateSession(playerName);
            }
            
            // Save that we are attempting to host (for retry logic)
            lastKnownConnectionData = "host";
            
            // Save the player name
            if (playerNameInputField != null && !string.IsNullOrEmpty(playerNameInputField.text)) {
                playerConnectionManager.SetPlayerName(playerNameInputField.text);
            }
            
            // Save connection data for potential reconnection
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport != null) {
                lastKnownConnectionData = $"{transport.ConnectionData.Address}:{transport.ConnectionData.Port}";
                
                // Store connection data in player connection manager
                playerConnectionManager.StoreConnectionData(NetworkManager.Singleton.LocalClientId, lastKnownConnectionData);
            }
            
            if (NetworkManager.Singleton.StartHost()) {
                localPlayerSide = Side.White;
                wasConnected = true;
                
                // Hide the network panel after successful connection
                if (networkPanel != null) networkPanel.SetActive(false);
                if (statusPanel != null) statusPanel.SetActive(true);
                if (disconnectButton != null) disconnectButton.interactable = true;
                if (hostButton != null) hostButton.interactable = false;
                if (clientButton != null) clientButton.interactable = false;
                
                UpdateConnectionStatus("Hosting (White)");
                
                // Start a new game
                GameManager.Instance.StartNewGame();
                gameStarted = true;
                
                // Initial sync
                lastSyncedState = GameManager.Instance.SerializeGame();
                lastSyncedMoveCount = GameManager.Instance.LatestHalfMoveIndex;
                
                // Make sure only White pieces are enabled for the host
                RefreshAllPiecesInteractivity();
                
                if (verbose) Debug.Log("[HOST] Game started with initial state: " + lastSyncedState.Substring(0, Mathf.Min(30, lastSyncedState.Length)) + "...");
            } else {
                ShowConnectionError("Failed to start host: Network error");
            }
        } catch (Exception ex) {
            ShowConnectionError($"Host error: {ex.Message}");
            Debug.LogError($"Host start error: {ex}");
        }
    }

    /// <summary>
    /// Joins a game using a join code, integrating with SessionManager
    /// </summary>
    public bool JoinGame(string joinCode) {
        try {
            // Reset intentional disconnect flag
            intentionalDisconnect = false;
            
            // Special handling for "chessgame" code
            if (joinCode.ToLower() == "chessgame") {
                joinCode = "127.0.0.1:7777";
            }
            
            // Use NetworkErrorHandler to validate join code
            if (!ValidateJoinCode(joinCode)) {
                return false;
            }
            
            // Use SessionManager to handle joining
            if (useSessionManager && SessionManager.Instance != null) {
                string playerName = "Player";
                if (playerNameInputField != null) {
                    playerName = playerNameInputField.text;
                }
                
                if (!SessionManager.Instance.JoinSession(joinCode, playerName)) {
                    UpdateConnectionStatus("Failed to join session");
                    return false;
                }
            }
            
            // Set up transport directly if not using SessionManager
            if (!useSessionManager || SessionManager.Instance == null) {
                // Parse join code (IP:port format)
                string[] parts = joinCode.Split(':');
                if (parts.Length == 2 && ushort.TryParse(parts[1], out ushort port)) {
                    var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                    if (transport != null) {
                        transport.ConnectionData.Address = parts[0];
                        transport.ConnectionData.Port = port;
                        lastKnownConnectionData = joinCode;
                    } else {
                        UpdateConnectionStatus("Transport component not found");
                        return false;
                    }
                } else {
                    UpdateConnectionStatus("Invalid join code format");
                    return false;
                }
            }
            
            // Start monitoring connection attempt
            if (useNetworkErrorHandler && NetworkErrorHandler.Instance != null) {
                NetworkErrorHandler.Instance.MonitorConnectionAttempt();
            }
            
            // Start client
            DisableAllNetworkObjectsInScene();
            
            // Save the player name
            if (playerNameInputField != null && !string.IsNullOrEmpty(playerNameInputField.text)) {
                playerConnectionManager.SetPlayerName(playerNameInputField.text);
            }
            
            if (NetworkManager.Singleton.StartClient()) {
                localPlayerSide = Side.Black;
                wasConnected = true;
                
                // Hide the network panel after attempting connection
                if (networkPanel != null) networkPanel.SetActive(false);
                if (statusPanel != null) statusPanel.SetActive(true);
                if (disconnectButton != null) disconnectButton.interactable = true;
                if (hostButton != null) hostButton.interactable = false;
                if (clientButton != null) clientButton.interactable = false;
                
                UpdateConnectionStatus("Connecting...");
                gameStarted = true;
                
                // Start a connection timeout coroutine
                connectionTimeoutCoroutine = StartCoroutine(ConnectionTimeoutCoroutine());
                
                if (verbose) Debug.Log("[CLIENT] Attempting to connect to host");
                
                return true;
            } else {
                UpdateConnectionStatus("Failed to start client");
                return false;
            }
        } catch (Exception ex) {
            Debug.LogError($"Error joining game: {ex.Message}");
            UpdateConnectionStatus($"Error: {ex.Message}");
            return false;
        }
    }
    
    public void UpdateBoardVisuals(string serializedGameState) {
        // Load the game state
        GameManager.Instance.LoadGame(serializedGameState);
    
        // Update piece interactivity based on the new game state
        RefreshAllPiecesInteractivity();
    
        // Update the board visuals
        foreach ((Square square, Piece piece) in GameManager.Instance.CurrentPieces) {
            // Get the GameObject at this position
            GameObject pieceGO = BoardManager.Instance.GetPieceGOAtPosition(square);
        
            // If there's no piece there but should be, create it
            if (pieceGO == null) {
                BoardManager.Instance.CreateAndPlacePieceGO(piece, square);
            }
            // If there's a piece there that doesn't match the current state, update it
            else {
                VisualPiece visualPiece = pieceGO.GetComponent<VisualPiece>();
                if (visualPiece != null && visualPiece.PieceColor != piece.Owner) {
                    // Remove incorrect piece and create correct one
                    BoardManager.Instance.TryDestroyVisualPiece(square);
                    BoardManager.Instance.CreateAndPlacePieceGO(piece, square);
                }
            }
        }
    
        // Remove any pieces that are no longer in the game state
        VisualPiece[] allPieces = FindObjectsOfType<VisualPiece>();
        foreach (VisualPiece piece in allPieces) {
            Square position = piece.CurrentSquare;
            Piece boardPiece = GameManager.Instance.CurrentBoard[position];
        
            // If this visual piece has no corresponding piece in the game state, remove it
            if (boardPiece == null) {
                BoardManager.Instance.TryDestroyVisualPiece(position);
            }
        }
    
        // Update the UI
        if (UIManager.Instance != null) {
            // Trigger UI update (this would normally happen with GameResetToHalfMoveEvent)
            UIManager.Instance.SendMessage("OnGameResetToHalfMove", SendMessageOptions.DontRequireReceiver);
        }
    }

    /// <summary>
    /// Joins an existing network session as a client.
    /// </summary>
    public void StartClient()
    {
        try
        {
            // Reset intentional disconnect flag
            intentionalDisconnect = false;
            
            // Cancel any ongoing connection attempts
            if (connectionTimeoutCoroutine != null)
            {
                StopCoroutine(connectionTimeoutCoroutine);
                connectionTimeoutCoroutine = null;
            }
            
            HideConnectionError();
            
            string joinCode = joinCodeInputField != null ? joinCodeInputField.text : "";
            Debug.Log($"Join code entered: '{joinCode}'");
            
            // Special handling for "chessgame" code
            if (joinCode.ToLower() == "chessgame")
            {
                Debug.Log("Special code 'chessgame' detected, setting to localhost:7777");
                joinCode = "127.0.0.1:7777";
            }
            
            // Validate the join code
            if (!ValidateJoinCode(joinCode))
            {
                Debug.LogError("Join code validation failed");
                return; // Validation failed
            }
            
            // Setup transport with the join code
            string[] parts = joinCode.Split(':');
            if (parts.Length == 2 && ushort.TryParse(parts[1], out ushort port))
            {
                var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                if (transport != null)
                {
                    transport.ConnectionData.Address = parts[0];
                    transport.ConnectionData.Port = port;
                    lastKnownConnectionData = joinCode;
                    
                    // Store connection data in player connection manager
                    playerConnectionManager.StoreConnectionData(0, joinCode); // Client ID will be assigned later
                }
                else
                {
                    ShowConnectionError("Transport component not found");
                    return;
                }
            }
            else
            {
                ShowConnectionError("Invalid join code format");
                return;
            }
            
            DisableAllNetworkObjectsInScene();
            
            // Save the player name
            if (playerNameInputField != null && !string.IsNullOrEmpty(playerNameInputField.text))
            {
                playerConnectionManager.SetPlayerName(playerNameInputField.text);
            }
            
            if (NetworkManager.Singleton.StartClient())
            {
                localPlayerSide = Side.Black;
                wasConnected = true;
                
                // Hide the network panel after attempting connection
                if (networkPanel != null) networkPanel.SetActive(false);
                if (statusPanel != null) statusPanel.SetActive(true);
                if (disconnectButton != null) disconnectButton.interactable = true;
                if (hostButton != null) hostButton.interactable = false;
                if (clientButton != null) clientButton.interactable = false;
                
                UpdateConnectionStatus("Connecting...");
                gameStarted = true;
                
                // Start a connection timeout coroutine
                connectionTimeoutCoroutine = StartCoroutine(ConnectionTimeoutCoroutine());
                
                if (verbose) Debug.Log("[CLIENT] Attempting to connect to host");
            } else {
                ShowConnectionError("Failed to start client: Network error");
            }
        } catch (Exception ex) {
            ShowConnectionError($"Connection error: {ex.Message}");
            Debug.LogError($"Client start error: {ex}");
        }
    }

    /// <summary>
    /// Disables all NetworkObject components in the scene to prevent duplicate ID issues
    /// </summary>
    private void DisableAllNetworkObjectsInScene() {
        // Find all NetworkObjects in the scene
        NetworkObject[] networkObjects = FindObjectsOfType<NetworkObject>();
        foreach (NetworkObject netObj in networkObjects) {
            // Skip the NetworkManager's own NetworkObject
            if (netObj.GetComponent<NetworkManager>() != null) continue;
            
            // Disable the NetworkObject component
            if (netObj.enabled) {
                netObj.enabled = false;
                if (verbose) Debug.Log($"Disabled NetworkObject on {netObj.name}");
            }
        }
    }

    /// <summary>
    /// Disconnects from the current network session.
    /// </summary>
    public void Disconnect() {
        // Set intentional disconnect flag
        intentionalDisconnect = true;
        gameStarted = false;
        
        // Clean up reconnection tracking
        wasConnected = false;
        disconnectTime = 0f;
        attemptingReconnection = false;
        
        // Cancel any reconnection process
        if (useReconnectionManager && NetworkReconnectionManager.Instance != null) {
            NetworkReconnectionManager.Instance.CancelReconnection();
        }
        
        // Clean up session data
        if (useSessionManager && SessionManager.Instance != null) {
            SessionManager.Instance.LeaveSession();
        }
        
        // Cancel any ongoing connection attempts
        if (connectionTimeoutCoroutine != null) {
            StopCoroutine(connectionTimeoutCoroutine);
            connectionTimeoutCoroutine = null;
        }
        
        if (reconnectionTimeoutCoroutine != null) {
            StopCoroutine(reconnectionTimeoutCoroutine);
            reconnectionTimeoutCoroutine = null;
        }
        
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.Shutdown();
        }
        
        // Clean up player connection data
        if (playerConnectionManager != null) {
            playerConnectionManager.ClearAllPlayerData();
        }
        
        // Update UI
        HideConnectionError();
        if (networkPanel != null) networkPanel.SetActive(true);
        if (statusPanel != null) statusPanel.SetActive(false);
        if (reconnectionPanel != null) reconnectionPanel.SetActive(false);
        if (disconnectButton != null) disconnectButton.interactable = false;
        if (hostButton != null) hostButton.interactable = true;
        if (clientButton != null) clientButton.interactable = true;
        
        UpdateConnectionStatus("Disconnected");
    }

    /// <summary>
    /// Callback when a client connects to the network.
    /// </summary>
    private void OnClientConnected(ulong clientId) {
        if (clientId == NetworkManager.Singleton.LocalClientId) {
            string roleText = NetworkManager.Singleton.IsHost ? "Hosting (White)" : "Connected as Client (Black)";
            UpdateConnectionStatus(roleText);
            
            // Reset reconnection tracking
            wasConnected = true;
            disconnectTime = 0f;
            attemptingReconnection = false;
            
            // Cancel timeouts
            if (connectionTimeoutCoroutine != null) {
                StopCoroutine(connectionTimeoutCoroutine);
                connectionTimeoutCoroutine = null;
            }
            
            if (reconnectionTimeoutCoroutine != null) {
                StopCoroutine(reconnectionTimeoutCoroutine);
                reconnectionTimeoutCoroutine = null;
            }
            
            // Update UI
            HideConnectionError();
            if (statusPanel != null) statusPanel.SetActive(true);
            if (reconnectionPanel != null) reconnectionPanel.SetActive(false);
            
            // If we're the host, initialize a new game and sync to clients
            if (NetworkManager.Singleton.IsHost) {
                // Host should only enable White pieces
                RefreshAllPiecesInteractivity();
                if (verbose) Debug.Log("[HOST] Client connected, sending game state");
                
                // Send initial game state to the newly connected client
                SyncGameStateClientRpc(GameManager.Instance.SerializeGame());
            } else {
                // If we're the client, request the current game state from the host
                RequestSyncGameStateServerRpc();
            }
            
            // If using NetworkReconnectionManager, notify of successful reconnection
            if (attemptingReconnection && useReconnectionManager && NetworkReconnectionManager.Instance != null) {
                NetworkReconnectionManager.Instance.NotifyReconnectionSuccess();
            }
        } else {
            // A remote client connected - check if it's a reconnection
            bool isReconnection = false;
            foreach (var player in playerConnectionManager.GetDisconnectedPlayers()) {
                if (Time.time - player.DisconnectTime <= reconnectionTimeout) {
                    // This appears to be a reconnection
                    isReconnection = true;
                    playerConnectionManager.HandlePlayerReconnection(clientId);
                    UpdateConnectionStatus("Player Reconnected");
                    break;
                }
            }
            
            if (!isReconnection) {
                // New player connected
                UpdateConnectionStatus("Player Connected");
            }
            
            UpdateStatusPanel();
            
            // If we're the host, sync the game state to the new client
            if (NetworkManager.Singleton.IsHost) {
                string serializedGame = GameManager.Instance.SerializeGame();
                SyncGameStateClientRpc(serializedGame);
                if (verbose) Debug.Log("[HOST] Sent initial game state to new client: " + serializedGame.Substring(0, Mathf.Min(30, serializedGame.Length)) + "...");
            }
        }
    }

    /// <summary>
    /// Updates all piece interactivity based on current turn and network state
    /// </summary>
    public void RefreshAllPiecesInteractivity() {
        // Find all ChessNetworkPieceController components in the scene
        ChessNetworkPieceController[] controllers = FindObjectsOfType<ChessNetworkPieceController>();
        
        // Force update on each controller
        foreach (ChessNetworkPieceController controller in controllers) {
            controller.ForceUpdateInteractivity();
        }
        
        if (verbose) Debug.Log($"Refreshed interactivity for {controllers.Length} pieces. Current turn: {GameManager.Instance.SideToMove}");
    }

    /// <summary>
    /// Callback when a client disconnects from the network.
    /// </summary>
    private void OnClientDisconnected(ulong clientId) {
        if (clientId == NetworkManager.Singleton.LocalClientId) {
            // We disconnected
            if (NetworkManager.Singleton.IsHost || !allowReconnection || intentionalDisconnect) {
                // If we're the host or reconnection is disabled, go back to main menu
                UpdateConnectionStatus("Disconnected");
                gameStarted = false;
                wasConnected = false;
                
                // Show network panel again
                if (networkPanel != null) networkPanel.SetActive(true);
                if (statusPanel != null) statusPanel.SetActive(false);
                if (reconnectionPanel != null) reconnectionPanel.SetActive(false);
                if (disconnectButton != null) disconnectButton.interactable = false;
                if (hostButton != null) hostButton.interactable = true;
                if (clientButton != null) clientButton.interactable = true;
            } else {
                // If we're a client and reconnection is allowed, show reconnection UI
                UpdateConnectionStatus("Disconnected - Can reconnect");
                disconnectTime = Time.time;
                
                // If using NetworkReconnectionManager, let it handle reconnection
                if (useReconnectionManager && NetworkReconnectionManager.Instance != null) {
                    // NetworkReconnectionManager will handle showing reconnection UI
                } else {
                    // Show reconnection panel
                    if (reconnectionPanel != null) {
                        reconnectionPanel.SetActive(true);
                    }
                    
                    // Hide status panel
                    if (statusPanel != null) {
                        statusPanel.SetActive(false);
                    }
                }
            }
        } else {
            // Remote client disconnected - handle gracefully with PlayerConnectionManager
            UpdateConnectionStatus("Player Disconnected");
            UpdateStatusPanel();
            
            // If we're the host, notify other clients about player disconnection
            if (NetworkManager.Singleton.IsHost) {
                var player = playerConnectionManager.GetPlayerInfo(clientId);
                if (player != null) {
                    int sideAsInt = player.AssignedSide == Side.White ? 0 : 1;
                    UpdatePlayerConnectionStatusClientRpc(clientId, false, player.PlayerName, sideAsInt);
                }
            }
        }
    }

    /// <summary>
    /// Updates the connection status text in the UI.
    /// </summary>
    private void UpdateConnectionStatus(string status) {
        if (connectionStatusText != null) {
            connectionStatusText.text = $"Status: {status}";
            
            // Change color based on the status type
            if (status.StartsWith("Error")) {
                connectionStatusText.color = Color.red;
            } else if (status.Contains("Connected") || status.Contains("Hosting")) {
                connectionStatusText.color = Color.green;
            } else {
                connectionStatusText.color = Color.white;
            }
        }
        Debug.Log($"Network Status: {status}");
        
        // Notify observers of the status change
        ConnectionStatusChangedEvent?.Invoke(status);
    }

    /// <summary>
    /// Gets the side of the local player (White or Black).
    /// </summary>
    public Side GetLocalPlayerSide() {
        return localPlayerSide;
    }
    
    /// <summary>
    /// Checks if the game is in a waiting state (e.g., waiting for a player to reconnect)
    /// </summary>
    public bool IsWaitingForReconnection() {
        if (!allowReconnection || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return false;
        
        // Check if any players are disconnected but still within their reconnection window
        List<PlayerConnectionManager.PlayerInfo> disconnectedPlayers = playerConnectionManager.GetDisconnectedPlayers();
        return disconnectedPlayers.Count > 0;
    }
    
    /// <summary>
    /// Gets the list of players who are disconnected but can reconnect
    /// </summary>
    public List<PlayerConnectionManager.PlayerInfo> GetReconnectablePlayers() {
        return playerConnectionManager.GetDisconnectedPlayers();
    }
    
    /// <summary>
    /// Syncs the current game state to all clients.
    /// This is the core method that ensures synchronization between host and clients.
    /// </summary>
    [ClientRpc]
    public void SyncGameStateClientRpc(string serializedGameState) {
        // If we're the client, apply the state from the host
        if (!NetworkManager.Singleton.IsHost) {
            if (verbose) Debug.Log("[CLIENT] Received game state from host: " + serializedGameState.Substring(0, Mathf.Min(30, serializedGameState.Length)) + "...");
       
            // Load the serialized game state from the host
            GameManager.Instance.LoadGame(serializedGameState);
       
            // Update piece interactivity
            RefreshAllPiecesInteractivity();
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

        // In network mode:
        // 1. Check if it's the player's piece
        bool isPlayersPiece = pieceSide == localPlayerSide;
        
        // 2. Check if it's their turn via TurnManager
        bool isPlayersTurn = turnManager.CanPlayerMove(localPlayerSide);

        if (verbose) Debug.Log($"Can move check: Player's piece: {isPlayersPiece}, Player's turn: {isPlayersTurn}, Local side: {localPlayerSide}, Piece side: {pieceSide}");
       
        return isPlayersPiece && isPlayersTurn;
    }

    /// <summary>
    /// Notifies the host that a client has made a move.
    /// Called by client when they make a move.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void NotifyMoveServerRpc(string serializedMove) {
        if (NetworkManager.Singleton.IsHost) {
            if (verbose) Debug.Log("[HOST] Received move notification from client");
           
            // In our simplified approach, just sync the game state back to clients
            SyncGameStateClientRpc(GameManager.Instance.SerializeGame());
        }
    }

    /// <summary>
    /// Directly broadcasts the current game state to all clients.
    /// Called after a player makes a move.
    /// </summary>
    public void BroadcastCurrentGameState() {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient) return;
       
        if (NetworkManager.Singleton.IsHost) {
            string serializedGame = GameManager.Instance.SerializeGame();
            lastSyncedState = serializedGame; // Update last synced state
            lastSyncedMoveCount = GameManager.Instance.LatestHalfMoveIndex; // Update last synced move count
            SyncGameStateClientRpc(serializedGame);
            if (verbose) Debug.Log("[HOST] Broadcasting game state after move: " + serializedGame.Substring(0, Mathf.Min(30, serializedGame.Length)) + "...");
        } else {
            // If we're a client, notify the host about our move
            string serializedGame = GameManager.Instance.SerializeGame();
            NotifyMoveServerRpc(serializedGame);
            if (verbose) Debug.Log("[CLIENT] Notifying host of move: " + serializedGame.Substring(0, Mathf.Min(30, serializedGame.Length)) + "...");
        }
    }
   
    /// <summary>
    /// Updates a client about another player's connection status
    /// </summary>
    [ClientRpc]
    public void UpdatePlayerConnectionStatusClientRpc(ulong clientId, bool isConnected, string playerName, int playerSide) {
        if (!NetworkManager.Singleton.IsHost) {
            if (verbose) Debug.Log($"Player {playerName} is now {(isConnected ? "connected" : "disconnected")}");
           
            // Update UI or game state based on the other player's connection status
            Side side = playerSide == 0 ? Side.White : Side.Black;
           
            // If playing against a disconnected player, we may want to pause the game or show a waiting message
            if (!isConnected) {
                // Example: Show a message that opponent disconnected
                UpdateConnectionStatus($"Opponent {playerName} disconnected. Waiting for reconnection...");
            } else if (isConnected) {
                // Hide the disconnection message
                UpdateConnectionStatus("Opponent reconnected");
            }
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
            RefreshAllPiecesInteractivity();
        }
    }
   
    /// <summary>
    /// Requests the host to send the current game state (for reconnection)
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestSyncGameStateServerRpc() {
        if (NetworkManager.Singleton.IsHost) {
            if (verbose) Debug.Log("[HOST] Client requested game state sync");
           
            // Send the current game state to all clients
            string serializedGame = GameManager.Instance.SerializeGame();
            SyncGameStateClientRpc(serializedGame);
        }
    }
   
    /// <summary>
    /// Sets the player name for the session
    /// </summary>
    public void SetPlayerName(string name) {
        if (playerConnectionManager != null) {
            playerConnectionManager.SetPlayerName(name);
        }
    }
   
    /// <summary>
    /// Gets a reference to the PlayerConnectionManager
    /// </summary>
    public PlayerConnectionManager GetPlayerConnectionManager() {
        if (playerConnectionManagerRef == null) {
            playerConnectionManagerRef = GetComponent<PlayerConnectionManager>();
           
            // Auto-add if missing
            if (playerConnectionManagerRef == null) {
                playerConnectionManagerRef = gameObject.AddComponent<PlayerConnectionManager>();
            }
        }
       
        return playerConnectionManagerRef;
    }
   
    /// <summary>
    /// Checks if the current disconnect was intentional
    /// </summary>
    public bool IsIntentionalDisconnect() {
        return intentionalDisconnect;
    }
}