using SplenSoft.AssetBundles;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

public static class Loading
{
    public class LoadingToken
    {
        public float Progress { get; private set; }

        public void SetProgress(float progress)
        {
            Progress = progress;
            if (Progress == 1) Done();
        }

        public void SetProgress(object o, AssetRetrievalProgress progress)
        {
            Progress = progress.Progress;
            if (Progress == 1) Done();
        }

        public LoadingToken()
        {
            _loadingTokens.Add(this);
            LoadingTokensChanged?.Invoke();
        }

        public async void Done()
        {
            _loadingTokens.Remove(this);
            LoadingTokensChanged?.Invoke();
            await Task.Yield();
            if (_loadingTokens.Count == 0) 
            {
                _nonBackwardsProgress01 = 0;
            }
        }
    }

    public static UnityEvent LoadingTokensChanged = new();

    public static bool LoadingActive => _loadingTokens.Count > 0;

    private static float _nonBackwardsProgress01;

    private static List<LoadingToken> _loadingTokens = new();

    public static LoadingToken GetLoadingToken() => new();

    public static float GetTotalProgress01(bool getNonBackwards = true)
    {
        if (_loadingTokens.Count == 0) return 1;

        float progress = _loadingTokens.Sum(x => x.Progress) / _loadingTokens.Count;
        _nonBackwardsProgress01 = Mathf.Max(_nonBackwardsProgress01, progress);
        return getNonBackwards ? _nonBackwardsProgress01 : progress;
    }
}