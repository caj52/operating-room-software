using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Currently only affects the Z-axis, as that's the only use case
/// </summary>
public class MatchScale : MonoBehaviour
{
    /// <summary>
    /// Maximum time, in seconds, that the instance will wait 
    /// for a master to retrieve materials before putting a 
    /// warning in console.
    /// </summary>
    private const float _maximumWaitTime = 10;

    private static List<MatchScale> _instances = new();

    public static UnityEvent<ScaleChangedEvent> 
    OnScaleProvidersUpdated { get; } = new();

    public UnityEvent OnScaleUpdated { get; } = new();

    [field: SerializeField] 
    public ScaleGroup ScaleGroup { get; private set; }

    /// <summary>
    /// Determines if this instance can be read by other 
    /// members of the group on <see cref="Start"/>. 
    /// Read by Additional Walls being added to a scene in 
    /// progress or loaded (but should be false for Additional Walls)
    /// </summary>
    [field: SerializeField,
    Tooltip("Should be false for Additional Wall selectable (and its children) " +
    "and true for permanent scene objects (room walls, room " +
    "baseboards, room wall protectors)")]
    public bool CanBeReadByGroup { get; private set; }

    private Selectable _selectable;

    private GizmoHandler _gizmoHandler;

    private UnityEventManager _eventManager = new();

    private void Awake()
    {
        _instances.Add(this);
        _selectable = GetComponent<Selectable>();
        _gizmoHandler = GetComponent<GizmoHandler>();

        _eventManager.RegisterEvents
            ((_gizmoHandler.GizmoDragPostUpdate, InvokeEvent),
            (_gizmoHandler.GizmoDragEnded, InvokeEvent), 
            (_selectable.ScaleUpdated, InvokeEvent));
        
        _eventManager.RegisterEvents<ScaleChangedEvent>
            ((OnScaleProvidersUpdated, UpdateScale));

        _eventManager.AddListeners();
    }

    private void OnDestroy()
    {
        _instances.Remove(this);
        _eventManager.RemoveListeners();
    }

    private IEnumerator Start()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
            yield break;

        yield return new WaitUntil(() =>
            !ConfigurationManager.IsLoading);

        if (!CanBeReadByGroup)
        {
            float timer = 0;
            int times = 1;
            while (true)
            {
                var master = _instances
                    .FirstOrDefault(x => x.ScaleGroup == ScaleGroup && x.CanBeReadByGroup);

                if (master != default)
                {
                    UpdateScale(new(master));
                    break;
                }

                timer += Time.deltaTime;

                if (timer > _maximumWaitTime)
                {
                    timer -= _maximumWaitTime;
                    Debug.LogWarning($"{nameof(MatchScale)} on {gameObject.name} has been waiting {_maximumWaitTime * times++} seconds for a group master. It's likely that some scene object (wall, baseboard, wallprotector) is missing its group assignment");
                }

                yield return null;
            }
        }
    }

    private void InvokeEvent()
    {
        OnScaleProvidersUpdated?.Invoke(new(this));
    }

    private void InvokeEvent
    (Selectable.ScaleLevel scaleLevel) 
        => InvokeEvent();

    private void UpdateScale(ScaleChangedEvent eventArgs)
    {
        if (eventArgs.Sender == this) 
            return;

        if (eventArgs.Sender.ScaleGroup != ScaleGroup)
            return;

        // Use scale levels if they exist
        if (eventArgs.Sender._selectable.ScaleLevels.Count > 0)
        {
            var masterScaleLevel = eventArgs.Sender._gizmoHandler.IsBeingUsed ?
                eventArgs.Sender._selectable.CurrentPreviewScaleLevel :
                eventArgs.Sender._selectable.CurrentScaleLevel;

            Debug.Log($"Matching {masterScaleLevel.Size} from game object {eventArgs.Sender.name} to game object {gameObject.name}");
            var localScaleLevel = _selectable.ScaleLevels
                .First(x => x.Size == masterScaleLevel.Size);

            _selectable.SetScaleLevel(localScaleLevel, true);

            OnScaleUpdated?.Invoke();
            return;
        }

        // Use localscale otherwise
        Vector3 localScale = transform.localScale;
        localScale.z = eventArgs.Sender.transform.localScale.z;
        transform.localScale = localScale;

        OnScaleUpdated?.Invoke();
    }

    public class ScaleChangedEvent
    {
        public ScaleChangedEvent(MatchScale sender)
        {
            Sender = sender;
        }

        public MatchScale Sender { get; }
    }
}
