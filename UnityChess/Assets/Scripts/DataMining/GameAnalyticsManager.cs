using System;
using System.Collections.Generic;
using UnityEngine;
using Firebase.Analytics;
using Firebase.Database;
using Firebase.Extensions;
using UnityChess;
using Unity.Netcode;
using System.Threading.Tasks;

public class GameAnalyticsManager : MonoBehaviour
{
    // Singleton instance
    public static GameAnalyticsManager Instance { get; private set; }
    
    // References
    private FirebaseManager firebaseManager;
    private ChessNetworkManager networkManager;
    private GameManager gameManager;
    
    // Database references
    private DatabaseReference gameStatesRef;
    private DatabaseReference analyticsRef;
    
    // Analytics data
    private Dictionary<string, int> openingMoveStats = new Dictionary<string, int>();
    private Dictionary<string, int> dlcPurchaseStats = new Dictionary<string, int>();
    private Dictionary<string, int> gameResultStats = new Dictionary<string, int>();
    private int totalGamesPlayed = 0;
    private int totalMovesPlayed = 0;
    
    // Current game tracking
    private string currentGameId;
    private DateTime gameStartTime;
    private bool isGameActive = false;
    private List<string> movesPlayed = new List<string>();
    
    // Flag to determine if Firebase is properly initialized
    private bool isFirebaseInitialized = false;
    
    [Header("Analytics Dashboard")]
    [SerializeField] private GameObject analyticsDashboardPanel;
    [SerializeField] private UnityEngine.UI.Text totalGamesText;
    [SerializeField] private UnityEngine.UI.Text totalMovesText;
    [SerializeField] private UnityEngine.UI.Text topOpeningMovesText;
    [SerializeField] private UnityEngine.UI.Text topDlcPurchasesText;
    [SerializeField] private UnityEngine.UI.Text gameResultsText;
    [SerializeField] private UnityEngine.UI.Button saveGameButton;
    [SerializeField] private UnityEngine.UI.Button loadGameButton;
    [SerializeField] private UnityEngine.UI.InputField loadGameIdInput;
    [SerializeField] private UnityEngine.UI.Button showAnalyticsButton;
    
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
        
        // Find FirebaseManager
        firebaseManager = FindObjectOfType<FirebaseManager>();
        if (firebaseManager == null)
        {
            Debug.LogError("FirebaseManager not found. Analytics will be disabled.");
        }
        else if (firebaseManager.IsInitialized)
        {
            InitializeFirebaseReferences();
        }
        else
        {
            // Wait for Firebase to initialize
            firebaseManager.OnFirebaseInitialized += InitializeFirebaseReferences;
        }
        
        // Get references to other managers
        networkManager = FindObjectOfType<ChessNetworkManager>();
        gameManager = FindObjectOfType<GameManager>();
    }
    
    private void Start()
    {
        // Subscribe to relevant game events
        if (gameManager != null)
        {
            GameManager.NewGameStartedEvent += OnGameStarted;
            GameManager.GameEndedEvent += OnGameEnded;
            GameManager.MoveExecutedEvent += OnMoveExecuted;
        }
        
        // Set up UI button listeners
        if (saveGameButton != null)
        {
            saveGameButton.onClick.AddListener(SaveCurrentGameState);
        }
        
        if (loadGameButton != null)
        {
            loadGameButton.onClick.AddListener(LoadGameState);
        }
        
        if (showAnalyticsButton != null)
        {
            showAnalyticsButton.onClick.AddListener(ToggleAnalyticsDashboard);
        }
        
        // Hide analytics dashboard initially
        if (analyticsDashboardPanel != null)
        {
            analyticsDashboardPanel.SetActive(false);
        }
        
        // Load analytics data
        LoadAnalyticsData();
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (gameManager != null)
        {
            GameManager.NewGameStartedEvent -= OnGameStarted;
            GameManager.GameEndedEvent -= OnGameEnded;
            GameManager.MoveExecutedEvent -= OnMoveExecuted;
        }
        
        if (firebaseManager != null)
        {
            firebaseManager.OnFirebaseInitialized -= InitializeFirebaseReferences;
        }
    }
    
    private void InitializeFirebaseReferences()
    {
        if (firebaseManager != null && firebaseManager.Database != null)
        {
            gameStatesRef = firebaseManager.Database.Child("gameStates");
            analyticsRef = firebaseManager.Database.Child("analytics");
            isFirebaseInitialized = true;
            
            // Load analytics data
            LoadAnalyticsData();
            
            Debug.Log("Firebase references initialized for analytics");
        }
        else
        {
            Debug.LogError("Failed to initialize Firebase references for analytics");
        }
    }
    
    #region Game Event Handlers
    
    private void OnGameStarted()
    {
        // Generate a unique ID for this game
        currentGameId = GenerateGameId();
        gameStartTime = DateTime.Now;
        isGameActive = true;
        movesPlayed.Clear();
        
        // Log the game start event
        LogGameStartEvent();
    }
    
    private void OnGameEnded()
    {
        isGameActive = false;
        
        // Determine the game result
        string result = DetermineGameResult();
        
        // Log the game end event
        LogGameEndEvent(result);
        
        // Track game result for analytics
        if (!gameResultStats.ContainsKey(result))
        {
            gameResultStats[result] = 0;
        }
        gameResultStats[result]++;
        
        // Save analytics data
        SaveAnalyticsData();
        
        // Update UI
        UpdateAnalyticsDashboard();
    }
    
    private void OnMoveExecuted()
    {
        if (!isGameActive) return;
        
        // Get the last move
        if (gameManager.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove))
        {
            string moveNotation = latestHalfMove.ToString();
            movesPlayed.Add(moveNotation);
            
            // Increment total moves counter
            totalMovesPlayed++;
            
            // Track opening moves (first 2 moves of each game)
            if (movesPlayed.Count <= 2)
            {
                if (!openingMoveStats.ContainsKey(moveNotation))
                {
                    openingMoveStats[moveNotation] = 0;
                }
                openingMoveStats[moveNotation]++;
                
                // Save analytics after each opening move
                SaveAnalyticsData();
            }
        }
    }
    
    #endregion
    
    #region Analytics Logging
    
    public void LogGameStartEvent()
    {
        if (!isFirebaseInitialized) return;
        
        // Log to Firebase Analytics
        if (firebaseManager != null)
        {
            firebaseManager.LogGameStartEvent(currentGameId);
            Debug.Log($"Game start logged: {currentGameId}");
        }
        
        // Increment total games counter
        totalGamesPlayed++;
        
        // Save analytics data
        SaveAnalyticsData();
        
        // Update UI
        UpdateAnalyticsDashboard();
    }
    
    public void LogGameEndEvent(string result)
    {
        if (!isFirebaseInitialized) return;
        
        // Calculate game duration
        TimeSpan duration = DateTime.Now - gameStartTime;
        
        // Log to Firebase Analytics
        if (firebaseManager != null)
        {
            firebaseManager.LogGameEndEvent(currentGameId, result, movesPlayed.Count, duration.TotalSeconds);
            Debug.Log($"Game end logged: {currentGameId}, Result: {result}, Moves: {movesPlayed.Count}");
        }
    }
    
    public void LogDLCPurchaseEvent(string itemId, string itemName, int price)
    {
        if (!isFirebaseInitialized) return;
        
        // Log to Firebase Analytics
        if (firebaseManager != null)
        {
            firebaseManager.LogPurchaseEvent(itemId, itemName, price);
            Debug.Log($"DLC purchase logged: {itemName} for {price} credits");
        }
        
        // Track DLC purchase for analytics
        if (!dlcPurchaseStats.ContainsKey(itemName))
        {
            dlcPurchaseStats[itemName] = 0;
        }
        dlcPurchaseStats[itemName]++;
        
        // Save analytics data
        SaveAnalyticsData();
        
        // Update UI
        UpdateAnalyticsDashboard();
    }
    
    #endregion
    
    #region Game State Management
    
    public void SaveCurrentGameState()
    {
        if (!isFirebaseInitialized)
        {
            Debug.LogError("Firebase not initialized. Cannot save game state.");
            return;
        }
        
        // Get the current game state as a serialized string
        string gameState = gameManager.SerializeGame();
        
        // Generate a unique ID if one doesn't exist
        if (string.IsNullOrEmpty(currentGameId))
        {
            currentGameId = GenerateGameId();
        }
        
        // Create game state data
        Dictionary<string, object> gameStateData = new Dictionary<string, object>
        {
            { "state", gameState },
            { "timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
            { "moveCount", gameManager.LatestHalfMoveIndex },
            { "playerSide", gameManager.SideToMove.ToString() }
        };
        
        // Save to Firebase
        firebaseManager.SaveGameState(currentGameId, gameState, (success) => {
            if (success)
            {
                Debug.Log($"Game state saved successfully with ID: {currentGameId}");
                
                // Show confirmation message to user
                // For example: UIManager.Instance.ShowMessage($"Game saved! ID: {currentGameId}");
                
                // If we're in a networked game, notify other players
                if (networkManager != null && NetworkManager.Singleton.IsHost)
                {
                    NotifyGameStateSavedClientRpc(currentGameId);
                }
            }
            else
            {
                Debug.LogError("Failed to save game state");
            }
        });
    }
    
    public void LoadGameState()
    {
        if (!isFirebaseInitialized)
        {
            Debug.LogError("Firebase not initialized. Cannot load game state.");
            return;
        }
        
        // Get the game ID from input field
        string gameId = loadGameIdInput.text;
        
        if (string.IsNullOrEmpty(gameId))
        {
            Debug.LogError("Game ID is empty. Cannot load game state.");
            return;
        }
        
        // Load from Firebase
        firebaseManager.LoadGameState(gameId, (gameState) => {
            if (gameState != null)
            {
                Debug.Log($"Game state loaded successfully for ID: {gameId}");
                
                // Apply the game state
                gameManager.LoadGame(gameState);
                
                // Update current game ID
                currentGameId = gameId;
                
                // If we're in a networked game, notify other players
                if (networkManager != null && NetworkManager.Singleton.IsHost)
                {
                    SyncLoadedGameStateClientRpc(gameState);
                }
            }
            else
            {
                Debug.LogError($"Failed to load game state for ID: {gameId}");
            }
        });
    }
    
    [ClientRpc]
    private void NotifyGameStateSavedClientRpc(string gameId)
    {
        Debug.Log($"Game state saved by host with ID: {gameId}");
        // Update UI or show notification to client
        // For example: UIManager.Instance.ShowMessage($"Game saved by host! ID: {gameId}");
    }
    
    [ClientRpc]
    private void SyncLoadedGameStateClientRpc(string gameState)
    {
        Debug.Log("Loading game state from host");
        
        // Apply the game state
        gameManager.LoadGame(gameState);
    }
    
    #endregion
    
    #region Analytics Dashboard
    
    public void ToggleAnalyticsDashboard()
    {
        if (analyticsDashboardPanel != null)
        {
            bool newState = !analyticsDashboardPanel.activeSelf;
            analyticsDashboardPanel.SetActive(newState);
            
            if (newState)
            {
                UpdateAnalyticsDashboard();
            }
        }
    }
    
    private void UpdateAnalyticsDashboard()
    {
        if (analyticsDashboardPanel == null || !analyticsDashboardPanel.activeSelf) return;
        
        // Update total games and moves
        if (totalGamesText != null)
        {
            totalGamesText.text = $"Total Games: {totalGamesPlayed}";
        }
        
        if (totalMovesText != null)
        {
            totalMovesText.text = $"Total Moves: {totalMovesPlayed}";
        }
        
        // Update top opening moves
        if (topOpeningMovesText != null)
        {
            string openingText = "Top Opening Moves:\n";
            var sortedOpenings = SortDictionaryByValue(openingMoveStats);
            int count = 0;
            foreach (var opening in sortedOpenings)
            {
                openingText += $"- {opening.Key}: {opening.Value} times\n";
                count++;
                if (count >= 3) break; // Show top 3
            }
            topOpeningMovesText.text = openingText;
        }
        
        // Update top DLC purchases
        if (topDlcPurchasesText != null)
        {
            string dlcText = "Top DLC Purchases:\n";
            var sortedDlc = SortDictionaryByValue(dlcPurchaseStats);
            int count = 0;
            foreach (var dlc in sortedDlc)
            {
                dlcText += $"- {dlc.Key}: {dlc.Value} times\n";
                count++;
                if (count >= 3) break; // Show top 3
            }
            topDlcPurchasesText.text = dlcText;
        }
        
        // Update game results
        if (gameResultsText != null)
        {
            string resultsText = "Game Results:\n";
            foreach (var result in gameResultStats)
            {
                resultsText += $"- {result.Key}: {result.Value} games\n";
            }
            gameResultsText.text = resultsText;
        }
    }
    
    #endregion
    
    #region Helper Methods
    
    private string GenerateGameId()
    {
        // Generate a unique ID for this game
        return System.Guid.NewGuid().ToString().Substring(0, 8);
    }
    
    private string DetermineGameResult()
    {
        // Get the latest half-move
        if (gameManager.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove))
        {
            if (latestHalfMove.CausedCheckmate)
            {
                // Determine winner based on side to move (opposite side won)
                Side winningSide = gameManager.SideToMove == Side.White ? Side.Black : Side.White;
                return $"{winningSide} Win by Checkmate";
            }
            else if (latestHalfMove.CausedStalemate)
            {
                return "Draw by Stalemate";
            }
        }
        
        // If game ended but not by checkmate or stalemate, assume resignation
        return "Resignation";
    }
    
    private void SaveAnalyticsData()
    {
        if (!isFirebaseInitialized) return;
        
        Dictionary<string, object> analyticsData = new Dictionary<string, object>
        {
            { "totalGames", totalGamesPlayed },
            { "totalMoves", totalMovesPlayed },
            { "openingMoves", ConvertDictionaryToObject(openingMoveStats) },
            { "dlcPurchases", ConvertDictionaryToObject(dlcPurchaseStats) },
            { "gameResults", ConvertDictionaryToObject(gameResultStats) },
            { "lastUpdated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
        };
        
        analyticsRef.UpdateChildrenAsync(analyticsData).ContinueWithOnMainThread(task => {
            if (task.IsFaulted)
            {
                Debug.LogError($"Error saving analytics data: {task.Exception}");
            }
            else
            {
                Debug.Log("Analytics data saved successfully");
            }
        });
    }
    
    private void LoadAnalyticsData()
    {
        if (!isFirebaseInitialized) return;
        
        analyticsRef.GetValueAsync().ContinueWithOnMainThread(task => {
            if (task.IsFaulted)
            {
                Debug.LogError($"Error loading analytics data: {task.Exception}");
                return;
            }
            
            if (task.IsCompleted && task.Result.Exists)
            {
                DataSnapshot snapshot = task.Result;
                
                // Load total games and moves
                if (snapshot.Child("totalGames").Exists)
                {
                    totalGamesPlayed = Convert.ToInt32(snapshot.Child("totalGames").Value);
                }
                
                if (snapshot.Child("totalMoves").Exists)
                {
                    totalMovesPlayed = Convert.ToInt32(snapshot.Child("totalMoves").Value);
                }
                
                // Load opening moves
                if (snapshot.Child("openingMoves").Exists)
                {
                    openingMoveStats = new Dictionary<string, int>();
                    foreach (DataSnapshot child in snapshot.Child("openingMoves").Children)
                    {
                        openingMoveStats[child.Key] = Convert.ToInt32(child.Value);
                    }
                }
                
                // Load DLC purchases
                if (snapshot.Child("dlcPurchases").Exists)
                {
                    dlcPurchaseStats = new Dictionary<string, int>();
                    foreach (DataSnapshot child in snapshot.Child("dlcPurchases").Children)
                    {
                        dlcPurchaseStats[child.Key] = Convert.ToInt32(child.Value);
                    }
                }
                
                // Load game results
                if (snapshot.Child("gameResults").Exists)
                {
                    gameResultStats = new Dictionary<string, int>();
                    foreach (DataSnapshot child in snapshot.Child("gameResults").Children)
                    {
                        gameResultStats[child.Key] = Convert.ToInt32(child.Value);
                    }
                }
                
                Debug.Log("Analytics data loaded successfully");
                
                // Update UI
                UpdateAnalyticsDashboard();
            }
        });
    }
    
    private Dictionary<string, object> ConvertDictionaryToObject(Dictionary<string, int> dictionary)
    {
        Dictionary<string, object> result = new Dictionary<string, object>();
        foreach (var kvp in dictionary)
        {
            result[kvp.Key] = kvp.Value;
        }
        return result;
    }
    
    private List<KeyValuePair<string, int>> SortDictionaryByValue(Dictionary<string, int> dictionary)
    {
        List<KeyValuePair<string, int>> sortedList = new List<KeyValuePair<string, int>>(dictionary);
        sortedList.Sort((a, b) => b.Value.CompareTo(a.Value)); // Sort in descending order
        return sortedList;
    }
    
    #endregion
}