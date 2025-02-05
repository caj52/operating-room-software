using HighlightPlus;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class UI_Button_DuplicateObject : MonoBehaviour
{
    private void Awake()
    {
        Selectable.SelectionChanged += UpdateActiveState;

        gameObject.SetActive(false);
    }

    private void UpdateActiveState()
    {
        if (!Application.isPlaying) return;
        if (this == null || gameObject == null) return;

        bool active = Selectable.SelectedSelectables.Count > 0;

        if (active && !Selectable.SelectedSelectables
        .Any(x => x.IsDestructible))
            return;

        gameObject.SetActive(active);
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= UpdateActiveState;
    }

    public void DeleteSelectedSelectable()
    {
        UI_DialogPrompt.Open("Are you sure you want to Duplicate this object?",
            new ButtonAction
            {
                ButtonText = "Yes",
                Action = () =>
                {
                    var selectables = Selectable.SelectedSelectables;
                    DuplicateObject(selectables[0].gameObject);
       
                    UI_DialogPrompt.Close();
                },
            },
            new ButtonAction
            {
                ButtonText = "Cancel"
            }
        );
    }

    public void DuplicateObject(GameObject obj)
    {
        if (obj == null)
        {
            Debug.LogWarning("DuplicateObject: The object to duplicate is null.");
            return;
        }

        // Get original position & rotation
        Vector3 objPos = obj.transform.position;
        Quaternion objRot = obj.transform.rotation;

        // Instantiate the duplicate
        GameObject newObj = Instantiate(obj, objPos + Vector3.right, objRot);
        newObj.GetComponent<HighlightEffect>().highlighted = false;
       
        // Reset scale (fix parent-child scaling issues)
        newObj.transform.localScale = obj.transform.localScale;

        // Start coroutine to fix child scales after Unity updates
        StartCoroutine(ApplyChildScalesDelayed(obj.transform, newObj.transform));
        Debug.Log($"Duplicated {obj.name} -> Scale: {newObj.transform.localScale}");
    }

    private IEnumerator ApplyChildScalesDelayed(Transform original, Transform duplicate)
    {
        yield return new WaitForEndOfFrame(); // Wait for Unity to finish updating

        CopyChildScales(original, duplicate);
        Debug.Log("Scale applied after delay!");
        Selectable.DeselectAll();

    }

    private Dictionary<string, Transform> BuildChildLookup(Transform parent)
    {
        Dictionary<string, Transform> lookup = new Dictionary<string, Transform>();
        foreach (Transform child in parent.GetComponentsInChildren<Transform>())
        {
            if (!lookup.ContainsKey(child.name)) // Prevent duplicate names issues
                lookup[child.name] = child;
        }
        return lookup;
    }

    private void CopyChildScales(Transform original, Transform duplicate)
    {
        Dictionary<string, Transform> duplicateChildren = BuildChildLookup(duplicate);

        foreach (Transform originalChild in original)
        {
            if (duplicateChildren.TryGetValue(originalChild.name, out Transform newChild))
            {
                Vector3 scaleFix = new Vector3(
                    originalChild.lossyScale.x / duplicate.lossyScale.x,
                    originalChild.lossyScale.y / duplicate.lossyScale.y,
                    originalChild.lossyScale.z / duplicate.lossyScale.z
                );

                newChild.localScale = scaleFix;

                Debug.Log($"Child: {originalChild.name}, Applied Scale: {newChild.localScale}");

                // Recursively apply to all nested children
                CopyChildScales(originalChild, newChild);
            }
        }
    }
}
