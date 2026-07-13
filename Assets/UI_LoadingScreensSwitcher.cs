using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Chooses full-screen vs compact item loading UI based on active <see cref="Loading"/> tokens.
/// Wired on the UI_LoadingScreen prefab (InnerBox vs InnerBoxItemsLoading).
/// </summary>
public class UI_LoadingScreensSwitcher : MonoBehaviour
{
    public GameObject mainLoadingScreen;
    public GameObject itemLoadingScreen;

    void Awake()
    {
        Loading.LoadingTokensChanged.AddListener(Refresh);
        Refresh();
    }

    void OnDestroy()
    {
        Loading.LoadingTokensChanged.RemoveListener(Refresh);
    }

    void OnEnable()
    {
        Refresh();
    }

    void Refresh()
    {
        bool any = Loading.LoadingActive;
        bool showMain = any && Loading.HasMainLoading;
        bool showItem = any && !showMain;

        if (mainLoadingScreen != null && mainLoadingScreen.activeSelf != showMain)
            mainLoadingScreen.SetActive(showMain);

        if (itemLoadingScreen != null)
        {
            if (itemLoadingScreen.activeSelf != showItem)
                itemLoadingScreen.SetActive(showItem);

            // Item overlay stretches full-screen with a transparent Image — don't eat clicks.
            if (showItem)
            {
                var img = itemLoadingScreen.GetComponent<Image>();
                if (img != null)
                    img.raycastTarget = false;

                var cg = itemLoadingScreen.GetComponent<CanvasGroup>();
                if (cg == null)
                    cg = itemLoadingScreen.AddComponent<CanvasGroup>();
                cg.blocksRaycasts = false;
                cg.interactable = false;
            }
        }
    }
}
