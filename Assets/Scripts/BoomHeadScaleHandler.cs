using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RTG;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public class BoomHeadScaleHandler : MonoBehaviour
{
    [field: SerializeField] public Row[] attachRow;
    [field: SerializeField] public ColumnAtScale[] attachOffsets;
    private Selectable _selectable;
    [field: SerializeField] public Transform railAttachPoint;
    [field: SerializeField] public Selectable.ScaleLevel scale;

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
        _selectable.OnScaleChange?.AddListener((x) => ReassembleRows(x));
    }

    private void OnEnable()
    {
        ReassembleRows(scale);
    }
   public void ReassembleRows(Selectable.ScaleLevel scaleLevel)
    {
        if (scaleLevel.TryGetValue("rows", out string s_rowCount))
        {
            if (!string.IsNullOrEmpty(s_rowCount))
            {
            int rowCount = int.Parse(s_rowCount);

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
        _selectable.OnScaleChange?.RemoveListener((x) => ReassembleRows(x));
    }
    void SetHeight(GameObject go, int i, int rowCount)
    {
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
        if(railAttachPoint.childCount == 1) return; 

        Selectable rail = railAttachPoint.GetChild(1).GetComponent<Selectable>();
        Transform point = rail.transform.GetChild(0);
        List<AttachedShelf> shelves = new List<AttachedShelf>();

        foreach(Selectable selectable in point.GetComponentsInChildren<Selectable>())
        {
            if (selectable.SpecialTypes.Contains(SpecialSelectableType.ServiceHeadShelves))
            {
                shelves.Add(new AttachedShelf(selectable.transform));
            }
        }

        foreach(AttachedShelf child in shelves)
        {
            child.shelf.SetParent(null);
        }

        rail.transform.localScale = new Vector3(1,1,1);

        foreach(AttachedShelf child in shelves)
        {
            child.shelf.SetParent(point);
            child.shelf.localPosition = child.localPosition;
            child.shelf.SetWorldScale(new Vector3(1,1,1));
        }
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

    public AttachedShelf(Transform s)
    {
        shelf = s;
        localPosition = s.localPosition;
    }
}