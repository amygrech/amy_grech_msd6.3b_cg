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
            try
            {
                storage = FirebaseStorage.DefaultInstance;
                storageRoot = storage.RootReference; // Use RootReference instead of GetReferenceFromUrl
                Debug.Log("Directly initialized Firebase Storage");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize Firebase Storage: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Load avatar from Firebase Storage and set it to an Image component
    /// </summary>
    public void LoadAvatar(string avatarPath, Image targetImage, Action onComplete = null, Action<Exception> onError = null)
    {
        // Fix the path - we need to remove "assets/" prefix if it exists
        string normalizedPath = NormalizeAvatarPath(avatarPath);
        
        StartCoroutine(LoadAvatarRoutine(normalizedPath, targetImage, onComplete, onError));
    }
    
    // Normalize the path to match what's in Firebase
    private string NormalizeAvatarPath(string path)
    {
        // If the path starts with "assets/", just get the filename
        if (path.StartsWith("assets/"))
        {
            string fileName = Path.GetFileName(path);
            Debug.Log($"Normalized path from {path} to {fileName}");
            return fileName;
        }
        return path;
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
        
        // Try to load a fallback from Resources
        if (TryLoadFallbackAvatar(avatarPath, targetImage))
        {
            onComplete?.Invoke();
            yield break;
        }
        
        // If not cached or stored locally, download from Firebase
        yield return DownloadFromFirebaseRoutine(avatarPath, localFilePath, targetImage, onComplete, onError);
    }
    
    private bool TryLoadFallbackAvatar(string avatarPath, Image targetImage)
    {
        // Try to load from Resources based on the filename
        string resourceName = null;
        string fileName = Path.GetFileName(avatarPath).ToLower();
        
        if (fileName.Contains("turtle"))
        {
            resourceName = "TurtleAvatar";
        }
        else if (fileName.Contains("shell"))
        {
            resourceName = "ShellAvatar";
        }
        else
        {
            resourceName = "DefaultAvatar";
        }
        
        Texture2D texture = Resources.Load<Texture2D>(resourceName);
        if (texture != null)
        {
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                Vector2.one * 0.5f
            );
            
            targetImage.sprite = sprite;
            
            // Cache the sprite
            avatarCache[avatarPath] = sprite;
            
            Debug.Log($"Using fallback avatar {resourceName} for {avatarPath}");
            return true;
        }
        
        Debug.LogWarning($"Fallback avatar {resourceName} not found in Resources");
        return false;
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
        if (storage == null || storageRoot == null)
        {
            Debug.LogError("Firebase Storage not initialized. Cannot download avatar.");
            onError?.Invoke(new InvalidOperationException("Firebase Storage not initialized"));
            yield break;
        }
        
        StorageReference avatarRef = null;
        
        // Wrap the entire download process to handle exceptions
        try
        {
            // Get reference to the file in Firebase Storage
            avatarRef = storageRoot.Child(avatarPath);
            Debug.Log($"Attempting to download from Firebase: {avatarPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error creating Firebase reference: {ex.Message}");
            onError?.Invoke(ex);
            yield break;
        }
        
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
        Debug.Log($"Got download URL: {downloadUrl}");
        
        // Download the image
        UnityEngine.Networking.UnityWebRequest request = 
            UnityEngine.Networking.UnityWebRequestTexture.GetTexture(downloadUrl);
        
        // Send the request - this is safe to yield return
        yield return request.SendWebRequest();
        
        if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            // Get the downloaded texture
            Texture2D texture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);
            
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
            try
            {
                SaveTextureToFile(texture, localFilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not save texture to file: {ex.Message}");
                // Continue since this is not critical
            }
            
            // Update any additional UI
            if (DirectAvatarConnection.Instance != null)
            {
                DirectAvatarConnection.Instance.UpdateAvatarFromTexture(avatarPath, texture);
            }
            
            onComplete?.Invoke();
        }
        else
        {
            Debug.LogError($"Failed to download avatar: {request.error}");
            onError?.Invoke(new Exception(request.error));
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
}