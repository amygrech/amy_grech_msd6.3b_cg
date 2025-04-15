using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;

/// <summary>
/// Manages the UI for displaying analytics data and game state management.
/// Combines functionality from original AnalyticsDashboard and AnalyticsUIManager.
/// </summary>
public class AnalyticsDashboard : MonoBehaviourSingleton<AnalyticsDashboard>
{
    [Header("Analytics Panel")]
    [SerializeField] private GameObject analyticsPanel; // Will always be shown
    [SerializeField] private Button refreshButton;
    [SerializeField] private GameObject loadingIndicator;

    [Header("Analytics Data Display")]
    [SerializeField] private TextMeshProUGUI topDLCsText;
    [SerializeField] private TextMeshProUGUI winLossText;
    [SerializeField] private TextMeshProUGUI matchStatsText;
    [SerializeField] private TextMeshProUGUI openingMovesText;
    [SerializeField] private TextMeshProUGUI totalGamesText; // Total games text display
    
    // Track the most recently saved match ID
    private string lastSavedMatchId;

    [Header("Game State Management")]
    [SerializeField] private Button saveGameButton;
    [SerializeField] private Button loadButton; // The Load button that loads the latest saved game
    [SerializeField] private TextMeshProUGUI saveLoadStatusText; // Optional status message

    [Header("Saved Game Items")]
    [SerializeField] private Transform savedGamesContainer; // Parent transform for saved game items
    [SerializeField] private GameObject savedGameItemPrefab; // Prefab for individual saved game items

    // List of currently displayed saved game items
    private List<GameObject> savedGameItems = new List<GameObject>();

    // Keep track of data load status
    private bool isDLCDataLoaded = false;
    private bool isGameDataLoaded = false;
    private bool isOpeningDataLoaded = false;

    // Track total games for analytics
    private int totalGamesPlayed = 0;

    private void Start()
    {
        // Set up UI buttons
        if (refreshButton != null)
            refreshButton.onClick.AddListener(RefreshDashboard);

        if (saveGameButton != null)
            saveGameButton.onClick.AddListener(SaveCurrentGame);

        if (loadButton != null)
            loadButton.onClick.AddListener(LoadLatestGame);

        // Make sure analytics panel is shown initially
        if (analyticsPanel != null)
            analyticsPanel.SetActive(true);

        // Initial data load
        RefreshDashboard();
        
        // Try to find the most recent game ID
        FindLatestGameId();
    }

    #region Analytics Panel

    /// <summary>
    /// Refreshes all analytics data from Firebase
    /// </summary>
    public void RefreshDashboard()
    {
        // Reset status trackers
        isDLCDataLoaded = false;
        isGameDataLoaded = false;
        isOpeningDataLoaded = false;
        
        // Show loading indicator if available
        if (loadingIndicator != null)
        {
            loadingIndicator.SetActive(true);
        }
        
        // Load DLC purchase data
        LoadTopDLCs();
        
        // Load game statistics
        LoadGameStatistics();
        
        // Load popular opening moves
        LoadPopularOpeningMoves();
    }
    
    private void LoadTopDLCs()
    {
        if (FirebaseManager.Instance == null || !FirebaseManager.Instance.IsInitialized)
        {
            Debug.LogError("Firebase not initialized!");
            topDLCsText.text = "Firebase not initialized. Cannot load DLC data.";
            isDLCDataLoaded = true;
            CheckAllDataLoaded();
            return;
        }
        
        FirebaseManager.Instance.Database.Child("purchases").GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError($"Error fetching DLC data: {task.Exception}");
                topDLCsText.text = "Error loading DLC data. Please try again.";
                isDLCDataLoaded = true;
                CheckAllDataLoaded();
                return;
            }
            
            if (task.IsCompleted)
            {
                Dictionary<string, int> avatarCounts = new Dictionary<string, int>();
                Dictionary<string, long> avatarRevenue = new Dictionary<string, long>();
                Dictionary<string, string> avatarNames = new Dictionary<string, string>();
                int totalPurchases = 0;

                foreach (var child in task.Result.Children)
                {
                    totalPurchases++;
                    
                    // Get basic purchase data
                    string avatarId = child.Child("avatarId").Value?.ToString() ?? "unknown";
                    string avatarName = child.Child("avatarName").Value?.ToString() ?? avatarId;
                    
                    // Get price as long value to avoid floating point errors
                    long price = 0;
                    if (child.Child("price").Value != null)
                    {
                        try
                        {
                            price = Convert.ToInt64(child.Child("price").Value);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"Failed to parse price: {ex.Message}");
                        }
                    }
                    
                    // Store the avatar name mapping
                    if (!avatarNames.ContainsKey(avatarId))
                    {
                        avatarNames[avatarId] = avatarName;
                    }
                    
                    // Update counts
                    if (avatarCounts.ContainsKey(avatarId))
                    {
                        avatarCounts[avatarId]++;
                        avatarRevenue[avatarId] += price;
                    }
                    else
                    {
                        avatarCounts[avatarId] = 1;
                        avatarRevenue[avatarId] = price;
                    }
                }
                
                // Create a sorted list by purchase count
                var sortedAvatars = avatarCounts.OrderByDescending(kvp => kvp.Value).ToList();
                
                topDLCsText.text = "Top Purchased DLCs:\n";
                
                if (sortedAvatars.Count == 0)
                {
                    topDLCsText.text += "No DLC purchases recorded yet.";
                }
                else
                {
                    foreach (var kvp in sortedAvatars.Take(5)) // Show top 5
                    {
                        string avatarName = avatarNames.ContainsKey(kvp.Key) ? avatarNames[kvp.Key] : kvp.Key;
                        long revenue = avatarRevenue.ContainsKey(kvp.Key) ? avatarRevenue[kvp.Key] : 0;
                        
                        topDLCsText.text += $"{avatarName}: {kvp.Value} purchases ({revenue} credits)\n";
                    }
                }
                
                isDLCDataLoaded = true;
                CheckAllDataLoaded();
            }
        });
    }
    
    private void LoadGameStatistics()
    {
        if (FirebaseManager.Instance == null || !FirebaseManager.Instance.IsInitialized)
        {
            Debug.LogError("Firebase not initialized!");
            matchStatsText.text = "Firebase not initialized. Cannot load match data.";
            winLossText.text = "Firebase not initialized.";
            isGameDataLoaded = true;
            CheckAllDataLoaded();
            return;
        }
        
        FirebaseManager.Instance.Database.Child("games").GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError($"Error fetching game data: {task.Exception}");
                matchStatsText.text = "Error loading match data. Please try again.";
                winLossText.text = "Error loading data.";
                isGameDataLoaded = true;
                CheckAllDataLoaded();
                return;
            }
            
            if (task.IsCompleted)
            {
                int totalGames = 0;
                int completedGames = 0;
                int inProgressGames = 0;
                
                // Win/loss tracking
                int wins = 0;
                int losses = 0;
                int draws = 0;
                
                // Duration tracking
                long totalDuration = 0;
                int gamesWithDuration = 0;
                
                // Total moves tracking
                int totalMoves = 0;
                int gamesWithMoves = 0;
                
                // Latest game timestamp
                long latestTimestamp = 0;
                string latestGameTime = "Never";
                
                foreach (var child in task.Result.Children)
                {
                    totalGames++;
                    
                    string status = child.Child("status").Value?.ToString() ?? "";
                    
                    // Track timestamp
                    if (child.Child("timestamp").Exists)
                    {
                        try
                        {
                            long timestamp = Convert.ToInt64(child.Child("timestamp").Value);
                            if (timestamp > latestTimestamp)
                            {
                                latestTimestamp = timestamp;
                                // Convert Unix timestamp to DateTime
                                System.DateTime dateTime = new System.DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
                                dateTime = dateTime.AddMilliseconds(timestamp).ToLocalTime();
                                latestGameTime = dateTime.ToString("yyyy-MM-dd HH:mm:ss");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"Failed to parse timestamp: {ex.Message}");
                        }
                    }
                    
                    if (status == "completed")
                    {
                        completedGames++;
                        
                        // Track game result
                        string result = child.Child("result").Value?.ToString() ?? "";
                        if (result == "win") wins++;
                        else if (result == "loss") losses++;
                        else if (result == "draw") draws++;
                        
                        // Track duration if available
                        if (child.Child("duration_seconds").Exists)
                        {
                            try
                            {
                                long duration = Convert.ToInt64(child.Child("duration_seconds").Value);
                                totalDuration += duration;
                                gamesWithDuration++;
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"Failed to parse duration: {ex.Message}");
                            }
                        }
                        
                        // Track move count if available
                        if (child.Child("move_count").Exists)
                        {
                            try
                            {
                                int moveCount = Convert.ToInt32(child.Child("move_count").Value);
                                totalMoves += moveCount;
                                gamesWithMoves++;
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"Failed to parse move count: {ex.Message}");
                            }
                        }
                    }
                    else if (status == "in_progress")
                    {
                        inProgressGames++;
                    }
                }
                
                // Save total games for reference
                totalGamesPlayed = totalGames;
                
                // Update win/loss text
                winLossText.text = $"Win/Loss/Draw: {wins}/{losses}/{draws}";
                
                // Update match stats text
                matchStatsText.text = "Chess Match Statistics:\n";
                matchStatsText.text += $"Total Games: {totalGames}\n";
                matchStatsText.text += $"Completed: {completedGames}\n";
                matchStatsText.text += $"In Progress: {inProgressGames}\n";
                
                if (gamesWithDuration > 0)
                {
                    float avgDuration = (float)totalDuration / gamesWithDuration;
                    matchStatsText.text += $"Avg. Duration: {FormatTimeSpan(avgDuration)}\n";
                }
                
                if (gamesWithMoves > 0)
                {
                    float avgMoves = (float)totalMoves / gamesWithMoves;
                    matchStatsText.text += $"Avg. Moves: {avgMoves:F1}";
                }
                
                // Update the total games text with highlighted formatting
                if (totalGamesText != null)
                {
                    totalGamesText.text = $"TOTAL GAMES PLAYED: {totalGames}";
                    
                    // Optional: Make it stand out
                    totalGamesText.fontSize = Mathf.Max(totalGamesText.fontSize, 24);
                    totalGamesText.fontStyle = FontStyles.Bold;
                    totalGamesText.color = new Color(0.2f, 0.6f, 1f); // Light blue color
                }
                
                isGameDataLoaded = true;
                CheckAllDataLoaded();
            }
        });
    }
    
    private void LoadPopularOpeningMoves()
    {
        if (FirebaseManager.Instance == null || !FirebaseManager.Instance.IsInitialized)
        {
            Debug.LogError("Firebase not initialized!");
            openingMovesText.text = "Firebase not initialized. Cannot load opening moves data.";
            isOpeningDataLoaded = true;
            CheckAllDataLoaded();
            return;
        }
        
        FirebaseManager.Instance.Database.Child("opening_moves").OrderByChild("count").LimitToLast(5)
            .GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError($"Error fetching opening moves data: {task.Exception}");
                openingMovesText.text = "Error loading opening moves data. Please try again.";
                isOpeningDataLoaded = true;
                CheckAllDataLoaded();
                return;
            }
            
            if (task.IsCompleted)
            {
                List<KeyValuePair<string, int>> topOpenings = new List<KeyValuePair<string, int>>();
                string mostPopularOpening = "None";
                int highestCount = 0;
                
                foreach (var child in task.Result.Children)
                {
                    string moveNotation = child.Key;
                    int count = 0;
                    
                    if (child.Child("count").Exists)
                    {
                        try
                        {
                            count = Convert.ToInt32(child.Child("count").Value);
                            
                            // Track the most popular opening
                            if (count > highestCount)
                            {
                                highestCount = count;
                                mostPopularOpening = FormatMoveNotation(moveNotation);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"Failed to parse opening move count: {ex.Message}");
                        }
                    }
                    
                    topOpenings.Add(new KeyValuePair<string, int>(moveNotation, count));
                }
                
                // Sort by count descending
                topOpenings.Sort((a, b) => b.Value.CompareTo(a.Value));
                
                openingMovesText.text = "Popular Opening Moves:\n";
                
                if (topOpenings.Count == 0)
                {
                    openingMovesText.text += "No opening moves recorded yet.";
                }
                else
                {
                    foreach (var move in topOpenings)
                    {
                        openingMovesText.text += $"{FormatMoveNotation(move.Key)}: {move.Value} times\n";
                    }
                }
                
                isOpeningDataLoaded = true;
                CheckAllDataLoaded();
            }
        });
    }
    
    private void CheckAllDataLoaded()
    {
        if (isDLCDataLoaded && isGameDataLoaded && isOpeningDataLoaded)
        {
            // Hide loading indicator if available
            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(false);
            }
            
            // Show total games in a more prominent way
            HighlightTotalGames();
        }
    }
    
    /// <summary>
    /// Highlights the total games text to make it more noticeable
    /// </summary>
    private void HighlightTotalGames()
    {
        if (totalGamesText != null)
        {
            // Add visual flourishes if needed
            totalGamesText.text = $"TOTAL GAMES PLAYED: {totalGamesPlayed}";
        }
    }
    
    /// <summary>
    /// Updates the analytics display with data from Firebase
    /// </summary>
    public void UpdateAnalyticsDisplay(Dictionary<string, object> data)
    {
        // Update game count
        if (totalGamesText != null && data.TryGetValue("totalGames", out object gamesValue))
        {
            long totalGames = Convert.ToInt64(gamesValue);
            totalGamesText.text = $"TOTAL GAMES PLAYED: {totalGames}";
            totalGamesPlayed = (int)totalGames;
        }

        // Update DLC purchases
        if (topDLCsText != null && data.TryGetValue("totalPurchases", out object purchasesValue))
        {
            long purchases = Convert.ToInt64(purchasesValue);
            topDLCsText.text = $"Total DLC Purchases: {purchases}";
        }
    }

    #endregion

    #region Game State Management

    /// <summary>
    /// Saves the current game state and logs to console
    /// </summary>
    public void SaveCurrentGame()
    {
        BoardStateManager boardStateManager = FindObjectOfType<BoardStateManager>();
        if (boardStateManager != null)
        {
            boardStateManager.SaveCurrentBoardState();
            
            // Get match ID from GameSessionSyncManager
            StartCoroutine(GetSavedMatchId());
            
            // Show feedback to user
            Debug.Log("Game state saved successfully!");
            if (saveLoadStatusText != null)
            {
                saveLoadStatusText.text = "Game saved!";
                saveLoadStatusText.color = Color.green;
            }
        }
        else
        {
            Debug.LogError("BoardStateManager not found in the scene!");
            if (saveLoadStatusText != null)
            {
                saveLoadStatusText.text = "Error: Could not save game!";
                saveLoadStatusText.color = Color.red;
            }
        }
    }
    
    /// <summary>
    /// Gets the match ID of the most recently saved game
    /// </summary>
    private IEnumerator GetSavedMatchId()
    {
        // We need to wait a frame because the match ID is set asynchronously
        yield return null;
        
        // Get match ID from GameSessionSyncManager
        GameSessionSyncManager syncManager = FindObjectOfType<GameSessionSyncManager>();
        
        if (syncManager != null)
        {
            // Use reflection to get the matchId since it's private
            System.Reflection.FieldInfo matchIdField = 
                typeof(GameSessionSyncManager).GetField("currentMatchId", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            
            if (matchIdField != null)
            {
                lastSavedMatchId = (string)matchIdField.GetValue(syncManager);
                
                // Log the match ID
                if (!string.IsNullOrEmpty(lastSavedMatchId))
                {
                    Debug.Log($"Game saved with Match ID: {lastSavedMatchId}");
                }
            }
        }
    }
    
    /// <summary>
    /// Finds the most recent game ID in Firebase
    /// </summary>
    private void FindLatestGameId()
    {
        if (FirebaseManager.Instance == null || !FirebaseManager.Instance.IsInitialized)
        {
            Debug.LogWarning("Firebase not initialized, can't find latest game ID");
            return;
        }
        
        FirebaseManager.Instance.Database.Child("games")
            .OrderByChild("timestamp")
            .LimitToLast(1)
            .GetValueAsync()
            .ContinueWithOnMainThread(task => {
                if (task.IsFaulted)
                {
                    Debug.LogError($"Error finding latest game: {task.Exception}");
                    return;
                }
                
                if (task.IsCompleted && task.Result.ChildrenCount > 0)
                {
                    DataSnapshot latestGame = null;
                    foreach (var child in task.Result.Children)
                    {
                        // There should only be one child since we limited to last 1
                        latestGame = child;
                    }
                    
                    if (latestGame != null && latestGame.Child("state").Exists)
                    {
                        lastSavedMatchId = latestGame.Key;
                        Debug.Log($"Found latest saved game ID: {lastSavedMatchId}");
                    }
                }
            });
    }
    
    /// <summary>
    /// Loads the most recently saved game
    /// </summary>
    public void LoadLatestGame()
    {
        if (string.IsNullOrEmpty(lastSavedMatchId))
        {
            Debug.LogWarning("No saved game ID found. Finding latest game...");
            FindLatestGameIdAndLoad();
            return;
        }
        
        LoadGame(lastSavedMatchId);
    }
    
    /// <summary>
    /// Finds the latest game ID and loads it immediately
    /// </summary>
    private void FindLatestGameIdAndLoad()
    {
        if (FirebaseManager.Instance == null || !FirebaseManager.Instance.IsInitialized)
        {
            Debug.LogError("Firebase not initialized, can't load latest game");
            if (saveLoadStatusText != null)
            {
                saveLoadStatusText.text = "Error: Firebase not initialized!";
                saveLoadStatusText.color = Color.red;
            }
            return;
        }
        
        FirebaseManager.Instance.Database.Child("games")
            .OrderByChild("timestamp")
            .LimitToLast(1)
            .GetValueAsync()
            .ContinueWithOnMainThread(task => {
                if (task.IsFaulted)
                {
                    Debug.LogError($"Error finding latest game: {task.Exception}");
                    if (saveLoadStatusText != null)
                    {
                        saveLoadStatusText.text = "Error: Could not find any games!";
                        saveLoadStatusText.color = Color.red;
                    }
                    return;
                }
                
                if (task.IsCompleted && task.Result.ChildrenCount > 0)
                {
                    DataSnapshot latestGame = null;
                    foreach (var child in task.Result.Children)
                    {
                        // There should only be one child since we limited to last 1
                        latestGame = child;
                    }
                    
                    if (latestGame != null && latestGame.Child("state").Exists)
                    {
                        string gameId = latestGame.Key;
                        lastSavedMatchId = gameId;
                        Debug.Log($"Found and loading latest game ID: {gameId}");
                        LoadGame(gameId);
                    }
                    else
                    {
                        Debug.LogWarning("No saved games found with state data");
                        if (saveLoadStatusText != null)
                        {
                            saveLoadStatusText.text = "No saved games found!";
                            saveLoadStatusText.color = Color.yellow;
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("No saved games found");
                    if (saveLoadStatusText != null)
                    {
                        saveLoadStatusText.text = "No saved games found!";
                        saveLoadStatusText.color = Color.yellow;
                    }
                }
            });
    }
    
    /// <summary>
    /// Loads a specific saved game
    /// </summary>
    private void LoadGame(string gameId)
    {
        BoardStateManager boardStateManager = FindObjectOfType<BoardStateManager>();
        if (boardStateManager != null)
        {
            Debug.Log($"Loading game: {gameId}");
            boardStateManager.LoadBoardState(gameId);
            
            if (saveLoadStatusText != null)
            {
                saveLoadStatusText.text = "Loading game...";
                saveLoadStatusText.color = Color.yellow;
                
                // Update status text after a delay
                StartCoroutine(UpdateStatusAfterDelay("Game loaded successfully!", Color.green, 2f));
            }
        }
        else
        {
            Debug.LogError("BoardStateManager not found in the scene!");
            if (saveLoadStatusText != null)
            {
                saveLoadStatusText.text = "Error: Could not load game!";
                saveLoadStatusText.color = Color.red;
            }
        }
    }
    
    /// <summary>
    /// Updates status text after a delay
    /// </summary>
    private IEnumerator UpdateStatusAfterDelay(string message, Color color, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (saveLoadStatusText != null)
        {
            saveLoadStatusText.text = message;
            saveLoadStatusText.color = color;
        }
    }

    #endregion

    #region Helper Methods
    
    // Helper to format move notation for display
    private string FormatMoveNotation(string rawNotation)
    {
        try
        {
            // Parse the raw notation (e.g., "P:e2-e4")
            string[] parts = rawNotation.Split(':');
            if (parts.Length != 2) return rawNotation;
            
            string pieceType = parts[0];
            string[] positions = parts[1].Split('-');
            if (positions.Length != 2) return rawNotation;
            
            string from = positions[0];
            string to = positions[1];
            
            // Translate piece type to standard chess notation
            string pieceSymbol = "";
            switch (pieceType)
            {
                case "P": pieceSymbol = ""; break; // Pawns don't get a symbol in standard notation
                case "N": pieceSymbol = "N"; break;
                case "B": pieceSymbol = "B"; break;
                case "R": pieceSymbol = "R"; break;
                case "Q": pieceSymbol = "Q"; break;
                case "K": pieceSymbol = "K"; break;
                default: pieceSymbol = pieceType; break;
            }
            
            // Create standard notation (e.g., "e4" or "Nf3")
            return pieceSymbol + to;
        }
        catch
        {
            // Return raw notation if parsing fails
            return rawNotation;
        }
    }
    
    // Helper to format time span in readable format
    private string FormatTimeSpan(float totalSeconds)
    {
        int minutes = (int)(totalSeconds / 60);
        int seconds = (int)(totalSeconds % 60);
        
        return $"{minutes}m {seconds}s";
    }
    
    #endregion
}