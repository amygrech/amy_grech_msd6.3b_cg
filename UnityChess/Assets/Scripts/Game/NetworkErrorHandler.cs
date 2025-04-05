using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Provides specialized error handling and diagnostic capabilities
/// for network-related operations in the chess game.
/// </summary>
public class NetworkErrorHandler : MonoBehaviour
{
    // Singleton instance
    public static NetworkErrorHandler Instance { get; private set; }
    
    // Error callback delegates
    public delegate void NetworkErrorCallback(NetworkErrorType errorType, string message);
    public static event NetworkErrorCallback OnNetworkError;
    
    // Define error types
    public enum NetworkErrorType
    {
        ConnectionFailed,
        ConnectionTimeout,
        Disconnected,
        ReconnectionFailed,
        JoinCodeInvalid,
        SessionFull,
        ServerError,
        ClientError,
        Unknown
    }
    
    // Track recent errors to avoid spamming
    private Dictionary<NetworkErrorType, float> lastErrorTimes = new Dictionary<NetworkErrorType, float>();
    private float errorCooldown = 2.0f;
    
    // Define error messages
    private Dictionary<NetworkErrorType, string> errorMessages = new Dictionary<NetworkErrorType, string>()
    {
        { NetworkErrorType.ConnectionFailed, "Failed to connect to the host. Check your internet connection and try again." },
        { NetworkErrorType.ConnectionTimeout, "Connection timed out. The host may be unavailable." },
        { NetworkErrorType.Disconnected, "You have been disconnected from the game." },
        { NetworkErrorType.ReconnectionFailed, "Failed to reconnect to the game." },
        { NetworkErrorType.JoinCodeInvalid, "The join code is invalid or expired." },
        { NetworkErrorType.SessionFull, "This game is already full." },
        { NetworkErrorType.ServerError, "A server error occurred." },
        { NetworkErrorType.ClientError, "A client error occurred." },
        { NetworkErrorType.Unknown, "An unknown network error occurred." }
    };
    
    // Network diagnostics data
    [Serializable]
    public class NetworkDiagnostics
    {
        public float LastRTT = 0;
        public int PacketLoss = 0;
        public int ConnectionAttempts = 0;
        public int SuccessfulConnections = 0;
        public int DisconnectCount = 0;
        public Dictionary<NetworkErrorType, int> ErrorCounts = new Dictionary<NetworkErrorType, int>();
    }
    
    // Diagnostics data
    public NetworkDiagnostics Diagnostics { get; private set; } = new NetworkDiagnostics();
    
    // Connection settings
    [SerializeField] private float connectionTimeout = 15f;
    [SerializeField] private int maxConnectionAttempts = 3;
    
    // Current connection attempt
    private int currentConnectionAttempt = 0;
    private Coroutine connectionTimeoutCoroutine;
    
    // Debug mode
    [SerializeField] private bool verbose = true;
    
    private void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Initialize error counts
            foreach (NetworkErrorType errorType in Enum.GetValues(typeof(NetworkErrorType)))
            {
                Diagnostics.ErrorCounts[errorType] = 0;
            }
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    private void Start()
    {
        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        }
        
        // Cancel any coroutines
        if (connectionTimeoutCoroutine != null)
        {
            StopCoroutine(connectionTimeoutCoroutine);
        }
    }
    
    /// <summary>
    /// Records when a client connects
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        // Reset connection attempt counter on successful connection
        currentConnectionAttempt = 0;
        Diagnostics.SuccessfulConnections++;
        
        // Cancel any timeout coroutine
        if (connectionTimeoutCoroutine != null)
        {
            StopCoroutine(connectionTimeoutCoroutine);
            connectionTimeoutCoroutine = null;
        }
    }
    
    /// <summary>
    /// Records when a client disconnects
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        Diagnostics.DisconnectCount++;
        
        // Only report disconnection as an error if it's not an intentional disconnect
        // (we would need to track this separately in the ChessNetworkManager)
        if (ChessNetworkManager.Instance != null && !ChessNetworkManager.Instance.IsIntentionalDisconnect())
        {
            ReportError(NetworkErrorType.Disconnected, $"Disconnected from session. Client ID: {clientId}");
        }
    }
    
    /// <summary>
    /// Handles transport failures
    /// </summary>
    private void OnTransportFailure()
    {
        ReportError(NetworkErrorType.ConnectionFailed, "Transport failure occurred");
    }
    
    /// <summary>
    /// Reports a network error and triggers callbacks
    /// </summary>
    public void ReportError(NetworkErrorType errorType, string customMessage = null)
    {
        // Check if we're on cooldown for this error type
        if (lastErrorTimes.TryGetValue(errorType, out float lastTime) && 
            Time.time - lastTime < errorCooldown)
        {
            // Skip reporting during cooldown
            return;
        }
        
        // Update last error time
        lastErrorTimes[errorType] = Time.time;
        
        // Increment error count
        Diagnostics.ErrorCounts[errorType]++;
        
        // Get error message
        string message = customMessage ?? errorMessages[errorType];
        
        // Log error
        if (verbose) Debug.LogError($"Network Error: {errorType} - {message}");
        
        // Trigger callback
        OnNetworkError?.Invoke(errorType, message);
    }
    
    /// <summary>
    /// Starts monitoring a connection attempt with timeout
    /// </summary>
    public void MonitorConnectionAttempt()
    {
        // Increment attempt counter
        currentConnectionAttempt++;
        Diagnostics.ConnectionAttempts++;
        
        // Cancel any existing timeout coroutine
        if (connectionTimeoutCoroutine != null)
        {
            StopCoroutine(connectionTimeoutCoroutine);
        }
        
        // Start new timeout coroutine
        connectionTimeoutCoroutine = StartCoroutine(ConnectionTimeoutCoroutine());
        
        if (verbose) Debug.Log($"Monitoring connection attempt {currentConnectionAttempt}/{maxConnectionAttempts}");
    }
    
    /// <summary>
    /// Coroutine to handle connection timeout
    /// </summary>
    private IEnumerator ConnectionTimeoutCoroutine()
    {
        float startTime = Time.time;
        
        // Wait for the timeout period
        while (Time.time - startTime < connectionTimeout)
        {
            // If we get connected, this coroutine will be stopped in OnClientConnected
            yield return new WaitForSeconds(0.5f);
        }
        
        // If we get here, the connection timed out
        connectionTimeoutCoroutine = null;
        
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
        {
            ReportError(NetworkErrorType.ConnectionTimeout, $"Connection timed out after {connectionTimeout} seconds");
            
            // Check if we should auto-retry
            if (currentConnectionAttempt < maxConnectionAttempts)
            {
                if (verbose) Debug.Log($"Auto-retrying connection (attempt {currentConnectionAttempt + 1}/{maxConnectionAttempts})");
                
                // Tell the ChessNetworkManager to retry the connection
                if (ChessNetworkManager.Instance != null)
                {
                    ChessNetworkManager.Instance.RetryConnection();
                }
            }
            else
            {
                if (verbose) Debug.Log($"Max connection attempts reached ({maxConnectionAttempts})");
                
                // Tell the ChessNetworkManager to abort
                if (ChessNetworkManager.Instance != null)
                {
                    ChessNetworkManager.Instance.Disconnect();
                }
            }
        }
    }
    
    /// <summary>
    /// Validates a join code
    /// </summary>
    public bool ValidateJoinCode(string joinCode)
    {
        // Basic validation
        if (string.IsNullOrEmpty(joinCode))
        {
            ReportError(NetworkErrorType.JoinCodeInvalid, "Join code cannot be empty");
            return false;
        }
        
        // For our implementation, join codes are IP:port or session codes
        if (joinCode.Contains(":"))
        {
            // IP:port format
            string[] parts = joinCode.Split(':');
            if (parts.Length == 2 && ushort.TryParse(parts[1], out _))
            {
                return true;
            }
        }
        else
        {
            // Session code format (for a more advanced implementation)
            // Here you would validate against a database or server
            
            // For now, we'll just do basic format validation
            // Assuming session codes are alphanumeric and 6 characters
            if (System.Text.RegularExpressions.Regex.IsMatch(joinCode, "^[A-Z0-9]{6}$"))
            {
                return true;
            }
        }
        
        ReportError(NetworkErrorType.JoinCodeInvalid, "Invalid join code format");
        return false;
    }
    
    /// <summary>
    /// Gets RTT (round-trip time) for diagnostics
    /// </summary>
    public void UpdateNetworkStats()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
        {
            // Get the UTP transport
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                // UTP doesn't expose RTT directly in Unity Netcode
                // In a real implementation, you would need custom code to measure this
                // This is just a placeholder
                Diagnostics.LastRTT = UnityEngine.Random.Range(20, 200);
                Diagnostics.PacketLoss = UnityEngine.Random.Range(0, 5);
            }
        }
    }
    
    /// <summary>
    /// Resets the diagnostics data
    /// </summary>
    public void ResetDiagnostics()
    {
        Diagnostics = new NetworkDiagnostics();
        
        // Initialize error counts
        foreach (NetworkErrorType errorType in Enum.GetValues(typeof(NetworkErrorType)))
        {
            Diagnostics.ErrorCounts[errorType] = 0;
        }
    }
    
    /// <summary>
    /// Gets a full diagnostics report as a string
    /// </summary>
    public string GetDiagnosticsReport()
    {
        string report = "Network Diagnostics Report:\n";
        report += $"Connection Attempts: {Diagnostics.ConnectionAttempts}\n";
        report += $"Successful Connections: {Diagnostics.SuccessfulConnections}\n";
        report += $"Disconnections: {Diagnostics.DisconnectCount}\n";
        report += $"Current RTT: {Diagnostics.LastRTT}ms\n";
        report += $"Packet Loss: {Diagnostics.PacketLoss}%\n";
        report += "Errors:\n";
        
        foreach (var error in Diagnostics.ErrorCounts)
        {
            if (error.Value > 0)
            {
                report += $"- {error.Key}: {error.Value}\n";
            }
        }
        
        return report;
    }
}