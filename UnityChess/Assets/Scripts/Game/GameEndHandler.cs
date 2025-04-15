using UnityEngine;
using Unity.Netcode;
using UnityChess;
using System.Collections;

/// <summary>
/// Handles game end conditions (checkmate, stalemate, resignation) in a networked chess game.
/// Detects game-ending states, broadcasts them to all clients, and ensures graceful session termination.
/// </summary>
public class GameEndHandler : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private ImprovedTurnSystem turnSystem;
    [SerializeField] private BoardSynchronizer boardSynchronizer;
    [SerializeField] private NetworkGameEndDetector gameEndDetector; // Optional: use if already in scene
    
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
        Resignation
    }
    
    private bool isMonitoring = false;
    private float checkInterval = 0.5f; // How often to check for game end conditions
    
    void Awake()
    {
        // Find references if not set in inspector
        if (turnSystem == null)
            turnSystem = FindObjectOfType<ImprovedTurnSystem>();
            
        if (boardSynchronizer == null)
            boardSynchronizer = FindObjectOfType<BoardSynchronizer>();
            
        if (gameEndDetector == null)
            gameEndDetector = FindObjectOfType<NetworkGameEndDetector>();
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
        
        Debug.Log("[GameEndHandler] Started monitoring for game end conditions");
        
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
        // Skip if we're not on server or if game is already over
        if ((!IsServer && !IsHost) || gameEndState.Value.IsGameOver)
            return;
            
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
            Debug.Log($"[SERVER] Checkmate detected! Winner: {previousSide}");
        
            newState.IsGameOver = true;
            newState.EndReason = GameEndReason.Checkmate;
            newState.WinningSide = previousSide == Side.White ? 0 : 1;
        
            gameEndState.Value = newState;
        
            // Notify all clients about game end
            NotifyGameEndClientRpc(newState.WinningSide, (int)GameEndReason.Checkmate);
            
            // Lock game interaction
            LockGameInteraction();
        }
        // Check for stalemate
        else if (latestHalfMove.CausedStalemate)
        {
            Debug.Log("[SERVER] Stalemate detected!");
        
            newState.IsGameOver = true;
            newState.EndReason = GameEndReason.Stalemate;
            newState.WinningSide = -1; // No winner in stalemate
        
            gameEndState.Value = newState;
        
            // Notify all clients about game end
            NotifyGameEndClientRpc(-1, (int)GameEndReason.Stalemate);
            
            // Lock game interaction
            LockGameInteraction();
        }
    }
    
    /// <summary>
    /// Handles changes to the game end state network variable
    /// </summary>
    private void OnGameEndStateChanged(GameEndState previousValue, GameEndState newValue)
    {
        if (newValue.IsGameOver && !previousValue.IsGameOver)
        {
            // Game just ended
            string winner = newValue.WinningSide == 0 ? "White" : 
                           newValue.WinningSide == 1 ? "Black" : "None";
                           
            string reason = newValue.EndReason.ToString();
            
            Debug.Log($"[GAME END] Game over! Winner: {winner}, Reason: {reason}");
            
            // If we have the network end detector, let it handle UI
            if (gameEndDetector != null)
                return;
                
            // Otherwise lock interaction
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
    /// Allows a player to resign from the game
    /// </summary>
    public void ResignGame()
    {
        // Get the local player's side
        Side localPlayerSide = ChessNetworkManager.Instance.GetLocalPlayerSide();
        int resigningSideInt = localPlayerSide == Side.White ? 0 : 1;
        
        Debug.Log($"[GameEndHandler] Player {localPlayerSide} is resigning");
        
        // Request resignation on the server
        RequestResignationServerRpc(resigningSideInt);
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
        
        // Lock game interaction
        LockGameInteraction();
    }
    
    /// <summary>
    /// Client RPC to notify all clients about game end
    /// </summary>
    [ClientRpc]
    public void NotifyGameEndClientRpc(int winningSide, int endReasonInt)
    {
        GameEndReason endReason = (GameEndReason)endReasonInt;
        
        string winnerText = winningSide == 0 ? "White" : 
                          winningSide == 1 ? "Black" : "None";
                          
        Debug.Log($"[CLIENT] Game ended - Winner: {winnerText}, Reason: {endReason}");
        
        // Lock game interaction if we're not the host (host does this in server code)
        if (!IsHost && !IsServer)
        {
            LockGameInteraction();
        }
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