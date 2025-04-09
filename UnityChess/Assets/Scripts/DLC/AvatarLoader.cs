using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Firebase.Storage;
using Firebase.Extensions;
using System.IO;

public class AvatarLoader : MonoBehaviour
{
    // Singleton instance
    public static AvatarLoader Instance { get; private set; }
    
    // Firebase Storage reference
    private FirebaseStorage storage;
    private StorageReference storageRoot;
    
    // Cache for downloaded avatars
    private Dictionary<string, Sprite> avatarCache = new Dictionary<string, Sprite>();
    
    // Directory for storing downloaded avatars locally
    private string localAvatarDirectory;
    
    private void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // Create local directory for storing avatars
        localAvatarDirectory = Path.Combine(Application.persistentDataPath, "Avatars");
        if (!Directory.Exists(localAvatarDirectory))
        {
            Directory.CreateDirectory(localAvatarDirectory);
        }
    }
    
    private void Start()
    {
        // Initialize Firebase Storage
        InitializeFirebaseStorage();
    }
    
    private void InitializeFirebaseStorage()
    {
        // Try to use existing FirebaseManager if available
        if (FirebaseManager.Instance != null && FirebaseManager.Instance.IsInitialized)
        {
            storage = FirebaseManager.Instance.Storage;
            storageRoot = FirebaseManager.Instance.StorageRoot;
            Debug.Log("Using FirebaseManager for Storage access");
        }
        else
        {
            // Fallback to direct initialization
            storage = FirebaseStorage.DefaultInstance;
            storageRoot = storage.GetReferenceFromUrl("gs://dlcstore-8ccb3.firebasestorage.app");
            Debug.Log("Directly initialized Firebase Storage");
        }
    }
    
    /// <summary>
    /// Load avatar from Firebase Storage and set it to an Image component
    /// </summary>
    public void LoadAvatar(string avatarPath, Image targetImage, Action onComplete = null, Action<Exception> onError = null)
    {
        StartCoroutine(LoadAvatarRoutine(avatarPath, targetImage, onComplete, onError));
    }
    
    private IEnumerator LoadAvatarRoutine(string avatarPath, Image targetImage, Action onComplete, Action<Exception> onError)
    {
        // First check if we have this avatar cached in memory
        if (avatarCache.TryGetValue(avatarPath, out Sprite cachedSprite))
        {
            targetImage.sprite = cachedSprite;
            onComplete?.Invoke();
            yield break;
        }
        
        // Check if we have it stored locally
        string localFilePath = Path.Combine(localAvatarDirectory, GetSafeFilename(avatarPath));
        if (File.Exists(localFilePath))
        {
            yield return LoadFromLocalFileRoutine(localFilePath, targetImage);
            onComplete?.Invoke();
            yield break;
        }
        
        // If not cached or stored locally, download from Firebase
        yield return DownloadFromFirebaseRoutine(avatarPath, localFilePath, targetImage, onComplete, onError);
    }
    
    private IEnumerator LoadFromLocalFileRoutine(string filePath, Image targetImage)
    {
        try
        {
            byte[] fileData = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(fileData);
            
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                Vector2.one * 0.5f
            );
            
            targetImage.sprite = sprite;
            
            // Cache in memory
            string relativePath = filePath.Replace(localAvatarDirectory + Path.DirectorySeparatorChar, "");
            avatarCache[relativePath] = sprite;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading local avatar: {ex.Message}");
        }
        
        yield return null;
    }
    
    private IEnumerator DownloadFromFirebaseRoutine(string avatarPath, string localFilePath, Image targetImage, Action onComplete, Action<Exception> onError)
    {
        // Get reference to the file in Firebase Storage
        StorageReference avatarRef = storageRoot.Child(avatarPath);
        
        // Get the download URL
        var urlTask = avatarRef.GetDownloadUrlAsync();
        yield return new WaitUntil(() => urlTask.IsCompleted || urlTask.IsFaulted);
        
        if (urlTask.IsFaulted)
        {
            Debug.LogError($"Failed to get download URL: {urlTask.Exception}");
            onError?.Invoke(urlTask.Exception);
            yield break;
        }
        
        string downloadUrl = urlTask.Result.ToString();
        UnityEngine.Networking.UnityWebRequest request = null;
        
        try
        {
            // Download the image
            request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(downloadUrl);
            request.SendWebRequest();
            
            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                // Get the downloaded texture
                Texture2D texture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);
                
                // Create a sprite from the texture
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    Vector2.one * 0.5f
                );
                
                // Apply to the target image
                targetImage.sprite = sprite;
                
                // Cache in memory
                avatarCache[avatarPath] = sprite;
                
                // Save to local storage
                SaveTextureToFile(texture, localFilePath);
                
                onComplete?.Invoke();
            }
            else
            {
                Debug.LogError($"Failed to download avatar: {request.error}");
                onError?.Invoke(new Exception(request.error));
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error downloading avatar: {ex.Message}");
            onError?.Invoke(ex);
        }
        finally
        {
            if (request != null)
            {
                request.Dispose();
            }
        }
    }
    
    private void SaveTextureToFile(Texture2D texture, string filePath)
    {
        try
        {
            byte[] pngData = texture.EncodeToPNG();
            File.WriteAllBytes(filePath, pngData);
            Debug.Log($"Saved avatar to {filePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to save avatar locally: {ex.Message}");
        }
    }
    
    private string GetSafeFilename(string input)
    {
        // Convert potentially unsafe path to a safe filename
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            input = input.Replace(c, '_');
        }
        return input;
    }
    
    /// <summary>
    /// Downloads all avatars from a collection in Firestore and caches them locally
    /// </summary>
    public void PreloadAvatars(List<string> avatarPaths, Action onComplete = null, Action<float> onProgress = null)
    {
        StartCoroutine(PreloadAvatarsRoutine(avatarPaths, onComplete, onProgress));
    }
    
    private IEnumerator PreloadAvatarsRoutine(List<string> avatarPaths, Action onComplete, Action<float> onProgress)
    {
        int total = avatarPaths.Count;
        int completed = 0;
        
        foreach (string path in avatarPaths)
        {
            // Placeholder image for loading
            Image dummyImage = new GameObject("TempImage").AddComponent<Image>();
            
            // Use the existing download method
            var downloadRoutine = LoadAvatarRoutine(
                path, 
                dummyImage, 
                onComplete: null, 
                onError: (ex) => Debug.LogWarning($"Failed to preload {path}: {ex.Message}")
            );
            
            yield return downloadRoutine;
            
            // Destroy the temporary image
            Destroy(dummyImage.gameObject);
            
            completed++;
            onProgress?.Invoke((float)completed / total);
            
            // Short delay to avoid overwhelming the system
            yield return new WaitForSeconds(0.1f);
        }
        
        onComplete?.Invoke();
    }
}