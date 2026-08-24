using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RTG;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[Serializable]
public class BoomHeadScaleHandler : MonoBehaviour
{
    [field: SerializeField] public Row[] attachRow;
    [field: SerializeField] public ColumnAtScale[] attachOffsets;
    private Selectable _selectable;
    [field: SerializeField] public Transform railAttachPoint;
    [field: SerializeField] public Selectable.ScaleLevel scale;

    private UnityAction<Selectable.ScaleLevel> _onScaleChangeHandler;
    /// <summary>Set by config load after SettleLoadedBoomAssembly so OnEnable does not reassemble again.</summary>
    public bool RowsSettledByConfigLoad;

    [field: SerializeField]
    private List<GameObject> GameObjectsDisabledOnRuntime
    { get; set; } = new();

    private void Awake()
    {
        _selectable = GetComponent<Selectable>();
        if (SceneManager.GetActiveScene().name == "Main")
        {
            GameObjectsDisabledOnRuntime.ForEach(x =>
            {
                x.SetActive(false);
            });
        }
    }

    private void Start()
    {
        if (_selectable == null) return;
        _onScaleChangeHandler = ReassembleRows;
        _selectable.OnScaleChange?.AddListener(_onScaleChangeHandler);
    }

    private void OnEnable()
    {
        StartCoroutine(DelayedReassembleRows());
    }

    private IEnumerator DelayedReassembleRows()
    {
        yield return new WaitUntil(() => !ConfigurationManager.IsLoading);
        yield return null;
        // Config load already ran ReassembleRows in SettleLoadedBoomAssembly.
        // Skip only that one deferred pass; clear so a later OnEnable still works.
        if (RowsSettledByConfigLoad)
        {
            RowsSettledByConfigLoad = false;
            yield break;
        }
        if (_selectable != null && _selectable.CurrentScaleLevel != null)
        {
            ReassembleRows(_selectable.CurrentScaleLevel);
        }
    }

    public void ReassembleRows(Selectable.ScaleLevel scaleLevel)
    {
        if (scaleLevel != null && scaleLevel.TryGetValue("rows", out string s_rowCount)
            && !string.IsNullOrEmpty(s_rowCount)
            && int.TryParse(s_rowCount, out int rowCount))
        {
            for (int i = 1; i <= attachRow.Length; i++)
            {
                if (i <= rowCount)
                {
                    foreach (GameObject go in attachRow[i - 1].entries)
                    {
                        go.SetActive(true);
                        SetHeight(go, i - 1, rowCount);
                    }
                }
                else
                {
                    foreach (GameObject go in attachRow[i - 1].entries)
                    {
                        go.SetActive(false);
                    }
                }
            }
        }

        SetRailScale(scaleLevel);
    }

    public void ReassembleRowsCount(int c)
    {
        for (int i = 1; i <= attachRow.Length; i++)
        {
            if (i <= c)
            {
                foreach (GameObject go in attachRow[i - 1].entries)
                {
                    go.SetActive(true);
                    SetHeight(go, i - 1, c);
                }
            }
            else
            {
                foreach (GameObject go in attachRow[i - 1].entries)
                {
                    go.SetActive(false);
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (_selectable != null && _onScaleChangeHandler != null)
            _selectable.OnScaleChange?.RemoveListener(_onScaleChangeHandler);
    }

    void SetHeight(GameObject go, int i, int rowCount)
    {
        if (attachOffsets == null || rowCount < 1 || rowCount > attachOffsets.Length) return;
        go.transform.localPosition = new Vector3(
                go.transform.localPosition.x,
                go.transform.localPosition.y,
                attachOffsets[rowCount - 1].zPosition[i]
            );
    }

    public void SetupNewRail()
    {
        SetRailScale(scale);
    }

    void SetRailScale(Selectable.ScaleLevel scaleLevel)
    {
        scale = scaleLevel;
        if (railAttachPoint == null || railAttachPoint.childCount <= 1) return;

        Selectable rail = railAttachPoint.GetChild(1).GetComponent<Selectable>();
        if (rail == null) return;

        if (rail.transform.childCount < 1) return;
        Transform point = rail.transform.GetChild(0);
        if (point == null) return;

        var shelves = new List<AttachedShelf>();
        foreach (Selectable selectable in point.GetComponentsInChildren<Selectable>())
        {
            if (selectable.SpecialTypes.Contains(SpecialSelectableType.ServiceHeadShelves))
            {
                shelves.Add(new AttachedShelf(selectable.transform));
            }
        }

        foreach (AttachedShelf child in shelves)
            child.shelf.SetParent(null);

        // AuthoredIdentity contract: root identity. Mesh/AP children keep prefab import
        // scales (Rear Rail 0.01, AttachPoint 0.1). Never tube-isolate or Size/extent squash.
        rail.transform.localScale = Vector3.one;
        rail.EnsureAuthoredIdentityMountScale();

        foreach (AttachedShelf child in shelves)
        {
            child.shelf.SetParent(point);
            child.shelf.localPosition = child.localPosition;
            // Preserve SKU X stretch — do not force (1,1,1).
            child.shelf.localScale = child.localScale;
        }

        RailScaleDiag.Dump("SetRailScale", rail);

        // Do NOT call EnsureAttachChain here — after load/MoveUp that double-compensates.
        // Interactive SetScaleLevel / pre-MoveUp FixLoaded already own attach-chain math.
    }
}

[Serializable]
public struct Row
{
    [field: SerializeField] public GameObject[] entries;
}

[Serializable]
public struct ColumnAtScale
{
    [field: SerializeField] public float[] zPosition;
}

[Serializable]
public struct AttachedShelf
{
    [field: SerializeField] public Transform shelf;
    [field: SerializeField] public Vector3 localPosition;
    [field: SerializeField] public Vector3 localScale;

    public AttachedShelf(Transform s)
    {
        shelf = s;
        localPosition = s.localPosition;
        localScale = s.localScale;
    }
}
