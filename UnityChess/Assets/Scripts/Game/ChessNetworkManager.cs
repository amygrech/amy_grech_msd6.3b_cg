using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

[DefaultExecutionOrder(100)]
public class ChessNetworkManager : MonoBehaviour {
    public static ChessNetworkManager Instance;
        
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    
    private void Awake() {
        if (hostButton != null) hostButton.onClick.AddListener(StartHost);
        if (clientButton != null) clientButton.onClick.AddListener(StartClient);

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
    }

    private void OnDestroy() {
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        }
    }

    public void StartHost() {
        NetworkManager.Singleton.StartHost();
        Debug.Log("Starting as Host (White)...");
    }

    public void StartClient() {
        NetworkManager.Singleton.StartClient();
        Debug.Log("Starting as Client (Black)...");
    }

    private void OnServerStarted() {
        if (NetworkManager.Singleton.IsHost)
            Debug.Log("Host started. You are also a client.");
        else
            Debug.Log("Server started. No local client.");
        if (NetworkManager.Singleton.IsServer) {
            GameManager.Instance.StartNewGame();
            Debug.Log("GameManager: New game started on server.");
        }
    }

    private void OnClientConnected(ulong clientId) {
        if (NetworkManager.Singleton.IsServer)
            Debug.Log($"Server: Client {clientId} connected. Total: {GetConnectedCount()}");
        else
            Debug.Log($"Client {clientId} connected. (Local client? {NetworkManager.Singleton.LocalClientId})");
    }

    private void OnClientDisconnected(ulong clientId) {
        if (NetworkManager.Singleton.IsServer)
            Debug.Log($"Server: Client {clientId} disconnected. Total: {GetConnectedCount()}");
        else
            Debug.Log($"Client {clientId} disconnected.");
    }

    private int GetConnectedCount() {
        return NetworkManager.Singleton.ConnectedClients.Count;
    }
}
