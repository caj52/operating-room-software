using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Will enforce that the scale of this selectable is always 
/// less than that of another selectable on the same arm assembly 
/// that shares this component's id
/// </summary>
[RequireComponent(typeof(Selectable))]
public class EnforceZScale : MonoBehaviour
{
    [field: SerializeField] public string Id { get; private set; }
    private Selectable _selectable;
    private List<Selectable> _upperSelectables = new();
    private Selectable _directParent;
    private List<Selectable.ScaleLevel> _sortedSelectableLevels = new();
    private List<Selectable.ScaleLevel> _sortedParentLevels = new();
    /// <summary>
    /// Used to prevent stack overflow
    /// </summary>
    private readonly int _maxEnforcementAttemptsInFrame = 5;
    private int _currentEnforcementAttempts = 0;

    private void Awake()
    {
        _selectable = GetComponent<Selectable>();
    }

    private IEnumerator Start()
    {
        yield return new WaitUntil(() => !ConfigurationManager.IsLoading);

        if (!_selectable.TryGetArmAssemblyRoot(out GameObject rootObj))
        {
            throw new System.Exception($"Component of type {typeof(EnforceZScale)} was installed on a selectable that is not part of an arm assembly. This is not allowed");
        }

        _upperSelectables = rootObj.GetComponentsInChildren<Selectable>()
            .Where(x => x.TryGetComponent(out EnforceZScale enforceLength) && enforceLength.Id == Id)
            .OrderBy(x => x.transform.position.y)
            .ToList();

        int index = _upperSelectables.IndexOf(_selectable) + 1;
        if (index < _upperSelectables.Count) 
        {
            _directParent = _upperSelectables[index];
        }

        CacheSortedScaleLevels();
        Subscribe();
        Enforce();
    }

    private void CacheSortedScaleLevels()
    {
        _sortedSelectableLevels = _selectable.ScaleLevels.OrderByDescending(x => x.Size).ToList();
        if (_directParent != null)
            _sortedParentLevels = _directParent.ScaleLevels.OrderBy(x => x.Size).ToList();
    }

    private void Subscribe()
    {
        if (_directParent != null) 
        {
            _directParent.ScaleUpdated.AddListener(OnScaleUpdated);
        }

        _selectable.ScaleUpdated.AddListener(OnScaleUpdated);
    }

    private void OnScaleUpdated()
    {
        CacheSortedScaleLevels();
        Enforce();
    }

    private void Unsubscribe()
    {
        if (_directParent != null && !_directParent.IsDestroyed)
        {
            _directParent.ScaleUpdated.RemoveListener(OnScaleUpdated);
        }

        if (_selectable != null && !_selectable.IsDestroyed) 
        {
            _selectable.ScaleUpdated.RemoveListener(OnScaleUpdated);
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void LateUpdate()
    {
        _currentEnforcementAttempts = 0;
    }

    private void Enforce()
    {
        if (_directParent == null) return;

        if (_currentEnforcementAttempts++ > _maxEnforcementAttemptsInFrame) 
        {
            throw new System.Exception($"Max enforcement attempts reached");
        }

        float parentScale = _directParent.CurrentScaleLevel.Size;
        if (parentScale <= _selectable.CurrentScaleLevel.Size)
        {
            Selectable.ScaleLevel level = FindLargestBelow(_sortedSelectableLevels, parentScale);
            if (level != null)
            {
                _selectable.SetScaleLevel(level, true);
            }
            else
            {
                level = FindSmallestAbove(_sortedParentLevels, _selectable.CurrentScaleLevel.Size);
                if (level != null)
                    _directParent.SetScaleLevel(level, true);
            }
        }
    }

    private static Selectable.ScaleLevel FindLargestBelow(List<Selectable.ScaleLevel> sortedDescending, float maxSize)
    {
        for (int i = 0; i < sortedDescending.Count; i++)
        {
            if (sortedDescending[i].Size < maxSize)
                return sortedDescending[i];
        }
        return null;
    }

    private static Selectable.ScaleLevel FindSmallestAbove(List<Selectable.ScaleLevel> sortedAscending, float minSize)
    {
        for (int i = 0; i < sortedAscending.Count; i++)
        {
            if (sortedAscending[i].Size > minSize)
                return sortedAscending[i];
        }
        return null;
    }
}
