using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

[RequireComponent(typeof(Button))]
public class DirectChessGameConnector : MonoBehaviour
{
    [SerializeField] private InputField joinCodeField;

    private void Awake()
    {
        // Get the button component and attach our click handler
        Button button = GetComponent<Button>();
        button.onClick.AddListener(HandleConnect);
    }

    public void HandleConnect()
    {
        // Get the join code text
        string joinCode = joinCodeField != null ? joinCodeField.text : "";
        
        // Check if we're using the special "chessgame" code
        if (joinCode.ToLower() == "chessgame")
        {
            Debug.Log("DirectChessGameConnector: Connecting with chessgame code...");
            
            // Configure transport directly
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                // Set localhost connection
                transport.ConnectionData.Address = "127.0.0.1";
                transport.ConnectionData.Port = 7777;
                
                // Disable any NetworkObjects directly
                NetworkObject[] networkObjects = FindObjectsOfType<NetworkObject>();
                foreach (NetworkObject netObj in networkObjects)
                {
                    if (netObj.GetComponent<NetworkManager>() == null && netObj.enabled)
                    {
                        netObj.enabled = false;
                    }
                }
                
                // Start client directly
                bool success = NetworkManager.Singleton.StartClient();
                Debug.Log("DirectChessGameConnector: Connection result = " + success);
            }
            else
            {
                Debug.LogError("DirectChessGameConnector: Could not find transport component!");
            }
        }
        else
        {
            // Let the normal system handle non-chessgame codes
            Debug.Log("DirectChessGameConnector: Using normal connection flow");
            
            // Call the public StartClient method in ChessNetworkManager
            ChessNetworkManager.Instance.StartClient();
        }
    }
}
