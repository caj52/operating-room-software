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

    private int _lastProgressPercent = -1;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(_instance.gameObject);
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        Loading.LoadingTokensChanged.AddListener(UpdateState);
     

    }


    private void Start()
    {
      
        UpdateState();
    }

    private void Update()
    {
        float progress01 = Loading.GetTotalProgress01();
        LoadingBar.fillAmount = progress01;

        int percent = Mathf.RoundToInt(progress01 * 100f);
        if (percent != _lastProgressPercent)
        {
            _lastProgressPercent = percent;
            string text = "Please wait ...: " + percent + "%";
            progress.text = text;
            progress1.text = text;
        }
    }

    private void OnDestroy()
    {
        Loading.LoadingTokensChanged.RemoveListener(UpdateState);
    }

    private void UpdateState()
    {
  
        gameObject.SetActive(Loading.LoadingActive);
    }

}
