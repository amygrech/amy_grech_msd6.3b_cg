using UnityEngine;
using UnityEngine.UI;

public class DLCStoreButton : MonoBehaviour
{
    private Button button;
    
    // Direct reference to the DLCStoreManager
    [SerializeField] private DLCStoreManager storeManager;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(OpenDLCStore);
        }
    }

    private void OpenDLCStore()
    {
        Debug.Log("DLC Store Button clicked!");
        
        // Try using the instance first
        if (DLCStoreManager.Instance != null)
        {
            Debug.Log("Using singleton instance");
            DLCStoreManager.Instance.OpenDLCStore();
        }
        // Fall back to direct reference
        else if (storeManager != null)
        {
            Debug.Log("Using direct reference");
            storeManager.OpenDLCStore();
        }
        else
        {
            Debug.LogError("DLCStoreManager not found! Assign it directly or ensure singleton is initialized.");
        }
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OpenDLCStore);
        }
    }
}