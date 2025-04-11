using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Storage;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine.Analytics;

public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager Instance { get; private set; }

    public FirebaseStorage Storage { get; private set; }
    public StorageReference StorageRoot { get; private set; }
    public DatabaseReference Database { get; private set; }

    public bool IsInitialized { get; private set; }

    public event Action OnFirebaseInitialized;

    private void Awake()
    {
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

        InitializeFirebase();
    }
    
    void Start()
    {
        StartCoroutine(SaveTestGameState());
    }

    private void InitializeFirebase()
    {
        Debug.Log("Initializing Firebase...");

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
            DependencyStatus dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available)
            {
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
            FirebaseApp app = FirebaseApp.DefaultInstance;
            if (app == null)
            {
                var options = new AppOptions
                {
                    DatabaseUrl = new Uri("https://dlcstore-8ccb3.firebasedatabase.app")
                };
                app = FirebaseApp.Create(options);
            }

            // Unity Analytics requires no extra setup here

            Storage = FirebaseStorage.DefaultInstance;
            StorageRoot = Storage.RootReference;

            FirebaseDatabase database = FirebaseDatabase.GetInstance(app, "https://dlcstore-8ccb3-default-rtdb.europe-west1.firebasedatabase.app");
            Database = database.RootReference;
            
            IsInitialized = true;
            Debug.Log("Firebase initialized successfully");

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
        Debug.Log($"[Unity Analytics] Logging purchase: {itemName} for {price}");
        Analytics.CustomEvent("purchase", new Dictionary<string, object>
        {
            {"item_id", itemId},
            {"item_name", itemName},
            {"price", price},
            {"currency", "credits"}
        });
    }

    public void LogGameStartEvent(string matchId)
    {
        Debug.Log($"[Unity Analytics] Logging game start: {matchId}");
        Analytics.CustomEvent("game_start", new Dictionary<string, object>
        {
            {"match_id", matchId}
        });
    }

    public void LogGameEndEvent(string matchId, string result, int moveCount, double duration)
    {
        Debug.Log($"[Unity Analytics] Logging game end: {matchId}, result: {result}");
        Analytics.CustomEvent("game_end", new Dictionary<string, object>
        {
            {"match_id", matchId},
            {"result", result},
            {"move_count", moveCount},
            {"duration_seconds", duration}
        });
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
            {"state", gameState},
            {"timestamp", ServerValue.Timestamp}
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
    
    IEnumerator SaveTestGameState()
    {
        yield return new WaitUntil(() =>
            FirebaseManager.Instance != null && FirebaseManager.Instance.IsInitialized);

        string matchId = System.Guid.NewGuid().ToString();
        string gameState = "e4 e5 Nf3 Nc6";

        FirebaseManager.Instance.SaveGameState(matchId, gameState, success =>
        {
            if (success)
                Debug.Log($"Game state saved under match ID: {matchId}");
            else
                Debug.LogError("Game state save failed.");
        });
    }

    #endregion
} 
