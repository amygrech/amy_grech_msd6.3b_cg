using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using UnityChess;

public class NetworkGameStateController : NetworkBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject gameStatePanel;
    [SerializeField] private InputField gameIdInputField;
    [SerializeField] private Button saveGameButton;
    [SerializeField] private Button loadGameButton;
    [SerializeField] private Text statusText;
    [SerializeField] private Button closeButton;
    [SerializeField] private Toggle autoSaveToggle;
    
    [Header("Settings")]
    [SerializeField] private float autoSaveInterval = 60f; // Auto-save every minute
    [SerializeField] private bool enableAutoSave = false;
    
    // References
    private GameManager gameManager;
    private ChessNetworkManager networkManager;
    private FirebaseManager firebaseManager;
    private GameAnalyticsManager analyticsManager;
    
    // Game state tracking
    private string currentGameId;
    private int lastSavedMoveIndex = -1;
    private Coroutine autoSaveCoroutine;
    
    // Network variable to track the current game ID
    private NetworkVariable<NetworkString> networkGameId = new NetworkVariable<NetworkString>(
        new NetworkString(""),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    private void Awake()
    {
        // Get references
        gameManager = FindObjectOfType<GameManager>();
        networkManager = FindObjectOfType<ChessNetworkManager>();
        firebaseManager = FindObjectOfType<FirebaseManager>();
        analyticsManager = FindObjectOfType<GameAnalyticsManager>();
        
        // Set up button listeners
        if (saveGameButton != null)
        {
            saveGameButton.onClick.AddListener(SaveGameState);
        }
        
        if (loadGameButton != null)
        {
            loadGameButton.onClick.AddListener(LoadGameState);
        }
        
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(ClosePanel);
        }
        
        if (autoSaveToggle != null)
        {
            autoSaveToggle.isOn = enableAutoSave;
            autoSaveToggle.onValueChanged.AddListener(OnAutoSaveToggled);
        }
        
        // Hide panel initially
        if (gameStatePanel != null)
        {
            gameStatePanel.SetActive(false);
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Subscribe to network variable changes
        networkGameId.OnValueChanged += OnGameIdChanged;
        
        // Subscribe to game events
        GameManager.MoveExecutedEvent += OnMoveExecuted;
        
        // Start auto-save if enabled and host
        if (IsHost && enableAutoSave)
        {
            StartAutoSave();
        }
        
        // Generate a new game ID if we're the host
        if (IsHost)
        {
            GenerateNewGameId();
        }
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        
        // Unsubscribe from events
        networkGameId.OnValueChanged -= OnGameIdChanged;
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
        
        // Stop auto-save
        StopAutoSave();
    }
    
    private void OnGameIdChanged(NetworkString previousValue, NetworkString newValue)
    {
        if (string.IsNullOrEmpty(newValue.Value)) return;
        
        Debug.Log($"Game ID changed to: {newValue.Value}");
        currentGameId = newValue.Value;
        
        // Update input field
        if (gameIdInputField != null)
        {
            gameIdInputField.text = currentGameId;
        }
        
        // Update status text
        if (statusText != null)
        {
            statusText.text = $"Current Game ID: {currentGameId}";
        }
    }
    
    private void OnMoveExecuted()
    {
        // Check if we should auto-save after this move
        if (IsHost && enableAutoSave)
        {
            int currentMoveIndex = gameManager.LatestHalfMoveIndex;
            
            // Save every 5 moves (arbitrary number, adjust as needed)
            if (currentMoveIndex > 0 && currentMoveIndex % 5 == 0 && currentMoveIndex != lastSavedMoveIndex)
            {
                SaveGameStateAutomatically();
                lastSavedMoveIndex = currentMoveIndex;
            }
        }
    }
    
    public void OpenPanel()
    {
        if (gameStatePanel != null)
        {
            gameStatePanel.SetActive(true);
            
            // Update UI
            if (gameIdInputField != null)
            {
                gameIdInputField.text = currentGameId;
            }
            
            if (statusText != null)
            {
                statusText.text = $"Current Game ID: {currentGameId}";
            }
            
            // Only host can save/load games
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            if (saveGameButton != null) saveGameButton.interactable = isHost;
            if (loadGameButton != null) loadGameButton.interactable = isHost;
            if (autoSaveToggle != null) autoSaveToggle.interactable = isHost;
        }
    }
    
    private void ClosePanel()
    {
        if (gameStatePanel != null)
        {
            gameStatePanel.SetActive(false);
        }
    }
    
    private void SaveGameState()
    {
        if (!IsHost)
        {
            Debug.LogWarning("Only the host can save game states.");
            return;
        }
        
        if (firebaseManager == null || !firebaseManager.IsInitialized)
        {
            Debug.LogError("Firebase not initialized. Cannot save game state.");
            if (statusText != null)
            {
                statusText.text = "Error: Firebase not initialized";
            }
            return;
        }
        
        // Get the current game state
        string gameState = gameManager.SerializeGame();
        
        // Save to Firebase
        firebaseManager.SaveGameState(currentGameId, gameState, (success) => {
            if (success)
            {
                Debug.Log($"Game state saved with ID: {currentGameId}");
                
                // Update status text
                if (statusText != null)
                {
                    statusText.text = $"Game saved with ID: {currentGameId}";
                }
                
                // Notify clients
                NotifyGameSavedClientRpc(currentGameId);
            }
            else
            {
                Debug.LogError("Failed to save game state");
                if (statusText != null)
                {
                    statusText.text = "Error: Failed to save game";
                }
            }
        });
    }
    
    private void SaveGameStateAutomatically()
    {
        if (!IsHost)
        {
            return;
        }
        
        if (firebaseManager == null || !firebaseManager.IsInitialized)
        {
            Debug.LogError("Firebase not initialized. Cannot auto-save game state.");
            return;
        }
        
        // Get the current game state
        string gameState = gameManager.SerializeGame();
        
        // Save to Firebase
        firebaseManager.SaveGameState(currentGameId, gameState, (success) => {
            if (success)
            {
                Debug.Log($"Game auto-saved with ID: {currentGameId}");
                
                // Notify clients
                NotifyGameSavedClientRpc(currentGameId);
            }
            else
            {
                Debug.LogError("Failed to auto-save game state");
            }
        });
    }
    
    private void LoadGameState()
    {
        if (!IsHost)
        {
            Debug.LogWarning("Only the host can load game states.");
            return;
        }
        
        if (firebaseManager == null || !firebaseManager.IsInitialized)
        {
            Debug.LogError("Firebase not initialized. Cannot load game state.");
            if (statusText != null)
            {
                statusText.text = "Error: Firebase not initialized";
            }
            return;
        }
        
        // Get the game ID from input field
        string gameId = gameIdInputField.text;
        
        if (string.IsNullOrEmpty(gameId))
        {
            Debug.LogError("Game ID is empty. Cannot load game state.");
            if (statusText != null)
            {
                statusText.text = "Error: Game ID is empty";
            }
            return;
        }
        
        // Load from Firebase
        firebaseManager.LoadGameState(gameId, (gameState) => {
            if (gameState != null)
            {
                Debug.Log($"Game state loaded for ID: {gameId}");
                
                // Apply the game state
                gameManager.LoadGame(gameState);
                
                // Update current game ID
                SetGameId(gameId);
                
                // Update status text
                if (statusText != null)
                {
                    statusText.text = $"Game loaded with ID: {gameId}";
                }
                
                // Notify clients
                SyncGameStateClientRpc(gameState);
            }
            else
            {
                Debug.LogError($"Failed to load game state for ID: {gameId}");
                if (statusText != null)
                {
                    statusText.text = $"Error: Failed to load game with ID: {gameId}";
                }
            }
        });
    }
    
    private void GenerateNewGameId()
    {
        if (!IsHost) return;
        
        // Generate a unique ID for this game
        string gameId = GenerateGameId();
        SetGameId(gameId);
    }
    
    private void SetGameId(string gameId)
    {
        if (!IsHost) return;
        
        currentGameId = gameId;
        networkGameId.Value = new NetworkString(gameId);
    }
    
    private string GenerateGameId()
    {
        // Generate a unique ID for this game
        return System.Guid.NewGuid().ToString().Substring(0, 8);
    }
    
    private void OnAutoSaveToggled(bool enabled)
    {
        if (!IsHost) return;
        
        enableAutoSave = enabled;
        
        if (enabled)
        {
            StartAutoSave();
        }
        else
        {
            StopAutoSave();
        }
    }
    
    private void StartAutoSave()
    {
        if (autoSaveCoroutine != null)
        {
            StopCoroutine(autoSaveCoroutine);
        }
        
        autoSaveCoroutine = StartCoroutine(AutoSaveCoroutine());
        Debug.Log("Auto-save started");
    }
    
    private void StopAutoSave()
    {
        if (autoSaveCoroutine != null)
        {
            StopCoroutine(autoSaveCoroutine);
            autoSaveCoroutine = null;
            Debug.Log("Auto-save stopped");
        }
    }
    
    private IEnumerator AutoSaveCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(autoSaveInterval);
            
            if (IsHost && enableAutoSave)
            {
                SaveGameStateAutomatically();
            }
        }
    }
    
    [ClientRpc]
    private void NotifyGameSavedClientRpc(string gameId)
    {
        Debug.Log($"Game saved by host with ID: {gameId}");
        
        // Update game ID on clients
        currentGameId = gameId;
        
        // Update UI if panel is open
        if (gameStatePanel != null && gameStatePanel.activeSelf)
        {
            if (gameIdInputField != null)
            {
                gameIdInputField.text = gameId;
            }
            
            if (statusText != null)
            {
                statusText.text = $"Game saved by host with ID: {gameId}";
            }
        }
    }
    
    [ClientRpc]
    private void SyncGameStateClientRpc(string gameState)
    {
        if (IsHost) return; // Host already has the state
        
        Debug.Log("Loading game state from host");
        
        // Apply the game state
        gameManager.LoadGame(gameState);
        
        // Update UI if panel is open
        if (gameStatePanel != null && gameStatePanel.activeSelf)
        {
            if (statusText != null)
            {
                statusText.text = "Game state loaded from host";
            }
        }
    }
    
    // Helper struct for network variables
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