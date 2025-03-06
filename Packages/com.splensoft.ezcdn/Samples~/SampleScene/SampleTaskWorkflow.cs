using SplenSoft.AssetBundles;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// The sample task workflow involves more code, but you get more control
// This one will change the color of the loading bar depending on
// completion status
public class SampleTaskWorkflow : MonoBehaviour
{
    bool _taskRunning;

    public Image LoadingBar;
    public Text TextNotification;

    public async void RequestAsset()
    {
        if (_taskRunning) return;
        _taskRunning = true;

        bool failed = false;
        // instead of handling the progress via event, we will
        // do so via code
        var progress = new Progress<AssetRetrievalProgress>();
        progress.ProgressChanged += (_, p) => 
        {
            LoadingBar.fillAmount = p.Progress;
        };
        // This launches an asynchronous request, but 
        // now we've stored the task as a variable.
        var task = GetComponent<PrefabRequester>().GetAsset(
            progress: progress,
            onFailure: _ => failed = true
        );

        await task;
        if (!Application.isPlaying) return;

        // instead of handling failure via event, we will do so via code
        if (failed)
        {
            LoadingBar.color = Color.red;

            TextNotification.text = "Something went wrong when " +
                "downloading Cube (Task workflow)!";

            return;
        }

        Instantiate(task.Result, transform.position, transform.rotation);
        _taskRunning = false;
        LoadingBar.color = Color.blue;

        TextNotification.text = "Downloaded cube " +
            "(Task workflow) successfully!";
    }
}