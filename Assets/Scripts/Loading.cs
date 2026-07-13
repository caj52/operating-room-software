using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

public static class Loading
{
    public enum Kind
    {
        /// <summary>Full-screen blocker (room load, scene load, heavy ops).</summary>
        Main,
        /// <summary>Compact on-screen indicator (placing / loading a single item).</summary>
        Item
    }

    public class LoadingToken
    {
        public float Progress { get; private set; }
        public Kind Kind { get; }

        public void SetProgress(float progress)
        {
            Progress = progress;
            if (Progress == 1) Done();
        }

        public void SetProgress(object o, SplenSoft.AssetBundles.AssetRetrievalProgress progress)
        {
            Progress = progress.Progress;
            if (Progress == 1) Done();
        }

        public LoadingToken(Kind kind = Kind.Main)
        {
            Kind = kind;
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

    /// <summary>True when any full-screen (Main) load is active.</summary>
    public static bool HasMainLoading => _loadingTokens.Any(t => t.Kind == Kind.Main);

    /// <summary>True when only compact item loads are active (no Main).</summary>
    public static bool ShouldShowItemLoading => LoadingActive && !HasMainLoading;

    private static float _nonBackwardsProgress01;

    private static readonly List<LoadingToken> _loadingTokens = new();

    public static LoadingToken GetLoadingToken(Kind kind = Kind.Main) => new(kind);

    public static float GetTotalProgress01(bool getNonBackwards = true)
    {
        if (_loadingTokens.Count == 0) return 1;

        float progress = _loadingTokens.Sum(x => x.Progress) / _loadingTokens.Count;
        _nonBackwardsProgress01 = Mathf.Max(_nonBackwardsProgress01, progress);
        return getNonBackwards ? _nonBackwardsProgress01 : progress;
    }
}
