using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Analytics;
using Firebase.Storage;
using Firebase.Database;
using Firebase.Extensions;

public class FirebaseManager : MonoBehaviour
{
    // Singleton pattern
    public static FirebaseManager Instance { get; private set; }
    
    // Firebase references
    public FirebaseStorage Storage { get; private set; }
    public StorageReference StorageRoot { get; private set; }
    public DatabaseReference Database { get; private set; }
    
    // Firebase initialization status
    public bool IsInitialized { get; private set; }
    
    // Events
    public event Action OnFirebaseInitialized;
    
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
        
        // Initialize Firebase
        InitializeFirebase();
    }
    
    private void InitializeFirebase()
    {
        Debug.Log("Initializing Firebase...");
        
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
            DependencyStatus dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available)
            {
                // Initialize Firebase components
                InitializeFirebaseComponents();
            }
            else
            {
                Debug.LogError($"Could not resolve all Firebase dependencies: {dependencyStatus}");
            }
        });
    }
    
    private void InitializeFirebaseComponents()
    {
        try
        {
            // Initialize Firebase with the options from the config file
            FirebaseApp app = FirebaseApp.DefaultInstance;
            if (app == null)
            {
                var options = new AppOptions
                {
                    DatabaseUrl = new Uri("https://dlcstore-8ccb3.firebasedatabase.app")
                };
                app = FirebaseApp.Create(options);
            }
        
            // Initialize Analytics
            FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
        
            // Initialize Storage
            Storage = FirebaseStorage.DefaultInstance;
            StorageRoot = Storage.GetReferenceFromUrl("gs://dlcstore-8ccb3.appspot.com");
        
            // Initialize Database
            Database = FirebaseDatabase.DefaultInstance.RootReference;
        
            IsInitialized = true;
            Debug.Log("Firebase initialized successfully");
        
            // Trigger event
            OnFirebaseInitialized?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"Firebase initialization error: {e.Message}");
            Debug.LogException(e);
        }
    }
    
    #region Analytics Methods
    
    public void LogPurchaseEvent(string itemId, string itemName, double price)
    {
        if (!IsInitialized) return;
        
        Debug.Log($"Logging purchase: {itemName} for {price}");
        
        // Create parameter list - using string constants instead of FirebaseAnalytics constants
        Parameter[] parameters = {
            new Parameter("item_id", itemId),
            new Parameter("item_name", itemName),
            new Parameter("price", price),
            new Parameter("currency", "credits")
        };
        
        // Log event - using string constant instead of FirebaseAnalytics constant
        FirebaseAnalytics.LogEvent("purchase", parameters);
    }
    
    public void LogGameStartEvent(string matchId)
    {
        if (!IsInitialized) return;
        
        Debug.Log($"Logging game start: {matchId}");
        
        Parameter[] parameters = {
            new Parameter("match_id", matchId)
        };
        
        FirebaseAnalytics.LogEvent("game_start", parameters);
    }
    
    public void LogGameEndEvent(string matchId, string result, int moveCount, double duration)
    {
        if (!IsInitialized) return;
        
        Debug.Log($"Logging game end: {matchId}, result: {result}");
        
        Parameter[] parameters = {
            new Parameter("match_id", matchId),
            new Parameter("result", result),
            new Parameter("move_count", moveCount),
            new Parameter("duration_seconds", duration)
        };
        
        FirebaseAnalytics.LogEvent("game_end", parameters);
    }
    
    #endregion
    
    #region Database Methods
    
    public void SaveUserData(string userId, Dictionary<string, object> userData)
    {
        if (!IsInitialized || Database == null)
        {
            Debug.LogError("Firebase Database not initialized!");
            return;
        }
        
        Database.Child("users").Child(userId).UpdateChildrenAsync(userData)
            .ContinueWithOnMainThread(task => {
                if (task.IsFaulted)
                {
                    Debug.LogError($"Error saving user data: {task.Exception}");
                }
                else
                {
                    Debug.Log("User data saved successfully");
                }
            });
    }
    
    public void GetUserData(string userId, Action<Dictionary<string, object>> callback)
    {
        if (!IsInitialized || Database == null)
        {
            Debug.LogError("Firebase Database not initialized!");
            callback?.Invoke(null);
            return;
        }
        
        Database.Child("users").Child(userId).GetValueAsync()
            .ContinueWithOnMainThread(task => {
                if (task.IsFaulted)
                {
                    Debug.LogError($"Error getting user data: {task.Exception}");
                    callback?.Invoke(null);
                }
                else if (task.IsCompleted)
                {
                    DataSnapshot snapshot = task.Result;
                    Dictionary<string, object> userData = new Dictionary<string, object>();
                    
                    if (snapshot.Exists)
                    {
                        foreach (DataSnapshot child in snapshot.Children)
                        {
                            userData[child.Key] = child.Value;
                        }
                    }
                    
                    callback?.Invoke(userData);
                }
            });
    }
    
    public void SaveGameState(string matchId, string gameState, Action<bool> callback = null)
    {
        if (!IsInitialized || Database == null)
        {
            Debug.LogError("Firebase Database not initialized!");
            callback?.Invoke(false);
            return;
        }
        
        Dictionary<string, object> gameData = new Dictionary<string, object>
        {
            { "state", gameState },
            { "timestamp", ServerValue.Timestamp }
        };
        
        Database.Child("games").Child(matchId).UpdateChildrenAsync(gameData)
            .ContinueWithOnMainThread(task => {
                if (task.IsFaulted)
                {
                    Debug.LogError($"Error saving game state: {task.Exception}");
                    callback?.Invoke(false);
                }
                else
                {
                    Debug.Log("Game state saved successfully");
                    callback?.Invoke(true);
                }
            });
    }
    
    public void LoadGameState(string matchId, Action<string> callback)
    {
        if (!IsInitialized || Database == null)
        {
            Debug.LogError("Firebase Database not initialized!");
            callback?.Invoke(null);
            return;
        }
        
        Database.Child("games").Child(matchId).Child("state").GetValueAsync()
            .ContinueWithOnMainThread(task => {
                if (task.IsFaulted)
                {
                    Debug.LogError($"Error loading game state: {task.Exception}");
                    callback?.Invoke(null);
                }
                else if (task.IsCompleted)
                {
                    DataSnapshot snapshot = task.Result;
                    if (snapshot.Exists)
                    {
                        string gameState = snapshot.Value.ToString();
                        callback?.Invoke(gameState);
                    }
                    else
                    {
                        Debug.Log("No saved game state found");
                        callback?.Invoke(null);
                    }
                }
            });
    }
    
    #endregion
    
    #region Storage Methods
    
    public void DownloadFile(string path, Action<byte[]> onComplete, Action<Exception> onError = null)
    {
        if (!IsInitialized || StorageRoot == null)
        {
            Debug.LogError("Firebase Storage not initialized!");
            onError?.Invoke(new Exception("Firebase Storage not initialized"));
            return;
        }
        
        StorageReference fileRef = StorageRoot.Child(path);
        const long maxAllowedSize = 5 * 1024 * 1024; // 5MB max size
        
        fileRef.GetBytesAsync(maxAllowedSize).ContinueWithOnMainThread(task => {
            if (task.IsFaulted)
            {
                Debug.LogError($"Download failed: {task.Exception}");
                onError?.Invoke(task.Exception);
            }
            else if (task.IsCompleted)
            {
                Debug.Log($"Downloaded {path} successfully");
                onComplete?.Invoke(task.Result);
            }
        });
    }
    
    public IEnumerator DownloadTexture(string path, Action<Texture2D> onComplete, Action<string> onError = null)
    {
        if (!IsInitialized || StorageRoot == null)
        {
            Debug.LogError("Firebase Storage not initialized!");
            onError?.Invoke("Firebase Storage not initialized");
            yield break;
        }
        
        StorageReference fileRef = StorageRoot.Child(path);
        
        // Get download URL
        var urlTask = fileRef.GetDownloadUrlAsync();
        yield return new WaitUntil(() => urlTask.IsCompleted || urlTask.IsFaulted);
        
        if (urlTask.IsFaulted)
        {
            Debug.LogError($"Failed to get download URL: {urlTask.Exception}");
            onError?.Invoke($"Failed to get download URL: {urlTask.Exception.Message}");
            yield break;
        }
        
        string downloadUrl = urlTask.Result.ToString();
        
        // Download the texture
        using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(downloadUrl))
        {
            yield return request.SendWebRequest();
            
            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Texture2D texture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);
                onComplete?.Invoke(texture);
            }
            else
            {
                Debug.LogError($"Failed to download texture: {request.error}");
                onError?.Invoke(request.error);
            }
        }
    }
    
    #endregion
}