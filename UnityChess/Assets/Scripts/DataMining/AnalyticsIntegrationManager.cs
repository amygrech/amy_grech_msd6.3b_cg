using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Integrates the analytics dashboard and game state management into the game's UI.
/// This script should be added to a GameObject in your main scene.
/// </summary>
public class AnalyticsIntegrationManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Canvas mainCanvas;
    [SerializeField] private GameObject mainMenu;
    [SerializeField] private Transform buttonContainer; // Where to add the analytics button
    
    [Header("Dashboard Settings")]
    [SerializeField] private bool createDashboardAtRuntime = true;
    [SerializeField] private GameObject analyticsDashboardPrefab;
    [SerializeField] private Sprite analyticsIcon;
    
    // References to created objects
    private GameObject analyticsDashboard;
    private Button analyticsButton;
    
    private void Awake()
    {
        // Find main canvas if not assigned
        if (mainCanvas == null)
        {
            mainCanvas = FindObjectOfType<Canvas>();
            if (mainCanvas == null)
            {
                Debug.LogError("No Canvas found in the scene. Cannot create UI elements.");
                enabled = false;
                return;
            }
        }
    }
    
    private void Start()
    {
        // Create or find analytics dashboard
        if (createDashboardAtRuntime)
        {
            if (analyticsDashboardPrefab != null)
            {
                // Instantiate from prefab
                analyticsDashboard = Instantiate(analyticsDashboardPrefab, mainCanvas.transform);
            }
            else
            {
                // Create programmatically
                AnalyticsDashboardBuilder builder = GetComponent<AnalyticsDashboardBuilder>();
                if (builder == null)
                {
                    builder = gameObject.AddComponent<AnalyticsDashboardBuilder>();
                }
                
                builder.targetCanvas = mainCanvas;
                analyticsDashboard = builder.CreateAnalyticsDashboard();
            }
        }
        else
        {
            // Find existing dashboard
            AnalyticsDashboardUI dashboardUI = FindObjectOfType<AnalyticsDashboardUI>();
            if (dashboardUI != null)
            {
                analyticsDashboard = dashboardUI.gameObject;
            }
        }
        
        // Add button to UI
        CreateAnalyticsButton();
        
        // Connect to Firebase manager events
        FirebaseManager firebaseManager = FindObjectOfType<FirebaseManager>();
        if (firebaseManager != null && !firebaseManager.IsInitialized)
        {
            firebaseManager.OnFirebaseInitialized += OnFirebaseInitialized;
        }
    }
    
    private void OnFirebaseInitialized()
    {
        Debug.Log("Firebase initialized - Analytics system ready");
        
        // Enable analytics button now that Firebase is ready
        if (analyticsButton != null)
        {
            analyticsButton.interactable = true;
        }
    }
    
    private void CreateAnalyticsButton()
    {
        // Find an appropriate parent for the button
        Transform parent = buttonContainer;
        if (parent == null && mainMenu != null)
        {
            parent = mainMenu.transform;
        }
        
        if (parent == null)
        {
            // Create a new container if none exists
            GameObject container = new GameObject("UI_Buttons");
            container.transform.SetParent(mainCanvas.transform, false);
            RectTransform containerRect = container.AddComponent<RectTransform>();
            
            // Position at top right corner
            containerRect.anchorMin = new Vector2(1, 1);
            containerRect.anchorMax = new Vector2(1, 1);
            containerRect.pivot = new Vector2(1, 1);
            containerRect.anchoredPosition = new Vector2(-20, -20);
            containerRect.sizeDelta = new Vector2(200, 40);
            
            parent = container.transform;
        }
        
        // Create button
        GameObject buttonObj = new GameObject("AnalyticsButton");
        buttonObj.transform.SetParent(parent, false);
        
        RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1, 0.5f);
        buttonRect.anchorMax = new Vector2(1, 0.5f);
        buttonRect.pivot = new Vector2(1, 0.5f);
        buttonRect.anchoredPosition = new Vector2(-10, 0);
        buttonRect.sizeDelta = new Vector2(40, 40);
        
        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.4f, 0.8f, 1);
        
        if (analyticsIcon != null)
        {
            buttonImage.sprite = analyticsIcon;
            buttonImage.type = Image.Type.Simple;
        }
        
        // Create button component
        analyticsButton = buttonObj.AddComponent<Button>();
        analyticsButton.targetGraphic = buttonImage;
        
        // Create icon or text
        if (analyticsIcon == null)
        {
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);
            
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            
            Text buttonText = textObj.AddComponent<Text>();
            buttonText.text = "📊"; // Chart emoji as icon
            buttonText.color = Color.white;
            buttonText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            buttonText.fontSize = 20;
            buttonText.alignment = TextAnchor.MiddleCenter;
        }
        
        // Set button action
        analyticsButton.onClick.AddListener(ToggleAnalyticsDashboard);
        
        // Initially disable until Firebase is ready
        FirebaseManager firebaseManager = FindObjectOfType<FirebaseManager>();
        analyticsButton.interactable = (firebaseManager != null && firebaseManager.IsInitialized);
    }
    
    private void ToggleAnalyticsDashboard()
    {
        if (analyticsDashboard != null)
        {
            bool currentState = analyticsDashboard.activeSelf;
            analyticsDashboard.SetActive(!currentState);
            
            // Tell the dashboard to refresh its data when opening
            if (!currentState)
            {
                AnalyticsDashboardUI dashboardUI = analyticsDashboard.GetComponent<AnalyticsDashboardUI>();
                if (dashboardUI != null)
                {
                    dashboardUI.RefreshDashboardData();
                }
            }
        }
        else
        {
            Debug.LogWarning("Analytics Dashboard not found!");
        }
    }
    
    private void OnDestroy()
    {
        // Clean up event subscription
        FirebaseManager firebaseManager = FindObjectOfType<FirebaseManager>();
        if (firebaseManager != null)
        {
            firebaseManager.OnFirebaseInitialized -= OnFirebaseInitialized;
        }
    }
}