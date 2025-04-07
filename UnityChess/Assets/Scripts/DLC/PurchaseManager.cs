using System.Threading.Tasks;
using UnityEngine;
using Firebase.Auth;
using Firebase.Firestore;

public class PurchaseManager : MonoBehaviour
{
    public static PurchaseManager Instance;
    private FirebaseAuth auth;
    private FirebaseFirestore db;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        auth = FirebaseAuth.DefaultInstance;
        db = FirebaseFirestore.DefaultInstance;
    }

    public async void TryPurchaseSkin(SkinData skin)
    {
        string userId = auth.CurrentUser.UserId;
        var userDoc = db.Collection("users").Document(userId);

        var snapshot = await userDoc.GetSnapshotAsync();
        int coins = snapshot.ContainsField("coins") ? snapshot.GetValue<int>("coins") : 0;

        if (coins < skin.price)
        {
            Debug.Log("Not enough coins!");
            return;
        }

        // Deduct coins and save ownership
        await userDoc.UpdateAsync("coins", coins - skin.price);
        await userDoc.Collection("ownedSkins").Document(skin.skinId).SetAsync(new { owned = true });

        Debug.Log($"Purchased {skin.name}!");
    }

    public async Task<bool> HasSkin(string skinId)
    {
        string userId = auth.CurrentUser.UserId;
        var skinDoc = db.Collection("users").Document(userId)
            .Collection("ownedSkins").Document(skinId);

        var snapshot = await skinDoc.GetSnapshotAsync();
        return snapshot.Exists;
    }
}