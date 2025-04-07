using System.Collections.Generic;
using UnityEngine;
using Firebase.Firestore;
using Firebase.Auth;
using System.Threading.Tasks;

public class DLCStoreUI : MonoBehaviour
{
    public GameObject skinItemPrefab;
    public Transform contentParent;

    private List<SkinData> availableSkins = new();
    private FirebaseAuth auth;
    private FirebaseFirestore db;

    private async void Start()
    {
        auth = FirebaseAuth.DefaultInstance;
        db = FirebaseFirestore.DefaultInstance;

        await LoadAvailableSkins();
        PopulateStoreUI();
    }

    private async Task LoadAvailableSkins()
    {
        var skinsSnapshot = await db.Collection("skins").GetSnapshotAsync();

        foreach (var doc in skinsSnapshot.Documents)
        {
            availableSkins.Add(new SkinData
            {
                skinId = doc.Id,
                name = doc.GetValue<string>("name"),
                price = doc.GetValue<int>("price"),
                imageUrl = doc.GetValue<string>("imageUrl")
            });
        }
    }

    private void PopulateStoreUI()
    {
        foreach (var skin in availableSkins)
        {
            var item = Instantiate(skinItemPrefab, contentParent);
            item.GetComponent<SkinItemUI>().Initialize(skin);
        }
    }
}