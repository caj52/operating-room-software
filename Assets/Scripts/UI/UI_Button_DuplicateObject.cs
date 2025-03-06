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
        if (!Application.isPlaying || this == null || gameObject == null) return;

        var selectedObjects = Selectable.SelectedSelectables;
        bool active = selectedObjects.Count > 0;

        if (active && !selectedObjects.Any(x => x.IsDestructible))
            return;

        gameObject.SetActive(active && selectedObjects[0].canBeDuplicated);
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= UpdateActiveState;
    }

    public void DeleteSelectedSelectable()
    {
        if (Selectable.SelectedSelectables.Count == 0) return;

        UI_DialogPrompt.Open("Are you sure you want to duplicate this object?",
            new ButtonAction
            {
                ButtonText = "Yes",
                Action = () =>
                {
                    var selectables = Selectable.SelectedSelectables;
                    if (selectables.Count > 0)
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

        PrepareObjectToDuplicate(obj);
        // Get original position, rotation, and scale
        Vector3 objPos = obj.transform.position;
        Quaternion objRot = obj.transform.rotation;
        Vector3 objScale = obj.transform.localScale;

        // Instantiate the duplicate
        GameObject newObj = Instantiate(obj, objPos + Vector3.right, objRot);
        newObj.transform.localScale = objScale; // Ensure same scale

        // Ensure `DuplicateRoom` exists before proceeding
        DuplicateRoom room = FindObjectOfType<DuplicateRoom>();
        if (room != null)
        {
            room.onObjectPlaced?.Invoke(newObj);
        }
        else
        {
            Debug.LogWarning("DuplicateRoom not found. Object duplication will proceed without assigning a room.");
        }

        // Disable highlighting on the new object if applicable
        HighlightEffect highlight = newObj.GetComponent<HighlightEffect>();
        if (highlight != null)
        {
            highlight.highlighted = false;
        }

        // Start coroutine to apply correct child scales
     //   StartCoroutine(ApplyChildScalesDelayed(obj.transform, newObj.transform));

        Debug.Log($"Duplicated {obj.name} -> Scale: {newObj.transform.localScale}");
    }

    private void PrepareObjectToDuplicate(GameObject obj)
    {
        foreach (Transform child in obj.GetComponentsInChildren<Transform>(true)) // true includes inactive children
        {
            Selectable selectable = child.GetComponent<Selectable>();
            if (selectable)
            {
                selectable.isDuplicated = true;
            }
        }
    }

    private IEnumerator ApplyChildScalesDelayed(Transform original, Transform duplicate)
    {
        yield return new WaitForEndOfFrame(); // Wait for Unity's update cycle
        CopyChildScales(original, duplicate);
    }

    private void CopyChildScales(Transform original, Transform duplicate)
    {
        foreach (Transform originalChild in original)
        {
            Transform newChild = duplicate.Find(originalChild.name);
            if (newChild != null)
            {
                newChild.localScale = originalChild.localScale; // Preserve exact local scale
                Debug.Log($"Child: {originalChild.name}, Applied Scale: {newChild.localScale}");

                // Recursively apply to all nested children
                CopyChildScales(originalChild, newChild);
            }
        }
    }
}
