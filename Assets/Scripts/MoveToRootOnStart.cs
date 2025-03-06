using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Makes this <see cref="GameObject"/> a scene root object 
/// on <see cref="Start"/> (removes all parents) 
/// </summary>
public class MoveToRootOnStart : MonoBehaviour
{
    /// <summary>
    /// Fires when object becomes a root object
    /// </summary>
    public UnityEvent OnMoved { get; } = new();

    public bool Moved { get; private set; }

    private Selectable _selectable;
    private Selectable _formerParent;

    private void Awake()
    {
        _formerParent = transform.root.GetComponent<Selectable>();
        _formerParent.SelectableDestroyed.AddListener(DestroyThis);
        _selectable = GetComponent<Selectable>();
    }

    private void OnDestroy()
    {
        transform.root.GetComponent<Selectable>().SelectableDestroyed.RemoveListener(DestroyThis);

        if (!_formerParent.IsDestroyed)
        {
            Destroy(_formerParent.gameObject);
        }
    }

    private void DestroyThis()
    {
        Destroy(gameObject);   
    }

    private void Start()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
            return;

        Selectable.ActiveSelectables.ForEach(x =>
        {
            if (x.RelatedSelectables.Contains(_selectable))
            {
                x.RelatedSelectables.Remove(_selectable);
            }
        });

        _selectable.RelatedSelectables = new() { _selectable };

        transform.parent = null;
        Moved = true;
        Debug.Log($"Set {gameObject.name} as root obj");
        OnMoved?.Invoke();
    }
}