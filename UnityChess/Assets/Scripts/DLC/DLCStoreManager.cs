using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Storage;
using Firebase.Extensions;
using System;
using System.Threading.Tasks;
using DLC;
using UnityEngine.Networking;

public class DLCStoreManager : MonoBehaviour
{
    // Singleton pattern
    public static DLCStoreManager Instance;

    // UI References
    [Header("UI References")]
    public GameObject dlcStorePanel;
    public TextMeshProUGUI creditsText;
    public Button closeButton;

    // Avatar Card References
    [Header("Avatar Cards")]
    public AvatarCard[] avatarCards;

    // Player Data
    [Header("Player Data")]
    public int playerCredits = 1000;
    public string currentAvatarURL = "";
    public Image playerAvatarImage;

    // Multiplayer references
    [Header("Multiplayer")]
    [SerializeField] private NetworkAvatarManager networkAvatarManager;

    // Firebase Storage
    private FirebaseStorage storage;
    private StorageReference storageReference;

    // Purchased avatars tracking
    private List<string> purchasedAvatars = new List<string>();

    private void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // Initialize Firebase Storage
        try {
            storage = FirebaseStorage.DefaultInstance;
            storageReference = storage.RootReference; // Use RootReference instead of GetReferenceFromUrl
            Debug.Log("Firebase Storage initialized with root reference: " + storageReference.Path);
        }
        catch (System.Exception ex) {
            Debug.LogError("Error initializing Firebase Storage: " + ex.Message);
        }

        // Initialize UI
        UpdateCreditsDisplay();
    
        // Try to find NetworkAvatarManager if not set
        if (networkAvatarManager == null)
        {
            networkAvatarManager = FindObjectOfType<NetworkAvatarManager>();
        }

        // Set up button listeners
        if (closeButton != null)
        {
            Debug.Log("Close button found, connecting click listener");
            closeButton.onClick.AddListener(CloseDLCStore);
        }
        else
        {
            Debug.LogError("Close button reference is missing! Assign it in the Inspector.");
        }

        // Try to use AvatarFirestoreManager if available, otherwise initialize manually
        if (AvatarFirestoreManager.Instance != null)
        {
            AvatarFirestoreManager.Instance.InitializeStore(this);
        }
        else
        {
            // Initialize avatar cards directly
            InitializeAvatarCards();
        }
    }
    
    // Make this method public so it can be called from AvatarFirestoreManager
    public void InitializeAvatarCards()
    {
        // Set up each avatar card with data and event listeners
        if (avatarCards.Length >= 2)
        {
            // First avatar card setup
            avatarCards[0].Initialize(
                "Turtle Avatar", 
                300, 
                "assets/turtle.jpg",
                OnPurchaseAvatar
            );

            // Second avatar card setup
            avatarCards[1].Initialize(
                "Shell Avatar", 
                500, 
                "assets/shell.jpg",
                OnPurchaseAvatar
            );

            // Load preview images from Firebase
            foreach (var card in avatarCards)
            {
                LoadAvatarPreview(card);
            }
        }
    }

    private void LoadAvatarPreview(AvatarCard card)
    {
        Debug.Log($"Loading preview for avatar: {card.avatarName}, path: {card.avatarPath}");
        
        // Use a default preview image for testing if the path starts with "assets/"
        if (card.avatarPath.StartsWith("assets/"))
        {
            // For testing, we can use a default sprite until Firebase is properly configured
            // This allows you to test the UI without Firebase working
            if (Application.isEditor)
            {
                // Load a default texture for testing in editor
                Texture2D defaultTexture = Resources.Load<Texture2D>("DefaultAvatar");
                if (defaultTexture != null)
                {
                    card.previewImage.sprite = Sprite.Create(
                        defaultTexture,
                        new Rect(0, 0, defaultTexture.width, defaultTexture.height),
                        Vector2.one * 0.5f
                    );
                    Debug.Log($"Using default preview for {card.avatarName}");
                    return;
                }
            }
        }

        // If AvatarLoader is available, use it
        if (AvatarLoader.Instance != null)
        {
            Debug.Log($"Using AvatarLoader for {card.avatarName}");
            AvatarLoader.Instance.LoadAvatar(card.avatarPath, card.previewImage);
            return;
        }

        try
        {
            // Otherwise use direct Firebase loading
            // Get reference to the image in Firebase Storage
            StorageReference avatarRef = storageReference.Child(card.avatarPath);
            Debug.Log($"Getting Firebase reference for {card.avatarPath}");

            // Download URL
            avatarRef.GetDownloadUrlAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogError($"Failed to get download URL for {card.avatarName}: {task.Exception}");
                    
                    // Use a solid color as fallback
                    card.previewImage.color = new Color(0.8f, 0.8f, 0.8f);
                }
                else
                {
                    // Got the download URL
                    string downloadUrl = task.Result.ToString();
                    Debug.Log($"Got download URL for {card.avatarName}: {downloadUrl}");
                    StartCoroutine(LoadImageFromURL(downloadUrl, card.previewImage));
                }
            });
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error loading preview for {card.avatarName}: {ex.Message}");
            
            // Use a solid color as fallback
            card.previewImage.color = new Color(0.8f, 0.8f, 0.8f);
        }
    }

    private IEnumerator LoadImageFromURL(string url, Image targetImage)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                targetImage.sprite = Sprite.Create(
                    texture, 
                    new Rect(0, 0, texture.width, texture.height), 
                    Vector2.one * 0.5f
                );
            }
            else
            {
                Debug.LogError("Failed to download image: " + request.error);
            }
        }
    }

    public void OpenDLCStore()
    {
        dlcStorePanel.SetActive(true);
        UpdateCardStates();
    }

    public void CloseDLCStore()
    {
        Debug.Log("CloseDLCStore method called");
    
        if (dlcStorePanel == null)
        {
            Debug.LogError("dlcStorePanel reference is missing! Cannot close panel.");
            return;
        }
    
        dlcStorePanel.SetActive(false);
        Debug.Log("DLC Store Panel should now be hidden");
    }

    private void UpdateCreditsDisplay()
    {
        creditsText.text = $"Credits: {playerCredits}";
    }

    private void UpdateCardStates()
    {
        // Update the purchase button states based on:
        // 1. If player has enough credits
        // 2. If avatar has already been purchased
        foreach (var card in avatarCards)
        {
            bool canPurchase = playerCredits >= card.price && !purchasedAvatars.Contains(card.avatarPath);
            card.SetPurchaseButtonState(canPurchase);
            card.UpdateCardState(purchasedAvatars.Contains(card.avatarPath));
        }
    }

    public void OnPurchaseAvatar(AvatarCard card)
    {
        // Check if player has enough credits
        if (playerCredits >= card.price)
        {
            // Deduct credits
            playerCredits -= card.price;
            UpdateCreditsDisplay();

            // Add to purchased list
            purchasedAvatars.Add(card.avatarPath);

            // Download and set the avatar
            DownloadAndSetAvatar(card.avatarPath);

            // Update UI states
            UpdateCardStates();

            // If using FirebaseManager, log purchase
            if (FirebaseManager.Instance != null && FirebaseManager.Instance.IsInitialized)
            {
                FirebaseManager.Instance.LogPurchaseEvent(card.avatarName, card.avatarName, card.price);
            }

            // If using AvatarFirestoreManager, save purchase
            if (AvatarFirestoreManager.Instance != null)
            {
                // Use a unique player ID in a real implementation
                string playerId = SystemInfo.deviceUniqueIdentifier;
                AvatarFirestoreManager.Instance.SavePurchase(playerId, card.avatarPath);
            }
        }
    }

    private void DownloadAndSetAvatar(string avatarPath)
    {
        Debug.Log($"Downloading and setting avatar: {avatarPath}");
        
        // For testing - use default texture if we're in the editor and it's one of our test avatars
        if (Application.isEditor && avatarPath.StartsWith("assets/"))
        {
            string defaultTextureName = "DefaultAvatar";
            
            // Try to extract a more specific texture name
            if (avatarPath.Contains("turtle"))
                defaultTextureName = "TurtleAvatar";
            else if (avatarPath.Contains("shell"))
                defaultTextureName = "ShellAvatar";
                
            Texture2D defaultTexture = Resources.Load<Texture2D>(defaultTextureName);
            if (defaultTexture != null)
            {
                Sprite sprite = Sprite.Create(
                    defaultTexture,
                    new Rect(0, 0, defaultTexture.width, defaultTexture.height),
                    Vector2.one * 0.5f
                );
                
                // Set to player's avatar image
                playerAvatarImage.sprite = sprite;
                
                // Also try to set it directly on the appropriate game object
                if (DirectAvatarConnection.Instance != null)
                {
                    DirectAvatarConnection.Instance.UpdateAvatar(avatarPath, sprite);
                }
                
                currentAvatarURL = avatarPath;
                
                // Notify other players about the avatar change
                NotifyAvatarChange(avatarPath);
                
                Debug.Log($"Using default texture for {avatarPath}");
                return;
            }
        }
        
        // If AvatarLoader is available, use it
        if (AvatarLoader.Instance != null)
        {
            Debug.Log($"Using AvatarLoader for avatar: {avatarPath}");
            AvatarLoader.Instance.LoadAvatar(avatarPath, playerAvatarImage);
            currentAvatarURL = avatarPath;
            
            // Notify other players about the avatar change via NetworkAvatarManager
            NotifyAvatarChange(avatarPath);
            
            return;
        }

        try
        {
            // Otherwise use direct Firebase loading
            // Get reference to the avatar in Firebase Storage
            StorageReference avatarRef = storageReference.Child(avatarPath);
            Debug.Log($"Getting Firebase reference for avatar: {avatarPath}");

            // Download URL
            avatarRef.GetDownloadUrlAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogError($"Failed to get download URL for avatar: {task.Exception}");
                    
                    // Still notify the network about the avatar change
                    // This allows the system to work with path references even if actual download fails
                    currentAvatarURL = avatarPath;
                    NotifyAvatarChange(avatarPath);
                }
                else
                {
                    // Got the download URL
                    string downloadUrl = task.Result.ToString();
                    Debug.Log($"Got download URL for avatar: {downloadUrl}");
                    currentAvatarURL = downloadUrl;
                    
                    // Download and set the avatar image
                    StartCoroutine(LoadImageFromURL(downloadUrl, playerAvatarImage));
                    
                    // Notify other players about the avatar change
                    NotifyAvatarChange(avatarPath);
                }
            });
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error downloading avatar: {ex.Message}");
            
            // Still update the current avatar URL and notify network
            currentAvatarURL = avatarPath;
            NotifyAvatarChange(avatarPath);
        }
    }

    private void NotifyAvatarChange(string avatarPath)
    {
        // Use NetworkAvatarManager to notify other players about the avatar change
        if (networkAvatarManager != null)
        {
            Debug.Log("Notifying other players about avatar change: " + avatarPath);
            networkAvatarManager.UpdatePlayerAvatar(avatarPath);
        }
        else
        {
            Debug.LogWarning("NetworkAvatarManager not found! Avatar change won't be synchronized.");
            
            // Try to find it if not assigned
            networkAvatarManager = FindObjectOfType<NetworkAvatarManager>();
            if (networkAvatarManager != null)
            {
                networkAvatarManager.UpdatePlayerAvatar(avatarPath);
            }
        }
    }
    
    private void OnEnable()
    {
        // Add event listener when the component is enabled
        if (closeButton != null)
        {
            // Remove any existing listeners first to avoid duplicates
            closeButton.onClick.RemoveListener(CloseDLCStore);
            closeButton.onClick.AddListener(CloseDLCStore);
        }
    }

    private void OnDisable()
    {
        // Remove event listener when the component is disabled
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(CloseDLCStore);
        }
    }
}