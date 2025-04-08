using System.Collections;
using UnityEngine;
using Firebase;
using Firebase.Extensions;

public class FirebaseInitializer : MonoBehaviour
{
    private static FirebaseInitializer instance;
    public static FirebaseInitializer Instance => instance;
    
    public bool IsInitialized { get; private set; }
    public FirebaseApp App { get; private set; }
    
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        instance = this;
        DontDestroyOnLoad(gameObject);
        
        InitializeFirebase();
    }
    
    private void InitializeFirebase()
    {
        Debug.Log("Initializing Firebase...");
        
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
            DependencyStatus status = task.Result;
            
            if (status == DependencyStatus.Available)
            {
                App = FirebaseApp.DefaultInstance;
                IsInitialized = true;
                Debug.Log("Firebase initialized successfully!");
            }
            else
            {
                Debug.LogError($"Could not resolve Firebase dependencies: {status}");
            }
        });
    }
}