using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles the initial setup and configuration of Unity Netcode for GameObjects.
/// Should be placed in a persistent scene or on a manager GameObject.
/// </summary>
public class NetworkSetup : MonoBehaviour {
    [Header("Network Configuration")]
    [SerializeField] private string ipAddress = "127.0.0.1";
    [SerializeField] private ushort port = 7777;
    [SerializeField] private bool startNetworkOnAwake = false;

    [Header("Prefabs")]
    [SerializeField] private GameObject networkManagerPrefab;

    private void Awake() {
        // Ensure we have a NetworkManager in the scene
        if (NetworkManager.Singleton == null && networkManagerPrefab != null) {
            Instantiate(networkManagerPrefab);
        }

        // Configure the network transport
        if (NetworkManager.Singleton != null) {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null) {
                transport.ConnectionData.Address = ipAddress;
                transport.ConnectionData.Port = port;
                Debug.Log($"Network transport configured: {ipAddress}:{port}");
            }
        }

        // Start the network automatically if configured
        if (startNetworkOnAwake && NetworkManager.Singleton != null) {
            NetworkManager.Singleton.StartHost();
            Debug.Log("Network started automatically in Host mode");
        }
    }

    /// <summary>
    /// Updates the network transport with new connection parameters.
    /// </summary>
    public void UpdateNetworkTransport(string address, ushort newPort) {
        if (NetworkManager.Singleton != null) {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null) {
                transport.ConnectionData.Address = address;
                transport.ConnectionData.Port = newPort;
                Debug.Log($"Network transport updated: {address}:{newPort}");
            }
        }
    }

    /// <summary>
    /// Registers network prefabs that need to be spawned during gameplay.
    /// </summary>
    public void RegisterNetworkPrefabs(GameObject[] prefabs) {
        if (NetworkManager.Singleton != null) {
            foreach (GameObject prefab in prefabs) {
                if (prefab.GetComponent<NetworkObject>() != null) {
                    NetworkManager.Singleton.AddNetworkPrefab(prefab);
                    Debug.Log($"Registered network prefab: {prefab.name}");
                } else {
                    Debug.LogWarning($"Failed to register network prefab: {prefab.name} - NetworkObject component missing");
                }
            }
        }
    }
}