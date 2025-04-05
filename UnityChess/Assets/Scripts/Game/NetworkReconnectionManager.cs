using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityChess;

/// <summary>
/// Manages the reconnection process for players who disconnect during a game.
/// Handles reconnection timeouts, saving connection state, and restoring game state.
/// </summary>
public class NetworkReconnectionManager : MonoBehaviour
{
    // Singleton instance
    public static NetworkReconnectionManager Instance { get; private set; }
    
    // Reconnection settings
    [SerializeField] private float reconnectionTimeout = 30f;
    [SerializeField] private int maxReconnectionAttempts = 3;
    [SerializeField] private float reconnectionBackoff = 2f; // Exponential backoff multiplier
    
    // Data class to store session reconnection information
    [Serializable]
    public class ReconnectionData
    {
        public string SessionId;
        public string ConnectionData; // IP:port
        public Side PlayerSide;
        public DateTime DisconnectTime;
        public int LastHalfMoveIndex;
        public int ReconnectionAttempts;
        public bool IsActive;
    }
    
    // Storage for current reconnection data
    private ReconnectionData currentReconnectionData;
    
    // Dictionary to track disconnected players when hosting
    private Dictionary<ulong, ReconnectionData> disconnectedPlayers = new Dictionary<ulong, ReconnectionData>();
    
    // Reconnection callbacks
    public delegate void ReconnectionCallback(bool success, string message);
    public static event ReconnectionCallback OnReconnectionResult;
    
    // Coroutine reference
    private Coroutine reconnectionCoroutine;
    
    // Debug mode
    [SerializeField] private bool verbose = true;
    
    private void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
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
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        
        // Clean up coroutine
        if (reconnectionCoroutine != null)
        {
            StopCoroutine(reconnectionCoroutine);
        }
    }
    
    /// <summary>
    /// Handles when a client connects, checking if it's a reconnection
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        // Check if this is a reconnection
        if (currentReconnectionData != null && currentReconnectionData.IsActive)
        {
            if (verbose) Debug.Log($"Reconnected to session {currentReconnectionData.SessionId}");
            
            // Reconnection was successful
            currentReconnectionData.IsActive = false;
            
            // Notify subscribers
            OnReconnectionResult?.Invoke(true, "Successfully reconnected to the game");
            
            // Cancel reconnection coroutine if running
            if (reconnectionCoroutine != null)
            {
                StopCoroutine(reconnectionCoroutine);
                reconnectionCoroutine = null;
            }
            
            // Request game state synchronization
            if (ChessNetworkManager.Instance != null)
            {
                ChessNetworkManager.Instance.RequestSyncGameStateServerRpc();
            }
        }
        
        // If we're the host, check if this is a reconnecting client
        if (NetworkManager.Singleton.IsHost)
        {
            foreach (var kvp in disconnectedPlayers)
            {
                if (verbose) Debug.Log($"Client {clientId} connected, checking against disconnected players");
                
                // We can't directly match by clientId since it will be different on reconnection
                // In a real implementation, you would need a way to identify returning players
                // For now, we'll clean up any disconnected players when a new client connects
                
                // This would typically involve the PlayerConnectionManager verifying identity
            }
        }
    }
    
    /// <summary>
    /// Handles when a client disconnects, saving reconnection data if needed
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        // If we're the host, track the disconnected client
        if (NetworkManager.Singleton.IsHost && clientId != NetworkManager.Singleton.LocalClientId)
        {
            // Get player info from PlayerConnectionManager
            PlayerConnectionManager.PlayerInfo playerInfo = null;
            if (ChessNetworkManager.Instance != null)
            {
                playerInfo = ChessNetworkManager.Instance.GetPlayerConnectionManager().GetPlayerInfo(clientId);
            }
            
            if (playerInfo != null)
            {
                // Store reconnection data for this client
                ReconnectionData clientData = new ReconnectionData
                {
                    SessionId = SessionManager.Instance != null ? 
                        SessionManager.Instance.GetCurrentSession()?.SessionId : "unknown",
                    PlayerSide = playerInfo.AssignedSide,
                    DisconnectTime = DateTime.Now,
                    LastHalfMoveIndex = GameManager.Instance.LatestHalfMoveIndex,
                    ReconnectionAttempts = 0,
                    IsActive = true
                };
                
                disconnectedPlayers[clientId] = clientData;
                
                if (verbose) Debug.Log($"Stored reconnection data for client {clientId} (Side: {playerInfo.AssignedSide})");
            }
        }
        
        // If we're the client who disconnected (not intentionally)
        if (clientId == NetworkManager.Singleton.LocalClientId && 
            ChessNetworkManager.Instance != null && 
            !ChessNetworkManager.Instance.IsIntentionalDisconnect())
        {
            // Save our connection data for potential reconnection
            SaveReconnectionData();
            
            // Start reconnection process
            StartReconnectionProcess();
        }
    }
    
    /// <summary>
    /// Saves reconnection data for the local client
    /// </summary>
    private void SaveReconnectionData()
    {
        if (NetworkManager.Singleton == null) return;
        
        // Get the transport data
        string connectionData = "";
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            connectionData = $"{transport.ConnectionData.Address}:{transport.ConnectionData.Port}";
        }
        
        // Get the session ID
        string sessionId = "unknown";
        if (SessionManager.Instance != null && SessionManager.Instance.GetCurrentSession() != null)
        {
            sessionId = SessionManager.Instance.GetCurrentSession().SessionId;
        }
        
        // Get player side
        Side playerSide = Side.White;
        if (ChessNetworkManager.Instance != null)
        {
            playerSide = ChessNetworkManager.Instance.GetLocalPlayerSide();
        }
        
        // Create reconnection data
        currentReconnectionData = new ReconnectionData
        {
            SessionId = sessionId,
            ConnectionData = connectionData,
            PlayerSide = playerSide,
            DisconnectTime = DateTime.Now,
            LastHalfMoveIndex = GameManager.Instance.LatestHalfMoveIndex,
            ReconnectionAttempts = 0,
            IsActive = true
        };
        
        if (verbose) Debug.Log($"Saved reconnection data: {connectionData}, Side: {playerSide}");
    }
    
    /// <summary>
    /// Starts the reconnection process for the local client
    /// </summary>
    private void StartReconnectionProcess()
    {
        if (currentReconnectionData == null || !currentReconnectionData.IsActive)
        {
            if (verbose) Debug.Log("No active reconnection data available");
            return;
        }
        
        // Cancel any existing reconnection coroutine
        if (reconnectionCoroutine != null)
        {
            StopCoroutine(reconnectionCoroutine);
        }
        
        // Start reconnection coroutine
        reconnectionCoroutine = StartCoroutine(ReconnectionCoroutine());
        
        if (verbose) Debug.Log("Started reconnection process");
    }
    
    /// <summary>
    /// Coroutine to handle the reconnection process with exponential backoff
    /// </summary>
    private IEnumerator ReconnectionCoroutine()
    {
        if (currentReconnectionData == null) yield break;
        
        // Initial delay before first reconnection attempt
        yield return new WaitForSeconds(1f);
        
        while (currentReconnectionData.IsActive && 
               currentReconnectionData.ReconnectionAttempts < maxReconnectionAttempts &&
               (DateTime.Now - currentReconnectionData.DisconnectTime).TotalSeconds < reconnectionTimeout)
        {
            // Increment attempt counter
            currentReconnectionData.ReconnectionAttempts++;
            
            if (verbose) Debug.Log($"Reconnection attempt {currentReconnectionData.ReconnectionAttempts}/{maxReconnectionAttempts}");
            
            // Try to reconnect
            if (AttemptReconnection())
            {
                // Wait for connection result (success will be handled in OnClientConnected)
                float waitTime = 5f; // Wait up to 5 seconds for connection
                float startTime = Time.time;
                
                while (Time.time - startTime < waitTime && currentReconnectionData.IsActive)
                {
                    yield return new WaitForSeconds(0.5f);
                    
                    // If we get connected, OnClientConnected will set IsActive to false
                    if (NetworkManager.Singleton.IsConnectedClient)
                    {
                        yield break;
                    }
                }
            }
            
            // If we're still active, the reconnection failed
            if (currentReconnectionData.IsActive)
            {
                // Calculate backoff time based on attempt number
                float backoffTime = Mathf.Pow(reconnectionBackoff, currentReconnectionData.ReconnectionAttempts - 1);
                
                if (verbose) Debug.Log($"Reconnection failed, waiting {backoffTime} seconds before retry");
                
                // Wait before next attempt with exponential backoff
                yield return new WaitForSeconds(backoffTime);
            }
        }
        
        // If we get here, all reconnection attempts failed or timed out
        if (currentReconnectionData.IsActive)
        {
            if (verbose) Debug.Log("Reconnection process failed");
            
            // Notify subscribers
            OnReconnectionResult?.Invoke(false, "Failed to reconnect to the game");
            
            // Cleanup
            currentReconnectionData.IsActive = false;
            
            // Return to main menu or show appropriate UI
            ChessNetworkManager.Instance?.OnReconnectionFailed();
        }
    }
    
    /// <summary>
    /// Attempts to reconnect to the session
    /// </summary>
    public bool AttemptReconnection()
    {
        if (currentReconnectionData == null || !currentReconnectionData.IsActive)
        {
            if (verbose) Debug.Log("No active reconnection data available");
            return false;
        }
        
        try
        {
            if (string.IsNullOrEmpty(currentReconnectionData.ConnectionData))
            {
                if (verbose) Debug.LogError("No connection data available");
                return false;
            }
            
            // Parse connection data
            string[] parts = currentReconnectionData.ConnectionData.Split(':');
            if (parts.Length != 2 || !ushort.TryParse(parts[1], out ushort port))
            {
                if (verbose) Debug.LogError("Invalid connection data format");
                return false;
            }
            
            // Set up the transport
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport == null)
            {
                if (verbose) Debug.LogError("Transport component not found");
                return false;
            }
            
            transport.ConnectionData.Address = parts[0];
            transport.ConnectionData.Port = port;
            
            if (verbose) Debug.Log($"Attempting to reconnect to {parts[0]}:{port}");
            
            // Start the client
            return NetworkManager.Singleton.StartClient();
        }
        catch (Exception ex)
        {
            if (verbose) Debug.LogError($"Error during reconnection: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Notifies that a reconnection was successful
    /// </summary>
    public void NotifyReconnectionSuccess()
    {
        if (currentReconnectionData != null)
        {
            currentReconnectionData.IsActive = false;
        }
        
        OnReconnectionResult?.Invoke(true, "Successfully reconnected to the game");
    }
    
    /// <summary>
    /// Notifies that a reconnection failed
    /// </summary>
    public void NotifyReconnectionFailure(string reason)
    {
        if (currentReconnectionData != null)
        {
            currentReconnectionData.IsActive = false;
        }
        
        OnReconnectionResult?.Invoke(false, reason);
    }
    
    /// <summary>
    /// Cancels the current reconnection process
    /// </summary>
    public void CancelReconnection()
    {
        if (reconnectionCoroutine != null)
        {
            StopCoroutine(reconnectionCoroutine);
            reconnectionCoroutine = null;
        }
        
        if (currentReconnectionData != null)
        {
            currentReconnectionData.IsActive = false;
        }
        
        if (verbose) Debug.Log("Reconnection process cancelled");
    }
    
    /// <summary>
    /// Cleans up the reconnection data for a player when they fully reconnect
    /// </summary>
    public void CleanupReconnectionData(ulong clientId)
    {
        if (disconnectedPlayers.ContainsKey(clientId))
        {
            disconnectedPlayers.Remove(clientId);
            if (verbose) Debug.Log($"Cleaned up reconnection data for client {clientId}");
        }
    }
    
    /// <summary>
    /// Gets the current reconnection timeout
    /// </summary>
    public float GetReconnectionTimeout()
    {
        return reconnectionTimeout;
    }
    
    /// <summary>
    /// Gets the remaining time for reconnection
    /// </summary>
    public float GetRemainingReconnectionTime()
    {
        if (currentReconnectionData == null || !currentReconnectionData.IsActive)
        {
            return 0f;
        }
        
        float elapsedTime = (float)(DateTime.Now - currentReconnectionData.DisconnectTime).TotalSeconds;
        return Mathf.Max(0f, reconnectionTimeout - elapsedTime);
    }
    
    /// <summary>
    /// Checks if a reconnection is in progress
    /// </summary>
    public bool IsReconnecting()
    {
        return currentReconnectionData != null && currentReconnectionData.IsActive;
    }
    
    /// <summary>
    /// Gets the number of disconnected players
    /// </summary>
    public int GetDisconnectedPlayerCount()
    {
        return disconnectedPlayers.Count;
    }
    
    /// <summary>
    /// Checks if a player has disconnected from the host's perspective
    /// </summary>
    public bool HasPlayerDisconnected(Side playerSide)
    {
        foreach (var data in disconnectedPlayers.Values)
        {
            if (data.PlayerSide == playerSide && data.IsActive)
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Gets connection data for the current session (for reconnection)
    /// </summary>
    public string GetConnectionData()
    {
        return currentReconnectionData?.ConnectionData ?? "";
    }
}