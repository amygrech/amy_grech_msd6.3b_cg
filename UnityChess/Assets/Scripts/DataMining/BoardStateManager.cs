using System;
using System.Collections.Generic;
using UnityEngine;
using UnityChess;

/// <summary>
/// This class bridges the existing chess game with the network/Firebase save functionality
/// </summary>
public class BoardStateManager : MonoBehaviour
{
    [SerializeField] private GameSessionSyncManager syncManager;
    
    private GameManager gameManager;
    private BoardManager boardManager;

    private void Start()
    {
        gameManager = GameManager.Instance;
        boardManager = BoardManager.Instance;
        
        // Ensure we have the reference to the sync manager
        if (syncManager == null)
        {
            syncManager = FindObjectOfType<GameSessionSyncManager>();
            if (syncManager == null)
            {
                Debug.LogError("GameSessionSyncManager not found in the scene!");
            }
        }
    }

    /// <summary>
    /// Captures the current state of the chess board and saves it to Firebase
    /// </summary>
    public void SaveCurrentBoardState()
    {
        if (syncManager == null || !gameManager) 
        {
            Debug.LogError("Required components missing for saving the game state!");
            return;
        }
        
        List<ChessPieceState> pieceStates = new List<ChessPieceState>();
        
        // Get the current board from GameManager
        Board currentBoard = gameManager.CurrentBoard;
        
        // Go through all squares on the board and find pieces
        for (int file = 1; file <= 8; file++)
        {
            for (int rank = 1; rank <= 8; rank++)
            {
                Square square = new Square(file, rank);
                Piece piece = currentBoard[square];
                
                if (piece != null)
                {
                    ChessPieceState pieceState = new ChessPieceState
                    {
                        pieceType = piece.GetType().Name,
                        color = piece.Owner.ToString(),
                        position = square.ToString()
                    };
                    
                    pieceStates.Add(pieceState);
                }
            }
        }
        
        // Set the state in the sync manager
        syncManager.SetCurrentGameState(pieceStates);
        
        // Tell the sync manager to save the state
        syncManager.SaveSessionState();
    }

    /// <summary>
    /// Restores a chess board state from Firebase using the provided match ID
    /// </summary>
    /// <param name="matchId">The match ID to restore</param>
    public void LoadBoardState(string matchId)
    {
        if (syncManager == null) 
        {
            Debug.LogError("GameSessionSyncManager not found!");
            return;
        }
        
        // Ask the sync manager to restore the state
        syncManager.RestoreSessionState(matchId);
    }
}