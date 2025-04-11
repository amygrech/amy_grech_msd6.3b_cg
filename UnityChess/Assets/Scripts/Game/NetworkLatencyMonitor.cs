using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class NetworkLatencyMonitor : NetworkBehaviour
{
    [Header("Configuration")]
    [SerializeField] private float pingInterval = 2.0f; // How often to send ping requests (seconds)
    [SerializeField] private bool detailedLogging = true; // If true, logs min/avg/max ping stats

    // Track ping data for each client
    private Dictionary<ulong, PingData> clientPingData = new Dictionary<ulong, PingData>();
    
    // Struct to store ping information for each client
    private struct PingData
    {
        public float lastPingTime;
        public float currentPing;
        public float minPing;
        public float maxPing;
        public float avgPing;
        public int pingCount;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Initialize ping data for the local client
        if (IsClient)
        {
            clientPingData[NetworkManager.Singleton.LocalClientId] = new PingData
            {
                lastPingTime = 0f,
                currentPing = 0f,
                minPing = float.MaxValue,
                maxPing = 0f,
                avgPing = 0f,
                pingCount = 0
            };
            
            Debug.Log("Network Latency Monitor: Started for client " + NetworkManager.Singleton.LocalClientId);
        }

        // Start the ping measurement coroutine if we are the server
        if (IsServer)
        {
            StartCoroutine(PingClients());
            Debug.Log("Network Latency Monitor: Server started pinging clients");
        }
    }

    // Server-side coroutine to periodically send ping requests to all clients
    private IEnumerator PingClients()
    {
        while (NetworkManager.Singleton.IsListening)
        {
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                // Skip pinging ourselves if we're host
                if (clientId == NetworkManager.Singleton.LocalClientId && NetworkManager.Singleton.IsHost)
                    continue;

                SendPingRequestClientRpc(clientId, NetworkManager.Singleton.ServerTime.Time);
            }

            yield return new WaitForSeconds(pingInterval);
        }
    }

    // Server sends ping request to clients with current server time
    [ClientRpc]
    private void SendPingRequestClientRpc(ulong targetClientId, double serverTime)
    {
        // Only respond if we are the target client
        if (targetClientId == NetworkManager.Singleton.LocalClientId)
        {
            RespondToPingServerRpc(targetClientId, serverTime);
        }
    }

    // Client responds to ping request
    [ServerRpc(RequireOwnership = false)]
    private void RespondToPingServerRpc(ulong clientId, double sentTime)
    {
        // Calculate round-trip time
        double currentTime = NetworkManager.Singleton.ServerTime.Time;
        double roundTripTime = (currentTime - sentTime) * 1000; // Convert to milliseconds

        // Notify client of its ping
        NotifyPingClientRpc(clientId, roundTripTime);

        // Log ping on server
        Debug.Log($"[SERVER] Client {clientId} ping: {roundTripTime:F2}ms");
    }

    // Server notifies client of its ping
    [ClientRpc]
    private void NotifyPingClientRpc(ulong targetClientId, double pingTime)
    {
        // Only process if we are the target client
        if (targetClientId == NetworkManager.Singleton.LocalClientId)
        {
            float ping = (float)pingTime;

            // Update ping data
            PingData data = clientPingData[NetworkManager.Singleton.LocalClientId];
            data.currentPing = ping;
            data.minPing = Mathf.Min(data.minPing, ping);
            data.maxPing = Mathf.Max(data.maxPing, ping);
            data.pingCount++;
            data.avgPing = ((data.avgPing * (data.pingCount - 1)) + ping) / data.pingCount;
            clientPingData[NetworkManager.Singleton.LocalClientId] = data;

            // Log to console
            if (detailedLogging)
            {
                Debug.Log($"[CLIENT] Ping: {ping:F2}ms (Min: {data.minPing:F2}ms, Avg: {data.avgPing:F2}ms, Max: {data.maxPing:F2}ms)");
            }
            else
            {
                Debug.Log($"[CLIENT] Ping: {ping:F2}ms");
            }
        }
    }

    // Public method to get current ping data for other systems
    public float GetCurrentPing()
    {
        if (clientPingData.TryGetValue(NetworkManager.Singleton.LocalClientId, out PingData data))
        {
            return data.currentPing;
        }
        return 0f;
    }

    // Public method to get average ping for other systems
    public float GetAveragePing()
    {
        if (clientPingData.TryGetValue(NetworkManager.Singleton.LocalClientId, out PingData data))
        {
            return data.avgPing;
        }
        return 0f;
    }
}