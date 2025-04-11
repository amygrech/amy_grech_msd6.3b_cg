using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Utility script to easily add network monitoring to your NetworkManager.
/// Attach this to your NetworkManager GameObject.
/// </summary>
public class NetworkMonitorSetup : MonoBehaviour
{
    [Header("Latency Monitoring")]
    [SerializeField] private bool enableLatencyMonitoring = true;
    [SerializeField] private float pingInterval = 2.0f;
    
    [Header("Server Logging")]
    [SerializeField] private bool enableServerLogging = true;
    [SerializeField] private float serverLogInterval = 10.0f;
    
    // References to created components
    private NetworkLatencyMonitor latencyMonitor;
    private ServerLatencyLogger serverLogger;
    
    private void Start()
    {
        NetworkManager networkManager = GetComponent<NetworkManager>();
        
        if (networkManager == null)
        {
            Debug.LogError("NetworkMonitorSetup must be attached to a GameObject with NetworkManager!");
            return;
        }
        
        SetupMonitoring();
        
        // Subscribe to network events
        networkManager.OnServerStarted += OnServerStarted;
        networkManager.OnClientConnectedCallback += OnClientConnected;
    }
    
    private void OnDestroy()
    {
        NetworkManager networkManager = GetComponent<NetworkManager>();
        
        if (networkManager != null)
        {
            networkManager.OnServerStarted -= OnServerStarted;
            networkManager.OnClientConnectedCallback -= OnClientConnected;
        }
    }
    
    public void SetupMonitoring()
    {
        if (enableLatencyMonitoring)
        {
            // Add latency monitor if it doesn't exist
            latencyMonitor = GetComponent<NetworkLatencyMonitor>();
            if (latencyMonitor == null)
            {
                latencyMonitor = gameObject.AddComponent<NetworkLatencyMonitor>();
                // Configure component via SerializedField values if needed
                // This would require modifying NetworkLatencyMonitor to expose those fields
            }
        }
        
        if (enableServerLogging)
        {
            // Add server logger if it doesn't exist
            serverLogger = GetComponent<ServerLatencyLogger>();
            if (serverLogger == null)
            {
                serverLogger = gameObject.AddComponent<ServerLatencyLogger>();
                // Configure component via SerializedField values if needed
            }
        }
        
        Debug.Log("Network monitoring setup complete. Metrics will be logged to Unity console.");
    }
    
    private void OnServerStarted()
    {
        Debug.Log("[SERVER] Server started. Network monitoring active.");
    }
    
    private void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log($"[CLIENT] Connected to server. Network monitoring active.");
        }
    }
    
    // This can be called from other scripts to manually enable/disable monitoring
    public void SetLatencyMonitoringEnabled(bool enabled)
    {
        enableLatencyMonitoring = enabled;
        
        if (latencyMonitor != null)
        {
            latencyMonitor.enabled = enabled;
        }
        else if (enabled)
        {
            SetupMonitoring();
        }
    }
}