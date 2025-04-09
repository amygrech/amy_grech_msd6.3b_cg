using System.Collections.Generic;
using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;
using System.Linq;

[FirestoreData]
public class AvatarData
{
    [FirestoreProperty]
    public string Id { get; set; }
    
    [FirestoreProperty]
    public string DisplayName { get; set; }
    
    [FirestoreProperty]
    public int Price { get; set; }
    
    [FirestoreProperty]
    public string StoragePath { get; set; }
}

public class AvatarFirestoreManager : MonoBehaviour
{
    // Singleton pattern
    public static AvatarFirestoreManager Instance { get; private set; }
    
    private FirebaseFirestore db;
    private CollectionReference avatarsCollection;
    
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
    }
    
    private void Start()
    {
        // Initialize Firestore
        db = FirebaseFirestore.DefaultInstance;
        avatarsCollection = db.Collection("StoreItems").Document("shell").Collection("avatars");
    }
    
    // Fetch all available avatars from Firestore
    public void GetAllAvatars(System.Action<List<AvatarData>> callback)
    {
        avatarsCollection.GetSnapshotAsync().ContinueWithOnMainThread(task => {
            if (task.IsFaulted)
            {
                Debug.LogError($"Error fetching avatars: {task.Exception}");
                callback?.Invoke(new List<AvatarData>());
                return;
            }
            
            QuerySnapshot snapshot = task.Result;
            List<AvatarData> avatars = new List<AvatarData>();
            
            foreach (DocumentSnapshot document in snapshot.Documents)
            {
                AvatarData avatar = document.ConvertTo<AvatarData>();
                avatar.Id = document.Id;
                avatars.Add(avatar);
            }
            
            callback?.Invoke(avatars);
        });
    }
    
    // Initialize store with avatars from Firestore
    public void InitializeStore(DLCStoreManager storeManager)
    {
        GetAllAvatars(avatars => {
            if (avatars.Count > 0)
            {
                // Sort avatars by price
                var sortedAvatars = avatars.OrderBy(a => a.Price).ToList();
                
                for (int i = 0; i < Mathf.Min(sortedAvatars.Count, storeManager.avatarCards.Length); i++)
                {
                    var avatar = sortedAvatars[i];
                    var card = storeManager.avatarCards[i];
                    
                    // Call the public Initialize method on the card
                    card.Initialize(
                        avatar.DisplayName,
                        avatar.Price,
                        avatar.StoragePath,
                        storeManager.OnPurchaseAvatar
                    );
                    
                    // Load preview
                    AvatarLoader.Instance.LoadAvatar(avatar.StoragePath, card.previewImage);
                }
                
                Debug.Log($"Initialized store with {avatars.Count} avatars from Firestore");
            }
            else
            {
                Debug.LogWarning("No avatars found in Firestore, using fallback data");
                // Instead of calling private method, call public method if available
                // or use direct initialization if needed
                InitializeCardsFallback(storeManager);
            }
        });
    }
    
    // Fallback initialization method when Firestore data is not available
    private void InitializeCardsFallback(DLCStoreManager storeManager)
    {
        // Example fallback initialization
        if (storeManager.avatarCards.Length >= 2)
        {
            // First avatar card setup
            storeManager.avatarCards[0].Initialize(
                "Turtle Avatar", 
                300, 
                "assets/turtle.jpg",
                storeManager.OnPurchaseAvatar
            );

            // Second avatar card setup
            storeManager.avatarCards[1].Initialize(
                "Shell Avatar", 
                500, 
                "assets/shell.jpg",
                storeManager.OnPurchaseAvatar
            );
            
            // Load preview images
            foreach (var card in storeManager.avatarCards)
            {
                AvatarLoader.Instance.LoadAvatar(card.avatarPath, card.previewImage);
            }
        }
    }
    
    // Track purchases in Firestore
    public void SavePurchase(string userId, string avatarId)
    {
        Dictionary<string, object> purchase = new Dictionary<string, object>
        {
            { "userId", userId },
            { "avatarId", avatarId },
            { "purchaseDate", FieldValue.ServerTimestamp }
        };
        
        db.Collection("purchases").AddAsync(purchase).ContinueWithOnMainThread(task => {
            if (task.IsFaulted)
            {
                Debug.LogError($"Error saving purchase: {task.Exception}");
            }
            else
            {
                Debug.Log("Purchase saved to Firestore");
            }
        });
    }
    
    // Get user's purchased avatars
    public void GetUserPurchases(string userId, System.Action<List<string>> callback)
    {
        db.Collection("purchases")
            .WhereEqualTo("userId", userId)
            .GetSnapshotAsync()
            .ContinueWithOnMainThread(task => {
                if (task.IsFaulted)
                {
                    Debug.LogError($"Error fetching purchases: {task.Exception}");
                    callback?.Invoke(new List<string>());
                    return;
                }
                
                QuerySnapshot snapshot = task.Result;
                List<string> purchasedAvatarIds = new List<string>();
                
                foreach (DocumentSnapshot document in snapshot.Documents)
                {
                    if (document.TryGetValue<string>("avatarId", out string avatarId))
                    {
                        purchasedAvatarIds.Add(avatarId);
                    }
                }
                
                callback?.Invoke(purchasedAvatarIds);
            });
    }
}