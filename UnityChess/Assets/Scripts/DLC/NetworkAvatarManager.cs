using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class NetworkAvatarManager : NetworkBehaviour
{
    [SerializeField] private Image playerAvatarImage;
    [SerializeField] private Image opponentAvatarImage;

    // Network variable to sync the avatar path
    private NetworkVariable<NetworkString> avatarPath = new NetworkVariable<NetworkString>(
        new NetworkString(""), 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Owner
    );

    // Dictionary to cache downloaded avatars
    private Dictionary<string, Sprite> avatarCache = new Dictionary<string, Sprite>();

    private void Start()
    {
        // Subscribe to avatar path change events
        avatarPath.OnValueChanged += OnAvatarPathChanged;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // If we're the local player, initialize with our current avatar
        if (IsOwner && DLCStoreManager.Instance != null && !string.IsNullOrEmpty(DLCStoreManager.Instance.currentAvatarURL))
        {
            SetAvatarServerRpc(DLCStoreManager.Instance.currentAvatarURL);
        }
    }

    private void OnAvatarPathChanged(NetworkString previousValue, NetworkString newValue)
    {
        if (string.IsNullOrEmpty(newValue.Value))
            return;

        // Load the new avatar
        StartCoroutine(LoadAndSetAvatar(newValue.Value));
    }

    // Called by DLCStoreManager when player changes their avatar
    public void UpdatePlayerAvatar(string newAvatarPath)
    {
        if (IsOwner)
        {
            SetAvatarServerRpc(newAvatarPath);
        }
    }

    [ServerRpc]
    private void SetAvatarServerRpc(string newAvatarPath)
    {
        avatarPath.Value = new NetworkString(newAvatarPath);
    }

    private IEnumerator LoadAndSetAvatar(string avatarUrl)
    {
        // If we already have this avatar cached, use it
        if (avatarCache.ContainsKey(avatarUrl))
        {
            SetAvatarSprite(avatarCache[avatarUrl]);
            yield break;
        }

        // Otherwise download it
        using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(avatarUrl))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Texture2D texture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);
                Sprite sprite = Sprite.Create(
                    texture, 
                    new Rect(0, 0, texture.width, texture.height), 
                    Vector2.one * 0.5f
                );

                // Cache the sprite
                avatarCache[avatarUrl] = sprite;

                // Set the avatar
                SetAvatarSprite(sprite);
            }
            else
            {
                Debug.LogError("Failed to download avatar: " + request.error);
            }
        }
    }

    private void SetAvatarSprite(Sprite sprite)
    {
        // Set the sprite on the appropriate image component
        if (IsOwner)
        {
            if (playerAvatarImage != null)
                playerAvatarImage.sprite = sprite;
        }
        else
        {
            if (opponentAvatarImage != null)
                opponentAvatarImage.sprite = sprite;
        }
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