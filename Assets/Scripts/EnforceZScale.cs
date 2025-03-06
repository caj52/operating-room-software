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
        List<Selectable> upperSelectables = new List<Selectable>();

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
        Subscribe();
        Enforce();
    }

    private void Subscribe()
    {
        if (_directParent != null) 
        {
            _directParent.ScaleUpdated.AddListener(Enforce);
        }

        _selectable.ScaleUpdated.AddListener(Enforce);
    }

    private void Unsubscribe()
    {
        if (_directParent != null && !_directParent.IsDestroyed)
        {
            _directParent.ScaleUpdated.RemoveListener(Enforce);
        }

        if (_selectable != null && !_selectable.IsDestroyed) 
        {
            _selectable.ScaleUpdated.RemoveListener(Enforce);
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
            //try get lower scale level
            var validLevels = _selectable.ScaleLevels.Where(x => x.Size < parentScale).OrderByDescending(x => x.Size).ToList();
            if (validLevels.Count > 0) 
            {
                _selectable.SetScaleLevel(validLevels[0], true);
            }
            else // force parent to be bigger
            {
                validLevels = _directParent.ScaleLevels.Where(x => x.Size > _selectable.CurrentScaleLevel.Size).OrderBy(x => x.Size).ToList();
                if (validLevels.Count > 0 )
                {
                    _directParent.SetScaleLevel(validLevels[0], true);
                }
            }
        }
    }
}
