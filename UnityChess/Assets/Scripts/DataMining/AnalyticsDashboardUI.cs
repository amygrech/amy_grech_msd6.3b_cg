using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AnalyticsDashboardUI : MonoBehaviour
{
    // Change in AnalyticsDashboardUI.cs
    [Header("Dashboard Elements")]
    public GameObject dashboardPanel; 
    public Button closeButton;         
    public Text headerText;            
    
    [Header("Game Stats Section")]
    [SerializeField] private Text totalGamesPlayedText;
    [SerializeField] private Text totalMovesPlayedText;
    [SerializeField] private Text averageMovesPerGameText;
    
    [Header("Opening Moves Section")]
    [SerializeField] private Text popularOpeningsText;
    
    [Header("DLC Stats Section")]
    [SerializeField] private Text popularDLCText;
    [SerializeField] private Text totalPurchasesText;
    
    [Header("Game Results Section")]
    [SerializeField] private Text gameResultsText;
    
    [Header("Game State Management")]
    [SerializeField] private InputField gameIdInputField;
    [SerializeField] private Button saveGameButton;
    [SerializeField] private Button loadGameButton;
    [SerializeField] private Text savedGameIdText;
    
    // Reference to the analytics manager
    private GameAnalyticsManager analyticsManager;
    
    private void Awake()
    {
        analyticsManager = FindObjectOfType<GameAnalyticsManager>();
        
        // Set up button listeners
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(CloseDashboard);
        }
        
        if (saveGameButton != null)
        {
            saveGameButton.onClick.AddListener(SaveGameState);
        }
        
        if (loadGameButton != null)
        {
            loadGameButton.onClick.AddListener(LoadGameState);
        }
        
        // Hide dashboard initially
        if (dashboardPanel != null)
        {
            dashboardPanel.SetActive(false);
        }
    }
    
    private void SaveGameState()
    {
        if (analyticsManager != null)
        {
            analyticsManager.SaveCurrentGameState();
            
            // You could update the saved game ID text here if needed
            // This would require a way to get the current game ID from the analytics manager
        }
    }
    
    private void LoadGameState()
    {
        if (analyticsManager != null && gameIdInputField != null)
        {
            string gameId = gameIdInputField.text;
            if (!string.IsNullOrEmpty(gameId))
            {
                analyticsManager.LoadGameState();
            }
            else
            {
                Debug.LogWarning("Game ID is empty. Cannot load game state.");
                // You could show an error message to the user here
            }
        }
    }
    
    public void OpenDashboard()
    {
        if (dashboardPanel != null)
        {
            dashboardPanel.SetActive(true);
            
            // Update dashboard data when opening
            RefreshDashboardData();
        }
    }
    
    private void CloseDashboard()
    {
        if (dashboardPanel != null)
        {
            dashboardPanel.SetActive(false);
        }
    }
    
    public void RefreshDashboardData()
    {
        // This method would ideally pull data from the GameAnalyticsManager
        // For now, we'll just display some placeholder data
        
        if (totalGamesPlayedText != null)
        {
            // In a real implementation, you would get this from GameAnalyticsManager
            totalGamesPlayedText.text = "Total Games: Loading...";
        }
        
        if (totalMovesPlayedText != null)
        {
            totalMovesPlayedText.text = "Total Moves: Loading...";
        }
        
        if (averageMovesPerGameText != null)
        {
            averageMovesPerGameText.text = "Avg. Moves/Game: Loading...";
        }
        
        if (popularOpeningsText != null)
        {
            popularOpeningsText.text = "Popular Openings:\nLoading...";
        }
        
        if (popularDLCText != null)
        {
            popularDLCText.text = "Popular DLC Items:\nLoading...";
        }
        
        if (totalPurchasesText != null)
        {
            totalPurchasesText.text = "Total Purchases: Loading...";
        }
        
        if (gameResultsText != null)
        {
            gameResultsText.text = "Game Results:\nLoading...";
        }
        
        // When data is available, update the UI (this would be called from a callback)
        StartCoroutine(GetAnalyticsData());
    }
    
    private IEnumerator GetAnalyticsData()
    {
        // Simulate a delay for data loading (in a real implementation, this would be a Firebase callback)
        yield return new WaitForSeconds(0.5f);
        
        // Update UI with data
        // In a real implementation, you would get this data from GameAnalyticsManager
        if (totalGamesPlayedText != null) totalGamesPlayedText.text = "Total Games: 42";
        if (totalMovesPlayedText != null) totalMovesPlayedText.text = "Total Moves: 1,253";
        if (averageMovesPerGameText != null) averageMovesPerGameText.text = "Avg. Moves/Game: 29.8";
        
        if (popularOpeningsText != null)
        {
            popularOpeningsText.text = "Popular Openings:\n" +
                "1. e4 (King's Pawn): 18\n" +
                "2. d4 (Queen's Pawn): 12\n" +
                "3. Nf3 (Réti Opening): 7";
        }
        
        if (popularDLCText != null)
        {
            popularDLCText.text = "Popular DLC Items:\n" +
                "1. Turtle Avatar: 15\n" +
                "2. Shell Avatar: 8\n" +
                "3. Pirate Set: 4";
        }
        
        if (totalPurchasesText != null) totalPurchasesText.text = "Total Purchases: 27";
        
        if (gameResultsText != null)
        {
            gameResultsText.text = "Game Results:\n" +
                "White Wins: 21\n" +
                "Black Wins: 14\n" +
                "Draws: 7";
        }
    }
}