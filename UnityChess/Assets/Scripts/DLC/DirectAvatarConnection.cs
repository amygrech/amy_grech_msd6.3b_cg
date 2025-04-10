using DLC;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// This script provides a direct connection between the DLC store purchases
// and the HostImage/ClientImage objects in the scene
public class DirectAvatarConnection : MonoBehaviour
{
    // Singleton pattern
    public static DirectAvatarConnection Instance;
    
    [Header("UI Image References")]
    [SerializeField] private Image hostImage;  // Reference to HostImage in hierarchy
    [SerializeField] private Image clientImage; // Reference to ClientImage in hierarchy
    
    private void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // Try to find the images if not assigned
        if (hostImage == null)
        {
            GameObject hostObj = GameObject.Find("HostImage");
            if (hostObj != null)
            {
                hostImage = hostObj.GetComponent<Image>();
                Debug.Log("Found HostImage: " + (hostImage != null ? "Success" : "Failed"));
            }
        }
        
        if (clientImage == null)
        {
            GameObject clientObj = GameObject.Find("ClientImage");
            if (clientObj != null)
            {
                clientImage = clientObj.GetComponent<Image>();
                Debug.Log("Found ClientImage: " + (clientImage != null ? "Success" : "Failed"));
            }
        }
    }
    
    // Call this from DLCStoreManager when an avatar is purchased
    public void UpdateAvatar(string avatarPath, Sprite avatarSprite)
    {
        Debug.Log($"DirectAvatarConnection: Updating avatar with path {avatarPath}");
        
        // Determine if we're host or client
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        bool isClient = NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost;
        
        // Update the appropriate image directly
        if (isHost)
        {
            if (hostImage != null)
            {
                Debug.Log("Directly updating HostImage sprite");
                hostImage.sprite = avatarSprite;
            }
            else
            {
                Debug.LogError("Cannot update HostImage - reference is null");
            }
        }
        else if (isClient)
        {
            if (clientImage != null)
            {
                Debug.Log("Directly updating ClientImage sprite");
                clientImage.sprite = avatarSprite;
            }
            else
            {
                Debug.LogError("Cannot update ClientImage - reference is null");
            }
        }
        
        // Also notify the NetworkAvatarManager to sync the change across the network
        NetworkAvatarManager networkManager = FindObjectOfType<NetworkAvatarManager>();
        if (networkManager != null)
        {
            networkManager.UpdatePlayerAvatar(avatarPath);
        }
    }
    
    // Call this from AvatarLoader when an avatar is loaded from Firebase
    public void UpdateAvatarFromPath(string avatarPath, Sprite avatarSprite)
    {
        Debug.Log($"DirectAvatarConnection: Updating avatar from path {avatarPath}");
        UpdateAvatar(avatarPath, avatarSprite);
    }
    
    // For when you have a raw texture instead of a sprite
    public void UpdateAvatarFromTexture(string avatarPath, Texture2D texture)
    {
        if (texture == null)
        {
            Debug.LogError("Cannot update avatar - texture is null");
            return;
        }
        
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            Vector2.one * 0.5f
        );
        
        UpdateAvatar(avatarPath, sprite);
    }
}