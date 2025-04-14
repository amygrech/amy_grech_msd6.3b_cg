using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;

public class AnalyticsDashboard : MonoBehaviour
{
    public TextMeshProUGUI topDLCsText;
    public TextMeshProUGUI winLossText;

    void Start()
    {
        RefreshDashboard();
    }

    public void RefreshDashboard()
    {
        FirebaseManager.Instance.Database.Child("purchases").GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted)
            {
                Dictionary<string, int> avatarCounts = new Dictionary<string, int>();

                foreach (var child in task.Result.Children)
                {
                    string avatarId = child.Child("avatarId").Value.ToString();
                    if (avatarCounts.ContainsKey(avatarId))
                        avatarCounts[avatarId]++;
                    else
                        avatarCounts[avatarId] = 1;
                }

                topDLCsText.text = "Top Purchased DLCs:\n";
                foreach (var kvp in avatarCounts)
                {
                    topDLCsText.text += $"{kvp.Key}: {kvp.Value} purchases\n";
                }
            }
        });

        FirebaseManager.Instance.Database.Child("games").GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted)
            {
                int wins = 0, losses = 0;

                foreach (var child in task.Result.Children)
                {
                    string result = child.Child("result").Value?.ToString() ?? "";
                    if (result == "win") wins++;
                    else if (result == "loss") losses++;
                }

                winLossText.text = $"Win/Loss Ratio: {wins} / {losses}";
            }
        });
    }
}