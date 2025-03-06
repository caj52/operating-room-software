using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using SplenSoft.UnityUtilities;
using UnityEngine.Events;
using System.Collections;
using UnityEngine.SceneManagement;

/// <summary>
/// Used by Additional Wall (extra wall) selectable 
/// to automatically match the height of the all to the ceiling
/// </summary>
public class MatchHeightToWalls : MonoBehaviour
{
    private Selectable _selectable;

    public UnityEvent HeightSet { get; } = new();

    /// <summary>
    /// Populated on awake. Scale will not be updated until all 
    /// of these components' 
    /// <see cref="MoveToRootOnStart.Moved"/> is true
    /// </summary>
    private MoveToRootOnStart[] _moveToRootOnStarts;

    private void Awake()
    {
        _selectable = GetComponent<Selectable>();

        _moveToRootOnStarts = GetComponentsInChildren
            <MoveToRootOnStart>();

        RoomSize.RoomSizeChanged.AddListener(UpdateScale);
    }

    private void OnDestroy()
    {
        RoomSize.RoomSizeChanged.RemoveListener(UpdateScale);
    }

    private IEnumerator Start()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
            yield break;

        yield return new WaitUntil(() =>
            !ConfigurationManager.IsLoading);

        UpdateScale();
    }

    private async void UpdateScale(RoomDimension dimension = default)
    {
        while (!_selectable.Started || 
        _moveToRootOnStarts.Any(x => !x.Moved)) 
        { 
            await Task.Yield();
            if (!Application.isPlaying)
                throw new AppQuitInTaskException();
        }

        transform.localScale = new Vector3
            (transform.localScale.x, 
            transform.localScale.y, 
            RoomSize.Instance.CurrentDimensions.Height.ToMeters());

        HeightSet?.Invoke();

        //Debug.Log("Updated additional wall scale");
    }
}