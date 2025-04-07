using Unity.Netcode;
using UnityChess;
using UnityEngine;

using Unity.Netcode;
using UnityChess;
using UnityEngine;

public class NetworkTurnManager : NetworkBehaviour {
    private NetworkVariable<int> currentTurn = new NetworkVariable<int>(0); // 0 for White, 1 for Black
    private NetworkVariable<bool> canMove = new NetworkVariable<bool>(true);

    private void Awake() {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening) {
            Debug.Log("Registering NetworkTurnManager with NetworkManager");
        }
    }

    public override void OnNetworkSpawn() {
        Debug.Log("NetworkTurnManager OnNetworkSpawn called");
        if (IsHost || IsServer) {
            Debug.Log("NetworkTurnManager initialized on host/server: White's turn");
            currentTurn.Value = 0; // White starts
            canMove.Value = true;
        }
    }

    public bool CanPlayerMove(Side playerSide) {
        if (!canMove.Value) {
            Debug.Log("Movement locked for all players");
            return false;
        }
        bool isPlayersTurn = (currentTurn.Value == 0 && playerSide == Side.White) ||
                             (currentTurn.Value == 1 && playerSide == Side.Black);
        Debug.Log($"Turn check: {playerSide} can move: {isPlayersTurn} (Current turn: {(currentTurn.Value == 0 ? "White" : "Black")})");
        return isPlayersTurn;
    }

    // *** New method to lock movement ***
    public void LockMovement() {
        canMove.Value = false;
        Debug.Log("Movement has been locked.");
    }
    

    // Use fallback mechanism if RPC fails
    public void EndTurn() {
        try {
            // Try to use the RPC if we can
            if (IsSpawned && (IsServer || IsHost)) {
                // Direct server-side call
                Debug.Log("Direct server-side turn change");
                ChangeCurrentTurn();
            } else if (IsSpawned) {
                // Client call via RPC
                Debug.Log("Client calling EndTurnServerRpc");
                EndTurnServerRpc();
            } else {
                Debug.LogWarning("NetworkTurnManager not spawned, using fallback mechanism");
                // Fallback for when network is not ready
                if (currentTurn.Value == 0) {
                    currentTurn.Value = 1;
                } else {
                    currentTurn.Value = 0;
                }
                canMove.Value = true;
                
                // Manually refresh pieces
                if (ChessNetworkManager.Instance != null) {
                    ChessNetworkManager.Instance.RefreshAllPiecesInteractivity();
                }
            }
        } catch (System.Exception e) {
            Debug.LogError($"Error in EndTurn: {e.Message}\n{e.StackTrace}");
            // Fallback mechanism
            if (currentTurn.Value == 0) {
                currentTurn.Value = 1;
            } else {
                currentTurn.Value = 0;
            }
            canMove.Value = true;
            
            // Manually refresh pieces
            if (ChessNetworkManager.Instance != null) {
                ChessNetworkManager.Instance.RefreshAllPiecesInteractivity();
            }
        }
    }
    
    // Server-side implementation of turn change
    private void ChangeCurrentTurn() {
        Debug.Log($"Changing turn: {(currentTurn.Value == 0 ? "White" : "Black")} -> {(currentTurn.Value == 0 ? "Black" : "White")}");
        
        // Toggle turn
        currentTurn.Value = currentTurn.Value == 0 ? 1 : 0;
        canMove.Value = true;
        
        // Notify clients
        UpdateTurnStateClientRpc(currentTurn.Value);
    }

    [ServerRpc(RequireOwnership = false)]
    public void EndTurnServerRpc() {
        Debug.Log("EndTurnServerRpc called");
        if (!IsServer && !IsHost) return;
        
        ChangeCurrentTurn();
    }

    [ClientRpc]
    private void UpdateTurnStateClientRpc(int newTurn) {
        // Update UI or game state based on new turn
        string currentPlayer = newTurn == 0 ? "White" : "Black";
        Debug.Log($"Turn changed to: {currentPlayer}");
        
        // Refresh piece interactivity based on the new turn
        if (ChessNetworkManager.Instance != null) {
            ChessNetworkManager.Instance.RefreshAllPiecesInteractivity();
        }
    }
}