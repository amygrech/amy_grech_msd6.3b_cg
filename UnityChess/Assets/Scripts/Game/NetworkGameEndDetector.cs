using UnityEngine;
using Unity.Netcode;
using UnityChess;
using System.Collections;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Detects and handles game-end conditions (checkmate, stalemate, resignation)
/// in a networked chess game. Ensures proper synchronization across clients.
/// </summary>
public class NetworkGameEndDetector : NetworkBehaviour
{
    [Header("Game References")]
    [SerializeField] private ImprovedTurnSystem turnSystem;
    [SerializeField] private BoardSynchronizer boardSynchronizer;
    
    [Header("UI Elements")]
    [SerializeField] private GameObject gameEndPanel;
    [SerializeField] private TextMeshProUGUI gameEndMessageText;
    [SerializeField] private Button resignButton;
    [SerializeField] private Button rematchButton;
    [SerializeField] private Button returnToLobbyButton;
    
    // Network variable to track game end state
    public NetworkVariable<GameEndState> gameEndState = new NetworkVariable<GameEndState>(
        new GameEndState { 
            IsGameOver = false, 
            WinningSide = -1, 
            EndReason = GameEndReason.None 
        },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    // Game end state structure
    public struct GameEndState : INetworkSerializable
    {
        public bool IsGameOver;
        public int WinningSide; // -1 = none, 0 = white, 1 = black
        public GameEndReason EndReason;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref IsGameOver);
            serializer.SerializeValue(ref WinningSide);
            serializer.SerializeValue(ref EndReason);
        }
    }
    
    // Enum to represent different end game reasons
    public enum GameEndReason
    {
        None,
        Checkmate,
        Stalemate,
        Resignation,
        Timeout,
        Disconnection
    }
    
    private bool isMonitoring = false;
    private float checkInterval = 0.5f; // How often to check for game end conditions
    
    private void Awake()
    {
        // Find references if not set in inspector
        if (turnSystem == null)
            turnSystem = FindObjectOfType<ImprovedTurnSystem>();
            
        if (boardSynchronizer == null)
            boardSynchronizer = FindObjectOfType<BoardSynchronizer>();
            
        // Ensure game end panel is initially hidden
        if (gameEndPanel != null)
            gameEndPanel.SetActive(false);
            
        // Set up button listeners
        if (resignButton != null)
            resignButton.onClick.AddListener(OnResignButtonClicked);
            
        if (rematchButton != null)
            rematchButton.onClick.AddListener(OnRematchButtonClicked);
            
        if (returnToLobbyButton != null)
            returnToLobbyButton.onClick.AddListener(OnReturnToLobbyButtonClicked);
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Subscribe to game state changes
        GameManager.MoveExecutedEvent += OnMoveExecuted;
        
        // Subscribe to network variable changes
        gameEndState.OnValueChanged += OnGameEndStateChanged;
        
        // Start monitoring for game end conditions
        if (IsServer || IsHost)
        {
            isMonitoring = true;
            StartCoroutine(MonitorGameEndConditions());
        }
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        
        // Unsubscribe from events
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
        gameEndState.OnValueChanged -= OnGameEndStateChanged;
        
        // Stop monitoring
        isMonitoring = false;
        StopAllCoroutines();
    }
    
    /// <summary>
    /// Continuously monitors for game end conditions
    /// </summary>
    private IEnumerator MonitorGameEndConditions()
    {
        // Wait for game to fully initialize
        yield return new WaitForSeconds(1.0f);
        
        Debug.Log("[NetworkGameEndDetector] Started monitoring for game end conditions");
        
        while (isMonitoring)
        {
            // Only check if game is not already over
            if (!gameEndState.Value.IsGameOver)
            {
                CheckForGameEndConditions();
            }
            
            yield return new WaitForSeconds(checkInterval);
        }
    }
    
    /// <summary>
    /// Called after each move execution to check for end conditions immediately
    /// </summary>
    private void OnMoveExecuted()
    {
        if (IsServer || IsHost)
        {
            // Check for game end conditions
            CheckForGameEndConditions();
        }
    }
    
    /// <summary>
    /// Checks the current game state for end conditions (checkmate, stalemate)
    /// </summary>
    private void CheckForGameEndConditions()
    {
        HalfMove latestHalfMove = default;
        bool hasCurrent = GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out latestHalfMove);
    
        if (!hasCurrent)
            return;
        
        Side currentSide = GameManager.Instance.SideToMove;
        Side previousSide = currentSide.Complement();
    
        GameEndState newState = gameEndState.Value;
    
        // Check for checkmate
        if (latestHalfMove.CausedCheckmate)
        {
            Debug.Log($"[NetworkGameEndDetector] Checkmate detected! Winner: {previousSide}");
        
            newState.IsGameOver = true;
            newState.EndReason = GameEndReason.Checkmate;
            newState.WinningSide = previousSide == Side.White ? 0 : 1;
        
            gameEndState.Value = newState;
        
            // Notify all clients about game end
            NotifyGameEndClientRpc(newState.WinningSide, (int)GameEndReason.Checkmate);
        }
        // Check for stalemate
        else if (latestHalfMove.CausedStalemate)
        {
            Debug.Log("[NetworkGameEndDetector] Stalemate detected!");
        
            newState.IsGameOver = true;
            newState.EndReason = GameEndReason.Stalemate;
            newState.WinningSide = -1; // No winner in stalemate
        
            gameEndState.Value = newState;
        
            // Notify all clients about game end
            NotifyGameEndClientRpc(-1, (int)GameEndReason.Stalemate);
        }
    }
    
    /// <summary>
    /// Handles changes to the game end state network variable
    /// </summary>
    private void OnGameEndStateChanged(GameEndState previousValue, GameEndState newValue)
    {
        if (newValue.IsGameOver && !previousValue.IsGameOver)
        {
            // Game just ended, update UI
            DisplayGameEndMessage(newValue.WinningSide, newValue.EndReason);
            
            // Disable all piece movement
            LockGameInteraction();
        }
    }
    
    /// <summary>
    /// Locks all game interaction after the game ends
    /// </summary>
    private void LockGameInteraction()
    {
        // Use turn system to lock all piece interactivity
        if (turnSystem != null)
        {
            turnSystem.lockInteractivity.Value = true;
        }
        
        // Disable all visual pieces
        BoardManager.Instance.SetActiveAllPieces(false);
    }
    
    /// <summary>
    /// Displays the appropriate game end message based on the end reason
    /// </summary>
    private void DisplayGameEndMessage(int winningSide, GameEndReason endReason)
    {
        if (gameEndPanel == null || gameEndMessageText == null)
            return;
            
        string message = "";
        
        switch (endReason)
        {
            case GameEndReason.Checkmate:
                string winner = winningSide == 0 ? "White" : "Black";
                message = $"Checkmate! {winner} wins the game.";
                break;
                
            case GameEndReason.Stalemate:
                message = "Stalemate! The game is a draw.";
                break;
                
            case GameEndReason.Resignation:
                string resignedSide = winningSide == 0 ? "Black" : "White";
                string winningSideName = winningSide == 0 ? "White" : "Black";
                message = $"{resignedSide} resigned. {winningSideName} wins the game.";
                break;
                
            case GameEndReason.Timeout:
                string timeoutSide = winningSide == 0 ? "Black" : "White";
                string timeoutWinner = winningSide == 0 ? "White" : "Black";
                message = $"{timeoutSide} ran out of time. {timeoutWinner} wins the game.";
                break;
                
            case GameEndReason.Disconnection:
                string disconnectedSide = winningSide == 0 ? "Black" : "White";
                string disconnectionWinner = winningSide == 0 ? "White" : "Black";
                message = $"{disconnectedSide} disconnected. {disconnectionWinner} wins the game.";
                break;
                
            default:
                message = "Game Over";
                break;
        }
        
        // Update UI text
        gameEndMessageText.text = message;
        
        // Show game end panel
        gameEndPanel.SetActive(true);
        
        Debug.Log($"[NetworkGameEndDetector] Game end message displayed: {message}");
    }
    
    /// <summary>
    /// Handles resignation button click
    /// </summary>
    private void OnResignButtonClicked()
    {
        // Get the local player's side
        Side localPlayerSide = ChessNetworkManager.Instance.GetLocalPlayerSide();
        int resigningSideInt = localPlayerSide == Side.White ? 0 : 1;
        
        Debug.Log($"[NetworkGameEndDetector] Player {localPlayerSide} is resigning");
        
        // Request resignation on the server
        RequestResignationServerRpc(resigningSideInt);
    }
    
    /// <summary>
    /// Handles rematch button click
    /// </summary>
    private void OnRematchButtonClicked()
    {
        // Request rematch on the server
        RequestRematchServerRpc();
    }
    
    /// <summary>
    /// Handles return to lobby button click
    /// </summary>
    private void OnReturnToLobbyButtonClicked()
    {
        // Disconnect from the current session
        if (ChessNetworkManager.Instance != null)
        {
            ChessNetworkManager.Instance.Disconnect();
        }
    }
    
    /// <summary>
    /// Server RPC for client to request resignation
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestResignationServerRpc(int resigningSide)
    {
        if (!IsServer && !IsHost)
            return;
            
        if (gameEndState.Value.IsGameOver)
            return; // Game already over
            
        Debug.Log($"[SERVER] Player {resigningSide} has resigned");
        
        // Set winner as the opposite side
        int winningSide = resigningSide == 0 ? 1 : 0;
        
        // Update the game end state
        GameEndState newState = new GameEndState
        {
            IsGameOver = true,
            WinningSide = winningSide,
            EndReason = GameEndReason.Resignation
        };
        
        gameEndState.Value = newState;
        
        // Notify all clients
        NotifyGameEndClientRpc(winningSide, (int)GameEndReason.Resignation);
    }
    
    /// <summary>
    /// Server RPC for client to request a rematch
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestRematchServerRpc()
    {
        if (!IsServer && !IsHost)
            return;
            
        Debug.Log("[SERVER] Rematch requested");
        
        // Reset the game end state
        GameEndState newState = new GameEndState
        {
            IsGameOver = false,
            WinningSide = -1,
            EndReason = GameEndReason.None
        };
        
        gameEndState.Value = newState;
        
        // Start a new game
        GameManager.Instance.StartNewGame();
        
        // Tell clients to reset their game state
        ResetGameClientRpc();
    }
    
    /// <summary>
    /// Client RPC to notify all clients about game end
    /// </summary>
    [ClientRpc]
    public void NotifyGameEndClientRpc(int winningSide, int endReasonInt)
    {
        GameEndReason endReason = (GameEndReason)endReasonInt;
        
        Debug.Log($"[CLIENT] Game ended - Winner: {(winningSide == 0 ? "White" : winningSide == 1 ? "Black" : "None")}, " +
                 $"Reason: {endReason}");
        
        // Update local UI
        DisplayGameEndMessage(winningSide, endReason);
        
        // Lock game interaction
        LockGameInteraction();
    }
    
    /// <summary>
    /// Client RPC to reset the game for a rematch
    /// </summary>
    [ClientRpc]
    public void ResetGameClientRpc()
    {
        Debug.Log("[CLIENT] Resetting game for rematch");
        
        // Hide the game end panel
        if (gameEndPanel != null)
            gameEndPanel.SetActive(false);
            
        // Refresh all pieces interactivity
        if (ChessNetworkManager.Instance != null)
            ChessNetworkManager.Instance.RefreshAllPiecesInteractivity();
            
        // Reset turn system lock
        if (turnSystem != null)
            turnSystem.lockInteractivity.Value = false;
    }
    
    /// <summary>
    /// Reports a player disconnection which may end the game
    /// </summary>
    public void ReportPlayerDisconnection(Side disconnectedSide)
    {
        if (!IsServer && !IsHost)
        {
            // Send request to server
            int disconnectedSideInt = disconnectedSide == Side.White ? 0 : 1;
            ReportDisconnectionServerRpc(disconnectedSideInt);
            return;
        }
        
        // Server implementation
        HandlePlayerDisconnection(disconnectedSide);
    }
    
    /// <summary>
    /// Server RPC to report a player disconnection
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ReportDisconnectionServerRpc(int disconnectedSide)
    {
        if (!IsServer && !IsHost)
            return;
            
        Side playerSide = disconnectedSide == 0 ? Side.White : Side.Black;
        HandlePlayerDisconnection(playerSide);
    }
    
    /// <summary>
    /// Handles player disconnection on the server
    /// </summary>
    private void HandlePlayerDisconnection(Side disconnectedSide)
    {
        if (gameEndState.Value.IsGameOver)
            return; // Game already over
            
        Debug.Log($"[SERVER] Player {disconnectedSide} has disconnected");
        
        // Set winner as the opposite side
        int winningSide = disconnectedSide == Side.White ? 1 : 0;
        
        // Update the game end state
        GameEndState newState = new GameEndState
        {
            IsGameOver = true,
            WinningSide = winningSide,
            EndReason = GameEndReason.Disconnection
        };
        
        gameEndState.Value = newState;
        
        // Notify all clients
        NotifyGameEndClientRpc(winningSide, (int)GameEndReason.Disconnection);
    }
    
    /// <summary>
    /// Public method to check if the game is over
    /// </summary>
    public bool IsGameOver()
    {
        return gameEndState.Value.IsGameOver;
    }
    
    /// <summary>
    /// Gets the winning side of the game
    /// </summary>
    public Side GetWinningSide()
    {
        int winningSide = gameEndState.Value.WinningSide;
        
        if (winningSide == 0)
            return Side.White;
        else if (winningSide == 1)
            return Side.Black;
        else
            return Side.None;
    }
    
    /// <summary>
    /// Gets the reason why the game ended
    /// </summary>
    public GameEndReason GetGameEndReason()
    {
        return gameEndState.Value.EndReason;
    }
}