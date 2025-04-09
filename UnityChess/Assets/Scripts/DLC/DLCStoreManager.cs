using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Storage;
using Firebase.Extensions;
using System;
using System.Threading.Tasks;
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

    // In DLCStoreManager.cs Start() method
    private void Start()
    {
        // Initialize Firebase Storage
        storage = FirebaseStorage.DefaultInstance;
        storageReference = storage.GetReferenceFromUrl("gs://dlcstore-8ccb3.firebasestorage.app");

        // Initialize UI
        UpdateCreditsDisplay();
    
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
        // If AvatarLoader is available, use it
        if (AvatarLoader.Instance != null)
        {
            AvatarLoader.Instance.LoadAvatar(card.avatarPath, card.previewImage);
            return;
        }

        // Otherwise use direct Firebase loading
        // Get reference to the image in Firebase Storage
        StorageReference avatarRef = storageReference.Child(card.avatarPath);

        // Download URL
        avatarRef.GetDownloadUrlAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError("Failed to get download URL: " + task.Exception);
            }
            else
            {
                // Got the download URL
                string downloadUrl = task.Result.ToString();
                StartCoroutine(LoadImageFromURL(downloadUrl, card.previewImage));
            }
        });
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

            // Notify other players about the avatar change (in multiplayer implementation)
            NotifyAvatarChange(card.avatarPath);

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
        // If AvatarLoader is available, use it
        if (AvatarLoader.Instance != null)
        {
            AvatarLoader.Instance.LoadAvatar(avatarPath, playerAvatarImage);
            currentAvatarURL = avatarPath;
            return;
        }

        // Otherwise use direct Firebase loading
        // Get reference to the avatar in Firebase Storage
        StorageReference avatarRef = storageReference.Child(avatarPath);

        // Download URL
        avatarRef.GetDownloadUrlAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError("Failed to get download URL: " + task.Exception);
            }
            else
            {
                // Got the download URL
                string downloadUrl = task.Result.ToString();
                currentAvatarURL = downloadUrl;
                
                // Download and set the avatar image
                StartCoroutine(LoadImageFromURL(downloadUrl, playerAvatarImage));
            }
        });
    }

    private void NotifyAvatarChange(string avatarPath)
    {
        // This method would use Netcode to notify other players about the avatar change
        // Implementation would depend on your multiplayer setup
        Debug.Log("Notifying other players about avatar change: " + avatarPath);
        
        // If NetworkAvatarManager is available, update the avatar
        NetworkAvatarManager networkAvatarManager = GetComponent<NetworkAvatarManager>();
        if (networkAvatarManager != null)
        {
            networkAvatarManager.UpdatePlayerAvatar(avatarPath);
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