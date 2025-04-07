using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityChess;
using UnityEngine;

/// <summary>
/// Manages player connections in a networked chess game, including handling player joins,
/// disconnects, and reconnections. Tracks player information and provides events for connection status changes.
/// </summary>
public class PlayerConnectionManager : MonoBehaviour
{
    // Events for notifying connection status changes
    public static event Action<string> ConnectionStatusChangedEvent;
    public static event Action<PlayerInfo> PlayerConnectedEvent;
    public static event Action<PlayerInfo> PlayerDisconnectedEvent;
    public static event Action<PlayerInfo> PlayerReconnectedEvent;

    // Class to store player information
    [Serializable]
    public class PlayerInfo
    {
        public ulong ClientId;
        public string PlayerName;
        public Side AssignedSide;
        public bool IsConnected;
        public float DisconnectTime;
        public string ConnectionData; // For reconnection purposes
    }

    // Dictionary to track all players by their client ID
    private Dictionary<ulong, PlayerInfo> players = new Dictionary<ulong, PlayerInfo>();
    
    // Local player name
    private string localPlayerName = "Player";
    
    // Reconnection timeout in seconds
    [SerializeField] private float reconnectionTimeout = 30f;
    
    // Debug logging
    [SerializeField] private bool verbose = true;
    
    private void Awake()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }
    
    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
    
    /// <summary>
    /// Sets the local player's name for display in the game
    /// </summary>
    public void SetPlayerName(string name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            localPlayerName = name;
            if (verbose) Debug.Log($"Player name set to: {name}");
        }
    }
    
    /// <summary>
    /// Handles when a client connects to the game
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton.IsHost && clientId != NetworkManager.Singleton.LocalClientId)
        {
            // Add the remote player (clients are always assigned to Black)
            PlayerInfo newPlayer = new PlayerInfo
            {
                ClientId = clientId,
                PlayerName = "Opponent", // Will be updated via RPC later
                AssignedSide = Side.Black,
                IsConnected = true,
                DisconnectTime = 0f
            };
            
            players[clientId] = newPlayer;
            
            if (verbose) Debug.Log($"Player connected: {clientId} (Black)");
            
            // Notify others of the connection
            PlayerConnectedEvent?.Invoke(newPlayer);
            ConnectionStatusChangedEvent?.Invoke("Player Connected");
            
            // Request player name from the client
            RequestPlayerNameServerRpc(clientId);
        }
        else if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            // Add the local player (host is White, client is Black)
            Side localSide = NetworkManager.Singleton.IsHost ? Side.White : Side.Black;
            
            PlayerInfo localPlayer = new PlayerInfo
            {
                ClientId = clientId,
                PlayerName = localPlayerName,
                AssignedSide = localSide,
                IsConnected = true,
                DisconnectTime = 0f
            };
            
            players[clientId] = localPlayer;
            
            if (verbose) Debug.Log($"Local player added: {clientId} ({localSide})");
            
            // If we're a client, send our name to the host
            if (!NetworkManager.Singleton.IsHost)
            {
                SendPlayerNameServerRpc(localPlayerName);
            }
        }
    }
    
    /// <summary>
    /// Handles when a client disconnects from the game
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        if (players.TryGetValue(clientId, out PlayerInfo player))
        {
            player.IsConnected = false;
            player.DisconnectTime = Time.time;
            
            if (verbose) Debug.Log($"Player disconnected: {player.PlayerName} ({player.AssignedSide})");
            
            // Notify others of the disconnection
            PlayerDisconnectedEvent?.Invoke(player);
            ConnectionStatusChangedEvent?.Invoke("Player Disconnected");
        }
    }
    
    /// <summary>
    /// Handles a player reconnection
    /// </summary>
    public void HandlePlayerReconnection(ulong clientId)
    {
        foreach (var player in players.Values)
        {
            if (!player.IsConnected && Time.time - player.DisconnectTime <= reconnectionTimeout)
            {
                // This is a reconnection - update the player info
                player.ClientId = clientId;
                player.IsConnected = true;
                
                if (verbose) Debug.Log($"Player reconnected: {player.PlayerName} ({player.AssignedSide})");
                
                // Notify others of the reconnection
                PlayerReconnectedEvent?.Invoke(player);
                ConnectionStatusChangedEvent?.Invoke("Player Reconnected");
                return;
            }
        }
        
        // If we get here, treat it as a new connection
        OnClientConnected(clientId);
    }
    
    /// <summary>
    /// Gets a list of all connected players
    /// </summary>
    public List<PlayerInfo> GetConnectedPlayers()
    {
        List<PlayerInfo> connectedPlayers = new List<PlayerInfo>();
        foreach (var player in players.Values)
        {
            if (player.IsConnected)
            {
                connectedPlayers.Add(player);
            }
        }
        return connectedPlayers;
    }
    
    /// <summary>
    /// Gets a list of all disconnected players that are within the reconnection window
    /// </summary>
    public List<PlayerInfo> GetDisconnectedPlayers()
    {
        List<PlayerInfo> disconnectedPlayers = new List<PlayerInfo>();
        
        foreach (var player in players.Values)
        {
            if (!player.IsConnected && Time.time - player.DisconnectTime <= reconnectionTimeout)
            {
                disconnectedPlayers.Add(player);
            }
        }
        
        return disconnectedPlayers;
    }
    
    /// <summary>
    /// Gets player information by client ID
    /// </summary>
    public PlayerInfo GetPlayerInfo(ulong clientId)
    {
        if (players.TryGetValue(clientId, out PlayerInfo info))
        {
            return info;
        }
        return null;
    }
    
    // *** New: Request player's name from a connected client ***
    [ServerRpc(RequireOwnership = false)]
    public void RequestPlayerNameServerRpc(ulong targetClientId) {
        // This could trigger a ClientRpc to request the name if needed.
        Debug.Log($"Requesting player name from client {targetClientId}");
    }

    // *** New: Receive the player's name from the client ***
    [ServerRpc(RequireOwnership = false)]
    public void SendPlayerNameServerRpc(string playerName) {
        // Using the sender's client ID from the context.
        ulong senderClientId = NetworkManager.Singleton.LocalClientId; // For demonstration; ideally use RpcContext.
        if (players.ContainsKey(senderClientId)) {
            players[senderClientId].PlayerName = playerName;
            Debug.Log($"Received player name '{playerName}' from client {senderClientId}");
        }
    }
    
    /// <summary>
    /// Asks a specific client to send their player name
    /// </summary>
    [ClientRpc]
    private void RequestPlayerNameClientRpc(ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == targetClientId)
        {
            SendPlayerNameServerRpc(localPlayerName);
        }
    }
    
    /// <summary>
    /// Broadcast a player's name to all clients
    /// </summary>
    [ClientRpc]
    private void BroadcastPlayerNameClientRpc(ulong clientId, string playerName)
    {
        if (players.TryGetValue(clientId, out PlayerInfo player))
        {
            player.PlayerName = playerName;
            if (verbose) Debug.Log($"Received player name update: {playerName} for client {clientId}");
        }
    }
    
    /// <summary>
    /// Store connection data for potential reconnection
    /// </summary>
    public void StoreConnectionData(ulong clientId, string connectionData)
    {
        if (players.TryGetValue(clientId, out PlayerInfo player))
        {
            player.ConnectionData = connectionData;
        }
    }
    
    /// <summary>
    /// Get stored connection data for a player
    /// </summary>
    public string GetConnectionData(ulong clientId)
    {
        if (players.TryGetValue(clientId, out PlayerInfo player))
        {
            return player.ConnectionData;
        }
        return string.Empty;
    }
    
    /// <summary>
    /// Clear all player data when the game is shutdown or restarted
    /// </summary>
    public void ClearAllPlayerData()
    {
        players.Clear();
    }
}