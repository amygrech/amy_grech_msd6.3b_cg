using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Logs additional server-side network statistics to the Unity console.
/// Attach this to the NetworkManager GameObject.
/// </summary>
public class ServerLatencyLogger : NetworkBehaviour
{
    [Header("Configuration")]
    [SerializeField] private float loggingInterval = 5.0f; // How often to log stats (seconds)
    [SerializeField] private bool logConnectionEvents = true;
    
    // Dictionary to store ping statistics for each client
    private Dictionary<ulong, ClientStats> clientStats = new Dictionary<ulong, ClientStats>();
    
    // Class to track statistics for each client
    private class ClientStats
    {
        public ulong clientId;
        public float lastRecordedPing;
        public float averagePing;
        public int messagesSent;
        public int messagesReceived;
        public float connectionTime;
        
        public ClientStats(ulong id)
        {
            clientId = id;
            connectionTime = Time.time;
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (!IsServer) return;
        
        // Register for network events
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        
        // Start logging coroutine
        StartCoroutine(LogServerStats());
    }
    
    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        
        base.OnNetworkDespawn();
    }
    
    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;
        
        // Add new client to tracking
        clientStats[clientId] = new ClientStats(clientId);
        
        if (logConnectionEvents)
        {
            Debug.Log($"[SERVER] Client {clientId} connected. Total clients: {NetworkManager.Singleton.ConnectedClientsList.Count}");
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;
        
        if (clientStats.TryGetValue(clientId, out ClientStats stats))
        {
            float sessionTime = Time.time - stats.connectionTime;
            
            if (logConnectionEvents)
            {
                Debug.Log($"[SERVER] Client {clientId} disconnected after {sessionTime:F1} seconds. " +
                          $"Average ping: {stats.averagePing:F1}ms. " +
                          $"Remaining clients: {NetworkManager.Singleton.ConnectedClientsList.Count}");
            }
            
            // Remove client from tracking
            clientStats.Remove(clientId);
        }
    }
    
    private IEnumerator LogServerStats()
    {
        while (IsServer && NetworkManager.Singleton.IsListening)
        {
            // Only log if we have clients connected
            if (NetworkManager.Singleton.ConnectedClientsList.Count > 0)
            {
                LogNetworkPerformance();
            }
            
            yield return new WaitForSeconds(loggingInterval);
        }
    }
    
    private void LogNetworkPerformance()
    {
        // Get NetworkManager metrics
        // Note: In a real implementation, you would use NetworkManager's actual metrics
        
        int connectedClients = NetworkManager.Singleton.ConnectedClientsList.Count;
        
        Debug.Log($"[SERVER] === Network Performance Report ===");
        Debug.Log($"[SERVER] Connected Clients: {connectedClients}");
        Debug.Log($"[SERVER] Server Time: {NetworkManager.Singleton.ServerTime.Time:F2}s");
        
        // Calculate average ping across all clients if we had that data
        // In this example, NetworkLatencyMonitor is gathering that data separately
        
        // Log server-specific metrics if available from NetworkManager
        // E.g., bandwidth usage, packets per second
        
        Debug.Log($"[SERVER] === End of Report ===");
    }
    
    // This method can be called from NetworkLatencyMonitor to update client ping info
    public void UpdateClientPing(ulong clientId, float ping)
    {
        if (!IsServer) return;
        
        if (clientStats.TryGetValue(clientId, out ClientStats stats))
        {
            stats.lastRecordedPing = ping;
            
            // Update running average
            if (stats.averagePing == 0)
                stats.averagePing = ping;
            else
                stats.averagePing = stats.averagePing * 0.7f + ping * 0.3f; // Weighted average
        }
    }
}