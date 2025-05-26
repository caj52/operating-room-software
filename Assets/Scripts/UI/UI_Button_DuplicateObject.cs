using HighlightPlus;
using System;
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

        Vector3 objPos = obj.transform.position;
        Quaternion objRot = obj.transform.rotation;
        Vector3 objScale = obj.transform.localScale;

        GameObject newObj = Instantiate(obj, objPos + Vector3.right, objRot);
        newObj.transform.localScale = objScale;

        DuplicateRoom room = FindObjectOfType<DuplicateRoom>();
        if (room != null)
        {
            room.onObjectPlaced?.Invoke(newObj);
        }

        HighlightEffect highlight = newObj.GetComponent<HighlightEffect>();
        if (highlight != null)
        {
            highlight.highlighted = false;
        }

        // ✅ Process ALL SelectablePrice components in hierarchy
        SelectablePrice[] oldPrices = obj.GetComponentsInChildren<SelectablePrice>(true);
        foreach (var oldPrice in oldPrices)
        {
            // Find corresponding new object in the duplicated hierarchy
            Transform relativePath = oldPrice.transform;
            string path = GetHierarchyPath(obj.transform, relativePath);
            Transform newTransform = newObj.transform.Find(path);

            if (newTransform == null)
            {
                Debug.LogWarning($"DuplicateObject: Could not find duplicated path: {path}");
                continue;
            }

            GameObject target = newTransform.gameObject;

            // Clean any copied component
            SelectablePrice copiedPrice = target.GetComponent<SelectablePrice>();
            if (copiedPrice != null) DestroyImmediate(copiedPrice);

            Selectable newSelectable = target.GetComponent<Selectable>();
            if (newSelectable == null)
            {
                Debug.LogWarning($"DuplicateObject: No Selectable found on {target.name}");
                continue;
            }

            SelectablePrice newPrice = target.AddComponent<SelectablePrice>();
            newPrice.isBoomObject = oldPrice.isBoomObject;
            newPrice.pricingObjectName = oldPrice.pricingObjectName;
            newPrice.UIObjectName = oldPrice.UIObjectName;
            newPrice.selectable = newSelectable;
            newPrice.objectPricingData = oldPrice.objectPricingData;
            newPrice.selectableObjectForSize = oldPrice.selectableObjectForSize;
            newPrice.sheetName = oldPrice.sheetName;

            // UI duplication
            PopulateUIWithPricingItems pricingUI = FindObjectOfType<PopulateUIWithPricingItems>(true);
            if (pricingUI != null)
            {
                PricingRowDataFill oldUIRow = oldPrice.PricingRowDataFill;
                if (oldUIRow != null)
                {
                    PricingRowDataFill newUIRow = Instantiate(oldUIRow, pricingUI.transform);
                    newPrice.PricingRowDataFill = newUIRow;

                    Action onDestroyedAction = newUIRow.DestroyRow;
                    newPrice.OnDestroyed += onDestroyedAction;
                    newUIRow.OnDestroyEvent += () => newPrice.OnDestroyed -= onDestroyedAction;
                }
            }
        }

        Debug.Log($"Duplicated {obj.name} with all selectable prices -> Scale: {newObj.transform.localScale}");
    }
    private string GetHierarchyPath(Transform root, Transform target)
    {
        string path = "";
        while (target != root && target != null)
        {
            path = target.name + (string.IsNullOrEmpty(path) ? "" : "/" + path);
            target = target.parent;
        }
        return path;
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
