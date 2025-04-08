using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using Unity.Netcode;
using Firebase.Storage;
using Firebase.Extensions;
using Firebase.Auth;
using Firebase.Database;

public class DLCManager : NetworkBehaviour
{
    [Serializable]
    public class DLCSkin
    {
        public string id;
        public string name;
        public string previewImageUrl;
        public string assetUrl;
        public int price;
        public Sprite previewSprite;
        public bool isPurchased;
    }

    public static DLCManager Instance { get; private set; }

    // UI References - can be set via Inspector or code
    private Transform dlcStorePanel;
    private GameObject avatarCardPrefab;
    private Text creditsText;
    private GameObject purchaseConfirmationPanel;
    private Text confirmationText;
    private Button confirmPurchaseButton;
    private Button cancelPurchaseButton;
    
    // Method to initialize UI references from code
    public void InitializeUIReferences(
        Transform storePanel,
        GameObject cardPrefab,
        Text credits,
        GameObject confirmationPanel,
        Text confirmText,
        Button confirmButton,
        Button cancelButton)
    {
        dlcStorePanel = storePanel;
        avatarCardPrefab = cardPrefab;
        creditsText = credits;
        purchaseConfirmationPanel = confirmationPanel;
        confirmationText = confirmText;
        confirmPurchaseButton = confirmButton;
        cancelPurchaseButton = cancelButton;
        
        // Set up purchase confirmation panel
        confirmPurchaseButton.onClick.AddListener(ConfirmPurchase);
        cancelPurchaseButton.onClick.AddListener(() => purchaseConfirmationPanel.SetActive(false));
        purchaseConfirmationPanel.SetActive(false);
    }

    // Firebase references
    private FirebaseStorage storage;
    private DatabaseReference database;
    private FirebaseAuth auth;
    
    // Local player data
    private int playerCredits = 1000; // Default starting credits
    private string playerId;
    
    // DLC data
    private List<DLCSkin> availableSkins = new List<DLCSkin>();
    private DLCSkin selectedSkin;
    
    // Cache directory for downloaded assets
    private string cachePath;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Set up cache path
        cachePath = Path.Combine(Application.persistentDataPath, "DLCCache");
        Directory.CreateDirectory(cachePath);
    }

    private void Start()
    {
        InitializeFirebase();
        
        // Check if UI references are already set via inspector
        if (dlcStorePanel != null && confirmPurchaseButton != null && cancelPurchaseButton != null)
        {
            // Set up purchase confirmation panel
            confirmPurchaseButton.onClick.AddListener(ConfirmPurchase);
            cancelPurchaseButton.onClick.AddListener(() => purchaseConfirmationPanel.SetActive(false));
            purchaseConfirmationPanel.SetActive(false);
        }
    }

    private void InitializeFirebase()
    {
        // Initialize Firebase components
        auth = FirebaseAuth.DefaultInstance;
        storage = FirebaseStorage.DefaultInstance;
        database = FirebaseDatabase.DefaultInstance.RootReference;
        
        // Anonymous sign-in for Firebase
        auth.SignInAnonymouslyAsync().ContinueWithOnMainThread(task => {
            if (task.IsCompleted && !task.IsFaulted && !task.IsCanceled)
            {
                playerId = auth.CurrentUser.UserId;
                Debug.Log("Firebase authenticated: " + playerId);
                
                // Load player data and available skins
                LoadPlayerData();
                LoadAvailableSkins();
            }
            else
            {
                Debug.LogError("Firebase authentication failed: " + task.Exception);
            }
        });
    }

    private void LoadPlayerData()
    {
        // Load player credits and purchased DLCs from Firebase
        database.Child("players").Child(playerId).GetValueAsync().ContinueWithOnMainThread(task => {
            if (task.IsCompleted && !task.IsFaulted && !task.IsCanceled)
            {
                DataSnapshot snapshot = task.Result;
                
                if (snapshot.Exists)
                {
                    // Load credits
                    if (snapshot.Child("credits").Exists)
                    {
                        playerCredits = int.Parse(snapshot.Child("credits").Value.ToString());
                    }
                    
                    // Load purchased skins
                    if (snapshot.Child("purchasedSkins").Exists)
                    {
                        foreach (DataSnapshot skinSnapshot in snapshot.Child("purchasedSkins").Children)
                        {
                            string skinId = skinSnapshot.Key;
                            
                            // Mark this skin as purchased
                            DLCSkin skin = availableSkins.Find(s => s.id == skinId);
                            if (skin != null)
                            {
                                skin.isPurchased = true;
                            }
                        }
                    }
                }
                else
                {
                    // Create new player profile
                    SavePlayerData();
                }
                
                // Update UI
                UpdateCreditsDisplay();
            }
        });
    }

    private void LoadAvailableSkins()
    {
        // Get list of available skins from Firebase
        database.Child("skins").GetValueAsync().ContinueWithOnMainThread(task => {
            if (task.IsCompleted && !task.IsFaulted && !task.IsCanceled)
            {
                DataSnapshot snapshot = task.Result;
                
                if (snapshot.Exists)
                {
                    availableSkins.Clear();
                    
                    foreach (DataSnapshot skinSnapshot in snapshot.Children)
                    {
                        DLCSkin skin = new DLCSkin
                        {
                            id = skinSnapshot.Key,
                            name = skinSnapshot.Child("name").Value.ToString(),
                            previewImageUrl = skinSnapshot.Child("previewUrl").Value.ToString(),
                            assetUrl = skinSnapshot.Child("assetUrl").Value.ToString(),
                            price = int.Parse(skinSnapshot.Child("price").Value.ToString()),
                            isPurchased = false
                        };
                        
                        availableSkins.Add(skin);
                    }
                    
                    // Update purchased status of skins
                    LoadPlayerData();
                    
                    // Create UI cards for available skins
                    PopulateDLCStore();
                }
            }
        });
    }

    private void PopulateDLCStore()
    {
        // Clear existing cards
        foreach (Transform child in dlcStorePanel)
        {
            Destroy(child.gameObject);
        }
        
        // Create cards for each skin
        foreach (DLCSkin skin in availableSkins)
        {
            GameObject cardObject = Instantiate(avatarCardPrefab, dlcStorePanel);
            DLCCardUI cardUI = cardObject.GetComponent<DLCCardUI>();
            
            // Set card data
            cardUI.Initialize(skin, OnPurchaseButtonClicked, OnPreviewImageLoaded);
            
            // Start downloading the preview image
            StartCoroutine(DownloadPreviewImage(skin));
        }
    }

    private IEnumerator DownloadPreviewImage(DLCSkin skin)
    {
        if (string.IsNullOrEmpty(skin.previewImageUrl))
            yield break;
            
        // Check if we already have the image cached
        string previewImagePath = Path.Combine(cachePath, skin.id + "_preview.jpg");
        
        if (File.Exists(previewImagePath))
        {
            // Load from cache
            byte[] imageData = File.ReadAllBytes(previewImagePath);
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(imageData);
            skin.previewSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f);
            OnPreviewImageLoaded(skin);
            yield break;
        }
        
        // Download from Firebase Storage
        StorageReference imageRef = storage.GetReferenceFromUrl(skin.previewImageUrl);
        
        imageRef.GetDownloadUrlAsync().ContinueWithOnMainThread(task => {
            if (task.IsCompleted && !task.IsFaulted && !task.IsCanceled)
            {
                string downloadUrl = task.Result.ToString();
                StartCoroutine(DownloadImageCoroutine(downloadUrl, previewImagePath, skin));
            }
        });
    }

    private IEnumerator DownloadImageCoroutine(string url, string savePath, DLCSkin skin)
    {
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            yield return www.SendWebRequest();
            
            if (www.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(www);
                
                // Save to cache
                File.WriteAllBytes(savePath, texture.EncodeToJPG());
                
                // Create sprite and update skin
                skin.previewSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f);
                OnPreviewImageLoaded(skin);
            }
            else
            {
                Debug.LogError("Error downloading image: " + www.error);
            }
        }
    }

    private void OnPreviewImageLoaded(DLCSkin skin)
    {
        // Find the card in the UI and update its image
        foreach (Transform child in dlcStorePanel)
        {
            DLCCardUI cardUI = child.GetComponent<DLCCardUI>();
            if (cardUI != null && cardUI.SkinId == skin.id)
            {
                cardUI.UpdatePreviewImage(skin.previewSprite);
                break;
            }
        }
    }

    private void OnPurchaseButtonClicked(DLCSkin skin)
    {
        selectedSkin = skin;
        
        if (skin.isPurchased)
        {
            // Already purchased, apply the skin
            ApplySkin(skin);
        }
        else
        {
            // Show purchase confirmation
            confirmationText.text = $"Purchase \"{skin.name}\" for {skin.price} credits?";
            purchaseConfirmationPanel.SetActive(true);
        }
    }

    private void ConfirmPurchase()
    {
        if (selectedSkin == null)
            return;
            
        // Check if player has enough credits
        if (playerCredits < selectedSkin.price)
        {
            // Show not enough credits message
            confirmationText.text = "Not enough credits!";
            return;
        }
        
        // Process purchase
        playerCredits -= selectedSkin.price;
        selectedSkin.isPurchased = true;
        
        // Update UI
        UpdateCreditsDisplay();
        purchaseConfirmationPanel.SetActive(false);
        
        // Download the full asset
        StartCoroutine(DownloadSkinAsset(selectedSkin));
        
        // Save purchase to Firebase
        database.Child("players").Child(playerId).Child("purchasedSkins").Child(selectedSkin.id).SetValueAsync(true);
        database.Child("players").Child(playerId).Child("credits").SetValueAsync(playerCredits);
        
        // Log purchase event to Analytics
        Firebase.Analytics.FirebaseAnalytics.LogEvent(
            Firebase.Analytics.FirebaseAnalytics.EventPurchase,
            new Firebase.Analytics.Parameter[] {
                //new Firebase.Analytics.Parameter(Firebase.Analytics.FirebaseAnalytics.ParameterItemId, selectedSkin.id),
                new Firebase.Analytics.Parameter(Firebase.Analytics.FirebaseAnalytics.ParameterPrice, selectedSkin.price)
            }
        );
    }

    private IEnumerator DownloadSkinAsset(DLCSkin skin)
    {
        if (string.IsNullOrEmpty(skin.assetUrl))
            yield break;
            
        // Check if we already have the asset cached
        string assetPath = Path.Combine(cachePath, skin.id + ".bundle");
        
        if (File.Exists(assetPath))
        {
            // Asset already downloaded
            ApplySkin(skin);
            yield break;
        }
        
        // Download from Firebase Storage
        StorageReference assetRef = storage.GetReferenceFromUrl(skin.assetUrl);
        
        assetRef.GetDownloadUrlAsync().ContinueWithOnMainThread(task => {
            if (task.IsCompleted && !task.IsFaulted && !task.IsCanceled)
            {
                string downloadUrl = task.Result.ToString();
                StartCoroutine(DownloadAssetCoroutine(downloadUrl, assetPath, skin));
            }
        });
    }

    private IEnumerator DownloadAssetCoroutine(string url, string savePath, DLCSkin skin)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();
            
            if (www.result == UnityWebRequest.Result.Success)
            {
                // Save to cache
                File.WriteAllBytes(savePath, www.downloadHandler.data);
                
                // Apply the skin
                ApplySkin(skin);
            }
            else
            {
                Debug.LogError("Error downloading asset: " + www.error);
            }
        }
    }

    public void ApplySkin(DLCSkin skin)
    {
        string assetPath = Path.Combine(cachePath, skin.id + ".bundle");
        
        if (!File.Exists(assetPath))
        {
            Debug.LogError("Skin asset not found in cache: " + assetPath);
            return;
        }
        
        // Store the active skin ID
        _activeSkinId = skin.id;
        
        // Load the asset bundle
        StartCoroutine(LoadAssetBundleCoroutine(assetPath, skin.id));
    }

    private IEnumerator LoadAssetBundleCoroutine(string bundlePath, string skinId)
    {
        AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(bundlePath);
        yield return request;
        
        if (request.assetBundle == null)
        {
            Debug.LogError("Failed to load asset bundle from path: " + bundlePath);
            yield break;
        }
        
        AssetBundle bundle = request.assetBundle;
        
        // Load all chess piece sprites from the bundle
        string[] assetNames = bundle.GetAllAssetNames();
        Dictionary<string, Sprite> skinSprites = new Dictionary<string, Sprite>();
        
        foreach (string assetName in assetNames)
        {
            AssetBundleRequest assetRequest = bundle.LoadAssetAsync<Sprite>(assetName);
            yield return assetRequest;
            
            if (assetRequest.asset != null)
            {
                Sprite sprite = assetRequest.asset as Sprite;
                string pieceName = Path.GetFileNameWithoutExtension(assetName);
                skinSprites.Add(pieceName, sprite);
            }
        }
        
        // Apply these sprites to the chess pieces
        ApplySpritesToChessPieces(skinSprites);
        
        // Notify other clients that we've changed our skin
        NotifyOtherClientsOfSkinChange(skinId);
        
        // Clean up
        bundle.Unload(false);
    }

    private void ApplySpritesToChessPieces(Dictionary<string, Sprite> skinSprites)
    {
        // Find all chess pieces and update their sprites
        // This depends on your chess piece implementation
        VisualPiece[] visualPieces = FindObjectsOfType<VisualPiece>();
        
        foreach (VisualPiece piece in visualPieces)
        {
            // Get the piece type and side (e.g., "white_pawn", "black_queen")
            string pieceName = GetPieceName(piece);
            
            if (skinSprites.TryGetValue(pieceName, out Sprite sprite))
            {
                // Update the visual of the piece
                SpriteRenderer renderer = piece.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    renderer.sprite = sprite;
                }
            }
        }
    }

    private string GetPieceName(VisualPiece piece)
    {
        // This method should determine the piece name based on your specific implementation
        // For example: "white_pawn", "black_queen", etc.
        string pieceName = piece.gameObject.name.ToLower();
        
        // Strip any extra information like position or ID numbers
        // This is just an example and should be adjusted to match your naming convention
        if (pieceName.Contains("("))
        {
            pieceName = pieceName.Substring(0, pieceName.IndexOf("(")).Trim();
        }
        
        return pieceName;
    }

    // NetworkManager method to notify other clients about skin change
    private void NotifyOtherClientsOfSkinChange(string skinId)
    {
        if (NetworkManager.Singleton.IsClient && DLCNetworkManager.Instance != null)
        {
            DLCNetworkManager.Instance.SkinActivatedServerRpc(skinId, NetworkManager.Singleton.LocalClientId);
        }
    }

    public void ApplySkinForPlayer(string skinId, ulong clientId)
    {
        // This method is called when another player changes their skin
        ApplySkinForOtherPlayer(skinId, clientId);
    }
    
    // Get the currently active skin ID
    public string GetActiveSkinId()
    {
        // Return the ID of the currently active skin
        return _activeSkinId;
    }
    
    private string _activeSkinId = null;
    
    private void ApplySkinForOtherPlayer(string skinId, ulong clientId)
    {
        // Find the skin in our available skins
        DLCSkin skin = availableSkins.Find(s => s.id == skinId);
        if (skin == null)
            return;
            
        // Download the skin if we don't have it
        string assetPath = Path.Combine(cachePath, skin.id + ".bundle");
        if (!File.Exists(assetPath))
        {
            StartCoroutine(DownloadSkinAsset(skin));
            return;
        }
        
        // Find pieces owned by the specified client
        // This part depends on how you track piece ownership in your multiplayer implementation
        // For example, you might have a NetworkBehaviour component on each piece that indicates its owner
        
        // Example (adjust according to your implementation):
        VisualPiece[] allPieces = FindObjectsOfType<VisualPiece>();
        List<VisualPiece> playerPieces = new List<VisualPiece>();
        
        foreach (VisualPiece piece in allPieces)
        {
            NetworkBehaviour networkBehaviour = piece.GetComponent<NetworkBehaviour>();
            if (networkBehaviour != null && networkBehaviour.OwnerClientId == clientId)
            {
                playerPieces.Add(piece);
            }
        }
        
        // Load and apply the skin just for this player's pieces
        StartCoroutine(LoadAndApplySkinForSpecificPieces(assetPath, playerPieces));
    }

    private IEnumerator LoadAndApplySkinForSpecificPieces(string bundlePath, List<VisualPiece> pieces)
    {
        AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(bundlePath);
        yield return request;
        
        if (request.assetBundle == null)
        {
            Debug.LogError("Failed to load asset bundle for other player from path: " + bundlePath);
            yield break;
        }
        
        AssetBundle bundle = request.assetBundle;
        
        // Load all chess piece sprites from the bundle
        string[] assetNames = bundle.GetAllAssetNames();
        Dictionary<string, Sprite> skinSprites = new Dictionary<string, Sprite>();
        
        foreach (string assetName in assetNames)
        {
            AssetBundleRequest assetRequest = bundle.LoadAssetAsync<Sprite>(assetName);
            yield return assetRequest;
            
            if (assetRequest.asset != null)
            {
                Sprite sprite = assetRequest.asset as Sprite;
                string pieceName = Path.GetFileNameWithoutExtension(assetName);
                skinSprites.Add(pieceName, sprite);
            }
        }
        
        // Apply these sprites only to the pieces we're interested in
        foreach (VisualPiece piece in pieces)
        {
            string pieceName = GetPieceName(piece);
            
            if (skinSprites.TryGetValue(pieceName, out Sprite sprite))
            {
                SpriteRenderer renderer = piece.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    renderer.sprite = sprite;
                }
            }
        }
        
        // Clean up
        bundle.Unload(false);
    }

    private void UpdateCreditsDisplay()
    {
        if (creditsText != null)
        {
            creditsText.text = "Credits: " + playerCredits;
        }
    }

    private void SavePlayerData()
    {
        // Save player data to Firebase
        Dictionary<string, object> playerData = new Dictionary<string, object>
        {
            { "credits", playerCredits }
        };
        
        database.Child("players").Child(playerId).UpdateChildrenAsync(playerData);
    }

    // Add a method to award credits (for testing or gameplay rewards)
    public void AddCredits(int amount)
    {
        playerCredits += amount;
        UpdateCreditsDisplay();
        SavePlayerData();
    }
}