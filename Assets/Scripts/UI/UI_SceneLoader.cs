using SplenSoft.AssetBundles;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

public class UI_SceneLoader : MonoBehaviour
{
    private VideoPlayer videoPlayer;
    // Start is called before the first frame update
    void Start()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        videoPlayer.loopPointReached += CheckVideoEnd;
    }

    private void CheckVideoEnd(VideoPlayer source)
    {
      
       
        AutoInstantiator.OnAppStart();
#if UNITY_EDITOR
        SceneManager.LoadScene("Start");
#endif
        AutoInstantiator.OnJobsFinished.AddListener(LoadScene);
      
    }

    private void LoadScene()
    {
        Debug.LogError("LoadScene");
        SceneManager.LoadScene("Start");
    }
}
