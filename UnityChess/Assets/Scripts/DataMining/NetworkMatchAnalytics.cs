using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityChess;

/// <summary>
/// Tracks and logs analytics data for networked chess matches.
/// This should be attached to the same GameObject as ChessNetworkManager or added at runtime.
/// </summary>
public class NetworkMatchAnalytics : NetworkBehaviour
{
    // Game session data
    private string matchId;
    private DateTime matchStartTime;
    private Side localPlayerSide;
    private Side winnerSide;
    private bool matchEnded = false;
    
    // Game statistics
    private int totalMoves = 0;
    private string openingSequence = "";
    private float totalThinkingTime = 0f;
    private int captureMoves = 0;
    private int checkMoves = 0;
    private string endGameCondition = "";
    
    // Connection data
    private bool wasDisconnected = false;
    private int disconnectionCount = 0;
    private float totalDisconnectionTime = 0f;
    
    // References
    private GameManager gameManager;
    private ChessNetworkManager networkManager;
    private FirebaseManager firebaseManager;
    
    // Cache the last move timestamp for thinking time calculation
    private float lastMoveTime;
    
    // Networked stats tracking
    private NetworkVariable<int> networkTotalMoves = new NetworkVariable<int>(0);
    private NetworkVariable<int> networkCaptureMoves = new NetworkVariable<int>(0);
    private NetworkVariable<int> networkCheckMoves = new NetworkVariable<int>(0);
    
    private void Awake()
    {
        // Get references
        gameManager = FindObjectOfType<GameManager>();
        networkManager = FindObjectOfType<ChessNetworkManager>();
        firebaseManager = FindObjectOfType<FirebaseManager>();
    }
    
    private void Start()
    {
        // Subscribe to events
        GameManager.NewGameStartedEvent += OnGameStarted;
        GameManager.GameEndedEvent += OnGameEnded;
        GameManager.MoveExecutedEvent += OnMoveExecuted;
        
        // Network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Get the local player's side
        if (networkManager != null)
        {
            localPlayerSide = networkManager.GetLocalPlayerSide();
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        GameManager.NewGameStartedEvent -= OnGameStarted;
        GameManager.GameEndedEvent -= OnGameEnded;
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
        
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }
    
    private void OnGameStarted()
    {
        // Reset match data
        matchId = GenerateMatchId();
        matchStartTime = DateTime.Now;
        matchEnded = false;
        totalMoves = 0;
        openingSequence = "";
        totalThinkingTime = 0f;
        captureMoves = 0;
        checkMoves = 0;
        disconnectionCount = 0;
        totalDisconnectionTime = 0f;
        lastMoveTime = Time.time;
        
        // Reset networked variables
        if (IsHost)
        {
            networkTotalMoves.Value = 0;
            networkCaptureMoves.Value = 0;
            networkCheckMoves.Value = 0;
        }
        
        Debug.Log($"Match started with ID: {matchId}");
        
        // Log match start to Firebase
        if (firebaseManager != null && firebaseManager.IsInitialized)
        {
            LogMatchStart();
        }
    }
    
    private void OnGameEnded()
    {
        if (matchEnded) return;
        
        matchEnded = true;
        
        // Determine the winner
        DetermineMatchResult();
        
        Debug.Log($"Match ended. Winner: {(winnerSide == Side.None ? "Draw" : winnerSide.ToString())}");
        
        // Log match end to Firebase
        if (firebaseManager != null && firebaseManager.IsInitialized)
        {
            LogMatchEnd();
        }
    }
    
    private void OnMoveExecuted()
    {
        // Calculate thinking time
        float thinkingTime = Time.time - lastMoveTime;
        totalThinkingTime += thinkingTime;
        lastMoveTime = Time.time;
        
        // Get the last move
        if (gameManager.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove))
        {
            // Increment move counter
            totalMoves++;
            
            // Track move data
            if (totalMoves <= 6) // Track first 3 moves from each side
            {
                if (!string.IsNullOrEmpty(openingSequence))
                    openingSequence += ", ";
                
                openingSequence += latestHalfMove.ToString();
            }
            
            // Track capture moves
            if (latestHalfMove.CapturedPiece != null)
            {
                captureMoves++;
                if (IsHost)
                {
                    networkCaptureMoves.Value = captureMoves;
                }
            }
            
            // Track check moves
            if (latestHalfMove.CausedCheck)
            {
                checkMoves++;
                if (IsHost)
                {
                    networkCheckMoves.Value = checkMoves;
                }
            }
            
            // Update networked stats
            if (IsHost)
            {
                networkTotalMoves.Value = totalMoves;
            }
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            // Local player disconnected
            wasDisconnected = true;
            disconnectionCount++;
            
            // Log the disconnection event
            LogDisconnection();
        }
    }
    
    private void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId && wasDisconnected)
        {
            // Local player reconnected
            wasDisconnected = false;
            
            // Log the reconnection event
            LogReconnection();
        }
    }
    
    private void DetermineMatchResult()
    {
        // Get the latest half-move
        if (gameManager.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove))
        {
            if (latestHalfMove.CausedCheckmate)
            {
                // The side that didn't make the last move won by checkmate
                winnerSide = gameManager.SideToMove == Side.White ? Side.Black : Side.White;
                endGameCondition = "Checkmate";
            }
            else if (latestHalfMove.CausedStalemate)
            {
                winnerSide = Side.None;
                endGameCondition = "Stalemate";
            }
            else
            {
                // Default to no winner (draw or resignation)
                winnerSide = Side.None;
                endGameCondition = "Other";
            }
        }
    }
    
    private string GenerateMatchId()
    {
        return System.Guid.NewGuid().ToString().Substring(0, 8);
    }
    
    #region Firebase Logging
    
    private void LogMatchStart()
    {
        Dictionary<string, object> matchData = new Dictionary<string, object>
        {
            { "matchId", matchId },
            { "startTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
            { "playerSide", localPlayerSide.ToString() },
            { "isNetworked", true }
        };
        
        // Log to Firebase Analytics
        firebaseManager.LogGameStartEvent(matchId);
        
        // Store match data in Firebase Database
        var database = firebaseManager.Database;
        if (database != null)
        {
            database.Child("matches").Child(matchId).SetValueAsync(matchData);
            Debug.Log("Match start logged to Firebase");
        }
    }
    
    private void LogMatchEnd()
    {
        // Calculate match duration
        TimeSpan duration = DateTime.Now - matchStartTime;
        
        Dictionary<string, object> matchData = new Dictionary<string, object>
        {
            { "endTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
            { "duration", duration.TotalSeconds },
            { "totalMoves", totalMoves },
            { "winnerSide", winnerSide.ToString() },
            { "endGameCondition", endGameCondition },
            { "openingSequence", openingSequence },
            { "captureMoves", captureMoves },
            { "checkMoves", checkMoves },
            { "disconnectionCount", disconnectionCount },
            { "totalDisconnectionTime", totalDisconnectionTime }
        };
        
        // Log to Firebase Analytics
        firebaseManager.LogGameEndEvent(matchId, endGameCondition, totalMoves, duration.TotalSeconds);
        
        // Update match data in Firebase Database
        var database = firebaseManager.Database;
        if (database != null)
        {
            database.Child("matches").Child(matchId).UpdateChildrenAsync(matchData);
            Debug.Log("Match end logged to Firebase");
        }
    }
    
    private void LogDisconnection()
    {
        Dictionary<string, object> eventData = new Dictionary<string, object>
        {
            { "matchId", matchId },
            { "timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
            { "event", "disconnection" },
            { "playerSide", localPlayerSide.ToString() },
            { "moveCount", totalMoves }
        };
        
        // Store event in Firebase Database
        var database = firebaseManager.Database;
        if (database != null)
        {
            string eventId = System.Guid.NewGuid().ToString().Substring(0, 8);
            database.Child("matchEvents").Child(matchId).Child(eventId).SetValueAsync(eventData);
            Debug.Log("Disconnection logged to Firebase");
        }
    }
    
    private void LogReconnection()
    {
        Dictionary<string, object> eventData = new Dictionary<string, object>
        {
            { "matchId", matchId },
            { "timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
            { "event", "reconnection" },
            { "playerSide", localPlayerSide.ToString() },
            { "moveCount", totalMoves }
        };
        
        // Store event in Firebase Database
        var database = firebaseManager.Database;
        if (database != null)
        {
            string eventId = System.Guid.NewGuid().ToString().Substring(0, 8);
            database.Child("matchEvents").Child(matchId).Child(eventId).SetValueAsync(eventData);
            Debug.Log("Reconnection logged to Firebase");
        }
    }
    
    #endregion
}