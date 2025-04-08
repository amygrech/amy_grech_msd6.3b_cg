using Unity.Netcode;
using UnityChess;
using UnityEngine;

public class NetworkTurnManager : NetworkBehaviour {
    // Network variable to track whose turn it is (0 for White, 1 for Black)
    // Making this public allows direct inspection in the Unity Editor
    public NetworkVariable<int> currentTurn = new NetworkVariable<int>(0, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server);
    
    // Network variable to control if movement is allowed
    public NetworkVariable<bool> canMove = new NetworkVariable<bool>(true,
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server);
        
    // Make sure we have a reference to the ChessNetworkManager
    private ChessNetworkManager networkManager;

    [SerializeField] private bool debugMode = true;

    private void Awake() {
        Debug.Log("[NetworkTurnManager] Awake called");
        networkManager = FindObjectOfType<ChessNetworkManager>();
        
        if (networkManager == null) {
            Debug.LogError("[NetworkTurnManager] Could not find ChessNetworkManager in the scene!");
        }
    }

    public override void OnNetworkSpawn() {
        base.OnNetworkSpawn();
        
        Debug.Log("[NetworkTurnManager] OnNetworkSpawn called. IsServer: " + (IsServer || IsHost));
        
        if (IsServer || IsHost) {
            Debug.Log("[NetworkTurnManager] Initializing on server: White's turn");
            currentTurn.Value = 0; // White starts
            canMove.Value = true;
        }
        
        // Subscribe to NetworkVariable changes
        currentTurn.OnValueChanged += OnTurnChanged;
        canMove.OnValueChanged += OnCanMoveChanged;
        
        // Force initial refresh
        if (networkManager != null) {
            networkManager.RefreshAllPiecesInteractivity();
        }
    }
    
    public override void OnNetworkDespawn() {
        base.OnNetworkDespawn();
        
        // Unsubscribe from events
        currentTurn.OnValueChanged -= OnTurnChanged;
        canMove.OnValueChanged -= OnCanMoveChanged;
    }
    
    private void OnTurnChanged(int previousValue, int newValue) {
        Debug.Log($"[NetworkTurnManager] Turn changed from {(previousValue == 0 ? "White" : "Black")} to {(newValue == 0 ? "White" : "Black")}");
        
        // Update piece interactivity whenever turn changes
        if (networkManager != null) {
            networkManager.RefreshAllPiecesInteractivity();
        }
    }
    
    private void OnCanMoveChanged(bool previousValue, bool newValue) {
        Debug.Log($"[NetworkTurnManager] CanMove changed from {previousValue} to {newValue}");
        
        // Update piece interactivity whenever movement permissions change
        if (networkManager != null) {
            networkManager.RefreshAllPiecesInteractivity();
        }
    }

    // This is the critical method that determines if a player can move
    public bool CanPlayerMove(Side playerSide) {
        if (!canMove.Value) {
            if (debugMode) Debug.Log($"[NetworkTurnManager] Movement locked for all players");
            return false;
        }
        
        bool isPlayersTurn = (currentTurn.Value == 0 && playerSide == Side.White) ||
                             (currentTurn.Value == 1 && playerSide == Side.Black);
                             
        if (debugMode) Debug.Log($"[NetworkTurnManager] Turn check: {playerSide} can move: {isPlayersTurn} (Current turn: {(currentTurn.Value == 0 ? "White" : "Black")})");
        return isPlayersTurn;
    }

    // Lock movement during transitions or game end
    public void LockMovement() {
        if (IsServer || IsHost) {
            Debug.Log("[NetworkTurnManager] Movement has been locked by host/server");
            canMove.Value = false;
        } else {
            LockMovementServerRpc();
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void LockMovementServerRpc() {
        Debug.Log("[NetworkTurnManager] Movement has been locked via ServerRpc");
        canMove.Value = false;
    }

    // Unlock movement (usually after state sync)
    public void UnlockMovement() {
        if (IsServer || IsHost) {
            Debug.Log("[NetworkTurnManager] Movement has been unlocked by host/server");
            canMove.Value = true;
        } else {
            UnlockMovementServerRpc();
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void UnlockMovementServerRpc() {
        Debug.Log("[NetworkTurnManager] Movement has been unlocked via ServerRpc");
        canMove.Value = true;
    }

    // End the current turn and switch to the other player
    public void EndTurn() {
        Debug.Log("[NetworkTurnManager] EndTurn called. IsServer: " + (IsServer || IsHost));
        
        if (IsServer || IsHost) {
            // Direct server-side call
            ChangeCurrentTurn();
        } else {
            // Client call via RPC
            EndTurnServerRpc();
        }
    }
    
    // Server-side implementation of turn change
    private void ChangeCurrentTurn() {
        Debug.Log($"[NetworkTurnManager] Changing turn: {(currentTurn.Value == 0 ? "White" : "Black")} -> {(currentTurn.Value == 0 ? "Black" : "White")}");
        
        // Toggle turn
        currentTurn.Value = currentTurn.Value == 0 ? 1 : 0;
        canMove.Value = true;
        
        // Notify clients
        UpdateTurnStateClientRpc(currentTurn.Value);
    }

    [ServerRpc(RequireOwnership = false)]
    public void EndTurnServerRpc() {
        Debug.Log("[NetworkTurnManager] EndTurnServerRpc called");
        if (!IsServer && !IsHost) return;
        
        ChangeCurrentTurn();
    }

    [ClientRpc]
    private void UpdateTurnStateClientRpc(int newTurn) {
        // Update UI or game state based on new turn
        string currentPlayer = newTurn == 0 ? "White" : "Black";
        Debug.Log($"[NetworkTurnManager] Turn changed to: {currentPlayer}");
        
        // Always force refresh piece interactivity 
        if (networkManager != null) {
            networkManager.RefreshAllPiecesInteractivity();
        } else {
            Debug.LogError("[NetworkTurnManager] ChessNetworkManager reference is null!");
            // Try finding it again if reference was lost
            networkManager = FindObjectOfType<ChessNetworkManager>();
            if (networkManager != null) {
                networkManager.RefreshAllPiecesInteractivity();
            }
        }
    }
    
    // Force-refresh all pieces (can be called from anywhere)
    [ClientRpc]
    public void ForceRefreshPiecesClientRpc() {
        Debug.Log("[NetworkTurnManager] ForceRefreshPiecesClientRpc called");
        if (networkManager != null) {
            networkManager.RefreshAllPiecesInteractivity();
        }
    }
}