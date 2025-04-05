using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityChess;

/// <summary>
/// Manages chess game sessions including creation, joining, and session state.
/// Handles generation and validation of join codes and tracks active sessions.
/// </summary>
public class SessionManager : MonoBehaviour
{
    [Serializable]
    public class GameSession
    {
        public string SessionId;
        public string HostName;
        public string HostAddress;
        public ushort HostPort;
        public DateTime CreationTime;
        public int PlayerCount;
        public bool IsPrivate;
    }
    
    // Singleton instance
    public static SessionManager Instance { get; private set; }
    
    [Header("Session Settings")]
    [SerializeField] private bool useRandomizedPorts = false;
    [SerializeField] private ushort minPort = 7777;
    [SerializeField] private ushort maxPort = 7877;
    [SerializeField] private int sessionCodeLength = 6;
    [SerializeField] private float sessionTimeout = 3600f; // 1 hour
    
    // The current session
    private GameSession currentSession;
    
    // Debug mode
    [SerializeField] private bool verbose = true;
    
    // Reference to NetworkManager
    private NetworkManager networkManager;
    
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
        
        // Get NetworkManager reference
        networkManager = NetworkManager.Singleton;
    }
    
    private void Start()
    {
        // Subscribe to network events
        if (networkManager != null)
        {
            networkManager.OnServerStarted += OnServerStarted;
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (networkManager != null)
        {
            networkManager.OnServerStarted -= OnServerStarted;
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
    
    /// <summary>
    /// Called when the server starts
    /// </summary>
    private void OnServerStarted()
    {
        if (verbose) Debug.Log("Server started");
    }
    
    /// <summary>
    /// Called when a client connects
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        if (currentSession != null)
        {
            currentSession.PlayerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
            if (verbose) Debug.Log($"Client connected to session {currentSession.SessionId}. Total players: {currentSession.PlayerCount}");
        }
    }
    
    /// <summary>
    /// Called when a client disconnects
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        if (currentSession != null)
        {
            currentSession.PlayerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
            if (verbose) Debug.Log($"Client disconnected from session {currentSession.SessionId}. Total players: {currentSession.PlayerCount}");
        }
    }
    
    /// <summary>
    /// Creates a new game session
    /// </summary>
    public GameSession CreateSession(string hostName, bool isPrivate = false)
    {
        try
        {
            // Get the transport component
            var transport = networkManager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("Transport component not found");
                return null;
            }
            
            // Generate a random port if needed
            if (useRandomizedPorts)
            {
                transport.ConnectionData.Port = GetRandomPort();
            }
            
            // Create a new session
            currentSession = new GameSession
            {
                SessionId = GenerateSessionCode(),
                HostName = hostName,
                HostAddress = transport.ConnectionData.Address,
                HostPort = transport.ConnectionData.Port,
                CreationTime = DateTime.Now,
                PlayerCount = 1,
                IsPrivate = isPrivate
            };
            
            if (verbose) Debug.Log($"Created session {currentSession.SessionId} for host {hostName}");
            
            return currentSession;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error creating session: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Joins an existing session by session code
    /// </summary>
    public bool JoinSession(string sessionCode, string playerName)
    {
        try
        {
            // Parse session code
            if (string.IsNullOrEmpty(sessionCode))
            {
                Debug.LogError("Invalid session code");
                return false;
            }
            
            // For a custom join code system, you would typically have a server where
            // you can look up the session details based on the code.
            // Since we're using direct connections, the code should encode the IP:port
            
            // Here we're assuming the join code is directly in the format IP:port or decryptable
            if (sessionCode.Contains(":"))
            {
                string[] parts = sessionCode.Split(':');
                if (parts.Length == 2 && ushort.TryParse(parts[1], out ushort port))
                {
                    // Set up the transport with the connection info
                    var transport = networkManager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                    if (transport != null)
                    {
                        transport.ConnectionData.Address = parts[0];
                        transport.ConnectionData.Port = port;
                        
                        // Create a placeholder session
                        currentSession = new GameSession
                        {
                            SessionId = sessionCode,
                            HostName = "Host", // Will be updated later
                            HostAddress = parts[0],
                            HostPort = port,
                            CreationTime = DateTime.Now,
                            PlayerCount = 2, // Host + this client
                            IsPrivate = true
                        };
                        
                        if (verbose) Debug.Log($"Joining session {sessionCode} as {playerName}");
                        return true;
                    }
                }
            }
            
            Debug.LogError("Invalid session code format");
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error joining session: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Leaves the current session
    /// </summary>
    public void LeaveSession()
    {
        if (currentSession != null)
        {
            if (verbose) Debug.Log($"Leaving session {currentSession.SessionId}");
            currentSession = null;
        }
    }
    
    /// <summary>
    /// Gets the current session
    /// </summary>
    public GameSession GetCurrentSession()
    {
        return currentSession;
    }
    
    /// <summary>
    /// Checks if the local player is the host of the current session
    /// </summary>
    public bool IsHost()
    {
        return networkManager != null && networkManager.IsHost;
    }
    
    /// <summary>
    /// Checks if the current session is active 
    /// </summary>
    public bool IsSessionActive()
    {
        return currentSession != null && networkManager != null && networkManager.IsConnectedClient;
    }
    
    /// <summary>
    /// Gets the current player count in the session
    /// </summary>
    public int GetPlayerCount()
    {
        return currentSession?.PlayerCount ?? 0;
    }
    
    /// <summary>
    /// Generates a random port number
    /// </summary>
    private ushort GetRandomPort()
    {
        return (ushort)UnityEngine.Random.Range(minPort, maxPort + 1);
    }
    
    /// <summary>
    /// Generates a unique session code
    /// </summary>
    private string GenerateSessionCode()
    {
        // In a real implementation, you might want to make this more secure
        // and ensure uniqueness against a database of active sessions
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Omitted confusing characters
        char[] stringChars = new char[sessionCodeLength];
        System.Random random = new System.Random();

        for (int i = 0; i < stringChars.Length; i++)
        {
            stringChars[i] = chars[random.Next(chars.Length)];
        }

        return new string(stringChars);
    }
    
    /// <summary>
    /// Gets the join code for the current session
    /// </summary>
    public string GetJoinCode()
    {
        if (currentSession == null)
        {
            return string.Empty;
        }
        
        // For our implementation, we'll use IP:port as the join code
        return $"{currentSession.HostAddress}:{currentSession.HostPort}";
    }
}