using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using System;

public class UI_GeneralLoadingScreen : MonoBehaviour
{
    public static UI_GeneralLoadingScreen instance;
    public Image loadingImage;
    public float loadingSpeed = 1f;
    public TextMeshProUGUI statusText;
    public Action OnCancel;
    public bool isLoading = false;
    private float targetFillAmount = 0f;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        gameObject.SetActive(false);

        // Make sure the fill amount starts at 0
        if (loadingImage != null)
        {
            loadingImage.type = Image.Type.Filled;
            loadingImage.fillAmount = 0f;
        }
    }

    public void ShowLoadingScreen(float targetProgress = 1f)
    {
    
        gameObject.SetActive(true);
        targetFillAmount = Mathf.Clamp01(targetProgress);

        if (!isLoading)
        {
            StartCoroutine(FillLoadingBar());
        }
    }

    public void HideLoadingScreen()
    {
        // Disabling the GO stops FillLoadingBar mid-loop without clearing isLoading,
        // which then permanently skips restarting the bar on the next Show.
        isLoading = false;
        StopAllCoroutines();
        gameObject.SetActive(false);
        if (loadingImage != null)
            loadingImage.fillAmount = 0f;
        targetFillAmount = 0f;
    }

    public void SetProgress(float progress)
    {
        targetFillAmount = Mathf.Clamp01(progress);
    }

    private IEnumerator FillLoadingBar()
    {
        isLoading = true;

        while (loadingImage != null && loadingImage.fillAmount < targetFillAmount)
        {
            loadingImage.fillAmount = Mathf.MoveTowards(loadingImage.fillAmount, targetFillAmount, loadingSpeed * Time.deltaTime);
            yield return null;
        }

        isLoading = false;
    }

    private void Update()
    {
        if (!gameObject.activeInHierarchy)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
            OnCancel?.Invoke();
    }

    public void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = "Please wait… " + message;
    }


    public void EnableLoadingScreen (bool enable)
    {
        gameObject.SetActive(enable);
        loadingImage.fillAmount = 0;
    }
}