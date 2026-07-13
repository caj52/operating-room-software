using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class UI_LoadingScreen : MonoBehaviour
{
    private static UI_LoadingScreen _instance;

    [field: SerializeField] private Image LoadingBar { get; set; }
    [SerializeField] private TextMeshProUGUI progress;
    [SerializeField] private TextMeshProUGUI progress1;

    CanvasGroup _canvasGroup;
    GraphicRaycaster _raycaster;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(_instance.gameObject);
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        _raycaster = GetComponent<GraphicRaycaster>();
        Loading.LoadingTokensChanged.AddListener(UpdateState);
    }

    private void Start()
    {
        UpdateState();
    }

    private void Update()
    {
        float p = Loading.GetTotalProgress01();
        if (LoadingBar != null)
            LoadingBar.fillAmount = p;
        string msg = "Please wait … " + Mathf.RoundToInt(p * 100f) + "%";
        if (progress != null)
            progress.text = msg;
        if (progress1 != null)
            progress1.text = msg;
    }

    private void OnDestroy()
    {
        Loading.LoadingTokensChanged.RemoveListener(UpdateState);
    }

    private void UpdateState()
    {
        bool active = Loading.LoadingActive;
        gameObject.SetActive(active);

        // Compact item loads must not steal input from placement / the scene.
        bool blockInput = active && Loading.HasMainLoading;
        if (_canvasGroup != null)
        {
            _canvasGroup.blocksRaycasts = blockInput;
            _canvasGroup.interactable = blockInput;
        }
        if (_raycaster != null)
            _raycaster.enabled = blockInput;
    }
}
