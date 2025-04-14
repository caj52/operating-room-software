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

    private void Awake()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        videoPlayer.Prepare();
        videoPlayer.loopPointReached += CheckVideoEnd;
       
    }

    private void Start()
    {
        videoPlayer.Play();
    }


    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            // Stop the video when space is pressed
            if (videoPlayer != null && videoPlayer.isPlaying)
            {
                videoPlayer.Pause();
            }

            // Call the parameterless version of CheckVideoEnd
            CheckVideoEnd();
        }
    }

    // This method is called when video naturally reaches the end
    private void CheckVideoEnd(VideoPlayer source)
    {
        CheckVideoEnd();
    }

    // Separate the core functionality into a parameterless method
    private void CheckVideoEnd()
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


    private void OnDestroy()
    {
        videoPlayer.loopPointReached -= CheckVideoEnd;
    }
}