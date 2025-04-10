using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityChess;

/// <summary>
/// Displays real-time statistics about the current game.
/// This can be toggled on/off during gameplay.
/// </summary>
public class GameStatsDisplay : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject statsPanel;
    [SerializeField] private Text movesCountText;
    [SerializeField] private Text gameTimeText;
    [SerializeField] private Text currentTurnText;
    [SerializeField] private Text capturedPiecesText;
    [SerializeField] private Button toggleButton;
    [SerializeField] private Button saveStateButton;
    
    [Header("Settings")]
    [SerializeField] private bool showOnStart = false;
    [SerializeField] private float updateInterval = 0.5f;
    
    // References
    private GameManager gameManager;
    private GameAnalyticsManager analyticsManager;
    
    // Game tracking
    private float gameStartTime;
    private int whiteCaptureCount = 0;
    private int blackCaptureCount = 0;
    private List<Piece> capturedPieces = new List<Piece>();
    
    private void Awake()
    {
        gameManager = FindObjectOfType<GameManager>();
        analyticsManager = FindObjectOfType<GameAnalyticsManager>();
        
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(ToggleStatsPanel);
        }
        
        if (saveStateButton != null && analyticsManager != null)
        {
            saveStateButton.onClick.AddListener(analyticsManager.SaveCurrentGameState);
        }
        
        // Hide stats panel initially if not showing on start
        if (statsPanel != null)
        {
            statsPanel.SetActive(showOnStart);
        }
    }
    
    private void Start()
    {
        // Subscribe to game events
        GameManager.NewGameStartedEvent += OnGameStarted;
        GameManager.MoveExecutedEvent += OnMoveExecuted;
        
        // Start updating stats
        StartCoroutine(UpdateStatsRoutine());
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        GameManager.NewGameStartedEvent -= OnGameStarted;
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
    }
    
    private void OnGameStarted()
    {
        // Reset stats
        gameStartTime = Time.time;
        whiteCaptureCount = 0;
        blackCaptureCount = 0;
        capturedPieces.Clear();
        
        // Update UI
        UpdateStats();
    }
    
    private void OnMoveExecuted()
    {
        // Check for captures by comparing the board before and after the move
        if (gameManager.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove))
        {
            if (latestHalfMove.CapturedPiece != null)
            {
                // Add to captured pieces list
                capturedPieces.Add(latestHalfMove.CapturedPiece);
                
                // Update counter based on owner
                if (latestHalfMove.CapturedPiece.Owner == Side.White)
                {
                    blackCaptureCount++;
                }
                else
                {
                    whiteCaptureCount++;
                }
            }
        }
        
        // Update UI
        UpdateStats();
    }
    
    private IEnumerator UpdateStatsRoutine()
    {
        while (true)
        {
            if (statsPanel != null && statsPanel.activeSelf)
            {
                UpdateStats();
            }
            
            yield return new WaitForSeconds(updateInterval);
        }
    }
    
    private void UpdateStats()
    {
        if (!statsPanel.activeSelf) return;
        
        // Update moves count
        if (movesCountText != null)
        {
            int moveCount = gameManager.LatestHalfMoveIndex;
            int fullMoveCount = (moveCount + 1) / 2;
            movesCountText.text = $"Moves: {fullMoveCount}";
        }
        
        // Update game time
        if (gameTimeText != null)
        {
            float elapsedTime = Time.time - gameStartTime;
            int minutes = Mathf.FloorToInt(elapsedTime / 60f);
            int seconds = Mathf.FloorToInt(elapsedTime % 60f);
            gameTimeText.text = $"Time: {minutes:00}:{seconds:00}";
        }
        
        // Update current turn
        if (currentTurnText != null)
        {
            currentTurnText.text = $"Turn: {gameManager.SideToMove}";
            
            // Color the text based on side
            if (gameManager.SideToMove == Side.White)
            {
                currentTurnText.color = Color.white;
            }
            else
            {
                currentTurnText.color = new Color(0.4f, 0.4f, 0.4f); // Dark gray for black
            }
        }
        
        // Update captured pieces
        if (capturedPiecesText != null)
        {
            string whiteCapturedText = FormatCapturedPieces(Side.White);
            string blackCapturedText = FormatCapturedPieces(Side.Black);
            
            capturedPiecesText.text = $"White captured: {whiteCapturedText}\nBlack captured: {blackCapturedText}";
        }
    }
    
    private string FormatCapturedPieces(Side side)
    {
        Dictionary<System.Type, int> pieceCounts = new Dictionary<System.Type, int>();
        
        // Count pieces by type
        foreach (Piece piece in capturedPieces)
        {
            if (piece.Owner == side)
            {
                System.Type pieceType = piece.GetType();
                if (!pieceCounts.ContainsKey(pieceType))
                {
                    pieceCounts[pieceType] = 0;
                }
                pieceCounts[pieceType]++;
            }
        }
        
        // Format the text
        string result = "";
        foreach (var kvp in pieceCounts)
        {
            if (!string.IsNullOrEmpty(result))
            {
                result += ", ";
            }
            
            string pieceName = kvp.Key.Name;
            int count = kvp.Value;
            result += $"{pieceName}:{count}";
        }
        
        return string.IsNullOrEmpty(result) ? "None" : result;
    }
    
    public void ToggleStatsPanel()
    {
        if (statsPanel != null)
        {
            statsPanel.SetActive(!statsPanel.activeSelf);
            
            // Update stats immediately when showing panel
            if (statsPanel.activeSelf)
            {
                UpdateStats();
            }
        }
    }
}