using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkManagerUI : NetworkBehaviour
{
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;

    private void Start()
    {
        Debug.Log("NetworkManagerUI script started.");

        // Verify buttons are assigned
        if (hostButton == null || clientButton == null)
        {
            Debug.LogError("Host or Client button is not assigned in the Inspector.");
            return;
        }

        Debug.Log("Host and Client buttons are assigned correctly.");

        hostButton.onClick.AddListener(() =>
        {
            Debug.Log("Host button clicked. Attempting to start Host...");
            bool success = NetworkManager.Singleton.StartHost();
            Debug.Log(success ? "Host started successfully." : "Failed to start host.");
        });

        clientButton.onClick.AddListener(() =>
        {
            Debug.Log("Client button clicked. Attempting to start Client...");
            bool success = NetworkManager.Singleton.StartClient();
            Debug.Log(success ? "Client started successfully." : "Failed to start client.");
        });
    }
}