using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Storage;

public class SkinManager : MonoBehaviour
{
    public static SkinManager Instance;

    private Dictionary<string, Sprite> cachedPreviews = new();

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public async Task<Sprite> DownloadSkinPreview(string url)
    {
        if (cachedPreviews.TryGetValue(url, out var cached))
            return cached;

        var storage = FirebaseStorage.DefaultInstance;
        var gsRef = storage.GetReferenceFromUrl(url);
        var bytes = await gsRef.GetBytesAsync(1 * 1024 * 1024);

        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(bytes);
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);

        cachedPreviews[url] = sprite;
        return sprite;
    }

    public async Task<Sprite> GetFullSkin(string skinId)
    {
        string path = Path.Combine(Application.persistentDataPath, $"{skinId}.png");

        if (File.Exists(path))
        {
            byte[] fileData = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(fileData);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
        }
        else
        {
            var storage = FirebaseStorage.DefaultInstance;
            var gsRef = storage.GetReference($"skins/{skinId}.png");
            var bytes = await gsRef.GetBytesAsync(1 * 1024 * 1024);

            File.WriteAllBytes(path, bytes);

            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
        }
    }
}