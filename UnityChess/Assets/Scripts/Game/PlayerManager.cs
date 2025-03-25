using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class PlayerManager : MonoBehaviour {
    [SerializeField] private Button joinButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private Button rejoinButton;

    private void Start() {
        joinButton.onClick.AddListener(JoinSession);
        leaveButton.onClick.AddListener(LeaveSession);
        rejoinButton.onClick.AddListener(RejoinSession);
    }

    private void JoinSession() {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer) {
            NetworkManager.Singleton.StartClient();
        }
    }

    private void LeaveSession() {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient) {
            NetworkManager.Singleton.Shutdown();
        }
    }

    private void RejoinSession() {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer) {
            NetworkManager.Singleton.StartClient();
        }
    }
}