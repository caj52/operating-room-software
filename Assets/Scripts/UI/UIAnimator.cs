using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

public class UIAnimator : MonoBehaviour
{
    [Header("UI Panel Settings")]
    [SerializeField] private RectTransform uiPanel;
    [SerializeField] private Vector2 visiblePanelPosition = Vector2.zero;
    [SerializeField] private Vector2 hiddenPanelPosition = new Vector2(-400, 0);

    [Header("Button Settings")]
    [SerializeField] private RectTransform closeButton;
    [SerializeField] private Vector2 visibleButtonPosition;
    [SerializeField] private Vector2 hiddenButtonPosition;

    [Header("Animation Properties")]
    [SerializeField] private float animationDuration = 0.5f;
    [SerializeField] private AnimationCurve animationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Optional Fade Settings")]
    [SerializeField] private CanvasGroup panelCanvasGroup;
    [SerializeField] private bool fadeEnabled = true;

    private bool isVisible = true;
    private Coroutine animationCoroutine;

    private void Awake()
    {
        // Ensure canvas groups exist if fade is enabled
        if (fadeEnabled)
        {
            if (panelCanvasGroup == null)
            {
                panelCanvasGroup = uiPanel.gameObject.GetComponent<CanvasGroup>();
                if (panelCanvasGroup == null)
                {
                    panelCanvasGroup = uiPanel.gameObject.AddComponent<CanvasGroup>();
                }
            }
        }

        // Store initial positions if not set in inspector
        if (visibleButtonPosition == Vector2.zero)
            visibleButtonPosition = closeButton.anchoredPosition;

        if (hiddenButtonPosition == Vector2.zero)
            hiddenButtonPosition = new Vector2(
                visibleButtonPosition.x + uiPanel.rect.width,
                visibleButtonPosition.y
            );
    }

    private void Start()
    {
        // Initial setup
        SetInitialState();
    }

    private void SetInitialState()
    {
        uiPanel.anchoredPosition = visiblePanelPosition;
        closeButton.anchoredPosition = visibleButtonPosition;

        if (fadeEnabled && panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 1f;
        }
    }

    public void ToggleVisibility()
    {
        if (isVisible)
            HideUI();
        else
            ShowUI();
    }

    public void HideUI()
    {
        if (!isVisible) return;

        // Stop any ongoing animations
        if (animationCoroutine != null)
            StopCoroutine(animationCoroutine);

        // Start animation
        animationCoroutine = StartCoroutine(AnimateUIAndButton(
            uiPanel, visiblePanelPosition, hiddenPanelPosition,
            closeButton, visibleButtonPosition, hiddenButtonPosition,
            1f, 0f
        ));
        closeButton.GetComponentInChildren<TextMeshProUGUI>().text = ">";
        isVisible = false;
    }

    public void ShowUI()
    {
        if (isVisible) return;

        // Stop any ongoing animations
        if (animationCoroutine != null)
            StopCoroutine(animationCoroutine);

        // Start animation
        animationCoroutine = StartCoroutine(AnimateUIAndButton(
            uiPanel, hiddenPanelPosition, visiblePanelPosition,
            closeButton, hiddenButtonPosition, visibleButtonPosition,
            0f, 1f
        ));
        closeButton.GetComponentInChildren<TextMeshProUGUI>().text = "<";
        isVisible = true;
    }

    private IEnumerator AnimateUIAndButton(
        RectTransform panel, Vector2 panelStartPos, Vector2 panelEndPos,
        RectTransform button, Vector2 buttonStartPos, Vector2 buttonEndPos,
        float startAlpha, float endAlpha)
    {
        float time = 0f;

        while (time <= animationDuration)
        {
            // Calculate normalized time and curve value
            float normalizedTime = time / animationDuration;
            float curveValue = animationCurve.Evaluate(normalizedTime);

            // Animate panel position
            panel.anchoredPosition = Vector2.Lerp(panelStartPos, panelEndPos, curveValue);

            // Animate button position
            button.anchoredPosition = Vector2.Lerp(buttonStartPos, buttonEndPos, curveValue);

            // Animate alpha if fade is enabled
            if (fadeEnabled && panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, curveValue);
            }

            time += Time.deltaTime;
            yield return null;
        }

        // Ensure final positions
        panel.anchoredPosition = panelEndPos;
        button.anchoredPosition = buttonEndPos;

        if (fadeEnabled && panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = endAlpha;
        }

        animationCoroutine = null;
    }

    // Optional method to directly bind to a button click
    public void OnCloseButtonClicked()
    {
        HideUI();
    }
}