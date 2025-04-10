using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

namespace DLC
{
    public class NetworkAvatarManager : NetworkBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image m_HostImage;
        [SerializeField] private Image m_ClientImage;

        // Network variable to sync the avatar path
        private NetworkVariable<NetworkString> m_HostAvatarPath = new NetworkVariable<NetworkString>(
            new NetworkString(""), 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );

        private NetworkVariable<NetworkString> m_ClientAvatarPath = new NetworkVariable<NetworkString>(
            new NetworkString(""), 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Owner
        );

        private void Start()
        {
            // Find references if not assigned
            if (m_HostImage == null)
            {
                GameObject hostObj = GameObject.Find("HostImage");
                if (hostObj != null)
                {
                    m_HostImage = hostObj.GetComponent<Image>();
                    Debug.Log("Found HostImage: " + (m_HostImage != null ? "Success" : "Failed"));
                }
            }
            
            if (m_ClientImage == null)
            {
                GameObject clientObj = GameObject.Find("ClientImage");
                if (clientObj != null)
                {
                    m_ClientImage = clientObj.GetComponent<Image>();
                    Debug.Log("Found ClientImage: " + (m_ClientImage != null ? "Success" : "Failed"));
                }
            }

            // Subscribe to avatar path change events
            m_HostAvatarPath.OnValueChanged += OnHostAvatarPathChanged;
            m_ClientAvatarPath.OnValueChanged += OnClientAvatarPathChanged;

            Debug.Log($"NetworkAvatarManager started. IsHost: {IsHost}, IsClient: {IsClient}, IsOwner: {IsOwner}");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // If we're the local player, initialize with our current avatar
            if (DLCStoreManager.Instance != null && !string.IsNullOrEmpty(DLCStoreManager.Instance.currentAvatarURL))
            {
                if (IsHost)
                {
                    // If host, directly set the host avatar path
                    Debug.Log($"Setting initial host avatar path: {DLCStoreManager.Instance.currentAvatarURL}");
                    m_HostAvatarPath.Value = new NetworkString(DLCStoreManager.Instance.currentAvatarURL);
                }
                else if (IsClient && !IsHost)
                {
                    // If client, use RPC to set the client avatar path
                    Debug.Log($"Setting initial client avatar path: {DLCStoreManager.Instance.currentAvatarURL}");
                    SetClientAvatarServerRpc(DLCStoreManager.Instance.currentAvatarURL);
                }
            }
        }

        private void OnHostAvatarPathChanged(NetworkString previousValue, NetworkString newValue)
        {
            if (string.IsNullOrEmpty(newValue.Value))
                return;

            Debug.Log($"Host avatar path changed to: {newValue.Value}");
            // Load the new avatar using AvatarLoader
            if (AvatarLoader.Instance != null && m_HostImage != null)
            {
                AvatarLoader.Instance.LoadAvatar(newValue.Value, m_HostImage, 
                    () => Debug.Log("Host avatar loaded successfully"), 
                    (ex) => Debug.LogError($"Error loading host avatar: {ex.Message}"));
            }
            else
            {
                Debug.LogWarning("Cannot load host avatar: AvatarLoader.Instance or m_HostImage is null");
            }
        }

        private void OnClientAvatarPathChanged(NetworkString previousValue, NetworkString newValue)
        {
            if (string.IsNullOrEmpty(newValue.Value))
                return;

            Debug.Log($"Client avatar path changed to: {newValue.Value}");
            // Load the new avatar using AvatarLoader
            if (AvatarLoader.Instance != null && m_ClientImage != null)
            {
                AvatarLoader.Instance.LoadAvatar(newValue.Value, m_ClientImage,
                    () => Debug.Log("Client avatar loaded successfully"),
                    (ex) => Debug.LogError($"Error loading client avatar: {ex.Message}"));
            }
            else
            {
                Debug.LogWarning("Cannot load client avatar: AvatarLoader.Instance or m_ClientImage is null");
            }
        }

        // Called by DLCStoreManager when player changes their avatar
        public void UpdatePlayerAvatar(string newAvatarPath)
        {
            Debug.Log($"UpdatePlayerAvatar called with path: {newAvatarPath}, IsHost: {IsHost}, IsClient: {IsClient}");

            // Only update the avatar that corresponds to the current client role
            if (IsHost)
            {
                Debug.Log($"Host setting avatar: {newAvatarPath}");
                // Host can directly set the host avatar path
                m_HostAvatarPath.Value = new NetworkString(newAvatarPath);
            }
            else if (IsClient && !IsHost)
            {
                Debug.Log($"Client setting avatar: {newAvatarPath}");
                // Client needs to use RPC to set the client avatar path
                SetClientAvatarServerRpc(newAvatarPath);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetClientAvatarServerRpc(string newAvatarPath, ServerRpcParams serverRpcParams = default)
        {
            Debug.Log($"SetClientAvatarServerRpc called with path: {newAvatarPath}");
            
            // Update the client avatar path
            m_ClientAvatarPath.Value = new NetworkString(newAvatarPath);
            
            // Optionally, we can identify which client called this RPC
            ulong clientId = serverRpcParams.Receive.SenderClientId;
            Debug.Log($"Client {clientId} updated their avatar to: {newAvatarPath}");
        }

        // Helper class to use string with NetworkVariable
        public struct NetworkString : INetworkSerializable
        {
            private string value;

            public NetworkString(string value)
            {
                this.value = value;
            }

            public string Value => value;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref value);
            }

            public override string ToString()
            {
                return value;
            }
        }
    }
}