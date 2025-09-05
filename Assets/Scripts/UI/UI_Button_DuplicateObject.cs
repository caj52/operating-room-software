using HighlightPlus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class UI_Button_DuplicateObject : MonoBehaviour
{
    private PopulateUIWithPricingItems _pricingUI;
    private DuplicateRoom _duplicateRoom;

    private void Awake()
    {
        Selectable.SelectionChanged += UpdateActiveState;
        gameObject.SetActive(false);

        _pricingUI = FindObjectOfType<PopulateUIWithPricingItems>(true);
        _duplicateRoom = FindObjectOfType<DuplicateRoom>();
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= UpdateActiveState;
    }

    private void UpdateActiveState()
    {
        if (!Application.isPlaying || this == null) return;

        var selectedObjects = Selectable.SelectedSelectables;
        bool shouldBeActive = selectedObjects.Count > 0 &&
                              selectedObjects.Any(x => x.IsDestructible) &&
                              selectedObjects[0].canBeDuplicated;

        gameObject.SetActive(shouldBeActive);
    }

    public void OnDuplicateButtonClicked()
    {
        if (Selectable.SelectedSelectables.Count == 0) return;

        UI_DialogPrompt.Open("Are you sure you want to duplicate this object?",
            new ButtonAction
            {
                ButtonText = "Yes",
                Action = async () =>
                {
                    var selectables = Selectable.SelectedSelectables;
                    if (selectables.Count > 0)
                    {
                        await DuplicateObjectAsync(selectables[0].gameObject);
                    }
                    UI_DialogPrompt.Close();
                },
            },
            new ButtonAction
            {
                ButtonText = "Cancel",
                Action = () => UI_DialogPrompt.Close()
            }
        );
    }

    //Anwar Edits
    public async Task DuplicateObjectAsync(GameObject obj)
    {
        //if (obj == null)
        //{
        //    Debug.LogWarning("DuplicateObject: The object to duplicate is null.");
        //    return;
        //}

        //try
        //{
        //    PrepareObjectForDuplication(obj);
        //    GameObject duplicatedObj = CreateDuplicate(obj);
        //    DisableHighlighting(duplicatedObj);
        //    await DuplicatePricingComponentsAsync(obj, duplicatedObj);
        //    NotifyDuplication(duplicatedObj);

        //    Debug.Log($"Successfully duplicated {obj.name}");
        //}
        //catch (Exception e)
        //{
        //    Debug.LogError($"Error duplicating object: {e.Message}");
        //}
        if (obj == null) { Debug.LogWarning("DuplicateObject: The object to duplicate is null."); return; }

        try
        {
            // DO NOT mark the original
            GameObject duplicatedObj = CreateDuplicate(obj);
            MarkDuplicateStateAndResetBaselines(duplicatedObj);
            // Mark the duplicate's Selectables so Start() doesn't recalc divergent ScaleZ
            foreach (var selectable in duplicatedObj.GetComponentsInChildren<Selectable>(true))
                selectable.isDuplicated = true;

            DisableHighlighting(duplicatedObj);
            await DuplicatePricingComponentsAsync(obj, duplicatedObj);
            NotifyDuplication(duplicatedObj);

            Debug.Log($"Successfully duplicated {obj.name}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error duplicating object: {e.Message}");
        }
    }

    private GameObject CreateDuplicate(GameObject original)
    {
        Vector3 position = original.transform.position + Vector3.right * 2f;
        GameObject duplicate = Instantiate(original, position, original.transform.rotation);
        duplicate.transform.localScale = original.transform.localScale;
        return duplicate;
    }
    /// <summary>
    /// On the clone: mark all Selectables as duplicated and reset their
    /// constraint baselines so they don't drift/snap.
    /// </summary>
    private void MarkDuplicateStateAndResetBaselines(GameObject duplicatedObj)
    {
        var selectables = duplicatedObj.GetComponentsInChildren<Selectable>(true);
        foreach (var sel in selectables)
        {
            sel.isDuplicated = true;                               // the clone is the duplicated one
            sel.OriginalLocalPosition = sel.transform.localPosition; // reset translation baseline

            // If you cache other “originals” in your Selectable (e.g., local rotation or child scales),
            // reset them here as well. Example:
            // sel.StoreChildScales(); // if you want to refresh child scale cache on the clone
            // (Only if your logic relies on it.)
        }
    }
    private void PrepareObjectForDuplication(GameObject obj)
    {
        // Do NOT mark the source Selectables as duplicated.
        // We only pre-mark APs so clones won’t “climb” again (Fix #3 uses this).
        foreach (var ap in obj.GetComponentsInChildren<AttachmentPoint>(true))
        {
            ap.MarkParentNormalized(); // method added in AttachmentPoint.cs below
        }
    }

    private void DisableHighlighting(GameObject obj)
    {
        var highlights = obj.GetComponentsInChildren<HighlightEffect>(true);
        foreach (var highlight in highlights)
        {
            highlight.highlighted = false;
        }
    }

    private async Task DuplicatePricingComponentsAsync(GameObject original, GameObject duplicate)
    {
        var originalPrices = original.GetComponentsInChildren<SelectablePrice>(true);
        if (originalPrices.Length == 0) return;

        bool isLightGroup = originalPrices.Any(p => p.sheetName == DataFilePaths.sheetNameLight);

        if (isLightGroup && originalPrices.Length > 1)
        {
            await BatchProcessLightGroup(originalPrices, original, duplicate);
        }
        else
        {
            var tasks = originalPrices.Select(price => DuplicateSinglePricingComponent(price, original, duplicate));
            await Task.WhenAll(tasks);
        }
    }

    private async Task BatchProcessLightGroup(SelectablePrice[] originalPrices, GameObject original, GameObject duplicate)
    {
        var lightPrices = originalPrices.Where(p => p.sheetName == DataFilePaths.sheetNameLight);
        var nonLightPrices = originalPrices.Where(p => p.sheetName != DataFilePaths.sheetNameLight);

        // Process light prices in batch (without individual events)
        var lightTasks = lightPrices.Select(price => CreatePricingComponent(price, original, duplicate, suppressEvents: true));
        await Task.WhenAll(lightTasks);

        // Process non-light prices normally
        var nonLightTasks = nonLightPrices.Select(price => DuplicateSinglePricingComponent(price, original, duplicate));
        await Task.WhenAll(nonLightTasks);

        // Trigger batch update
        PricingManager.Instance.OnTotalPriceChanged?.Invoke();
    }

    private async Task DuplicateSinglePricingComponent(SelectablePrice originalPrice, GameObject original, GameObject duplicate)
    {
        await CreatePricingComponent(originalPrice, original, duplicate, suppressEvents: false);
    }

    private async Task CreatePricingComponent(SelectablePrice originalPrice, GameObject original, GameObject duplicate, bool suppressEvents)
    {
        try
        {
            var targetObject = FindTargetObject(originalPrice, original, duplicate);
            if (targetObject == null) return;

            CleanExistingPricing(targetObject);

            if (!targetObject.TryGetComponent(out Selectable targetSelectable))
            {
                Debug.LogWarning($"No Selectable on duplicate: {targetObject.name}");
                return;
            }

            var newPrice = await PricingManager.Instance.AddPricingComponent(
                targetObject,
                originalPrice.isBoomObject,
                originalPrice.pricingObjectName,
                originalPrice.UIObjectName,
                originalPrice.sheetName,
                originalPrice.rootParentName,
                originalPrice.Price
            );

            SetupSizeReference(originalPrice, newPrice, original, duplicate);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error duplicating pricing: {e.Message}");
        }
    }

    private GameObject FindTargetObject(SelectablePrice originalPrice, GameObject original, GameObject duplicate)
    {
        string hierarchyPath = GetHierarchyPath(original.transform, originalPrice.transform);
        Transform targetTransform = FindTransformByPath(duplicate.transform, hierarchyPath);
        return targetTransform?.gameObject;
    }

    private void CleanExistingPricing(GameObject targetObject)
    {
        if (targetObject.TryGetComponent(out SelectablePrice existing))
            DestroyImmediate(existing);
    }

    private void SetupSizeReference(SelectablePrice originalPrice, SelectablePrice newPrice, GameObject original, GameObject duplicate)
    {
        if (newPrice == null || originalPrice.SelectableObjectForSize == null) return;

        string sizePath = GetHierarchyPath(original.transform, originalPrice.SelectableObjectForSize.transform);
        Transform sizeTransform = FindTransformByPath(duplicate.transform, sizePath);

        if (sizeTransform != null && sizeTransform.TryGetComponent(out Selectable sizeSelectable))
        {
            newPrice.SelectableObjectForSize = sizeSelectable;
        }
    }

    private string GetHierarchyPath(Transform root, Transform target)
    {
        if (target == root) return "";

        var path = new List<string>();
        Transform current = target;

        while (current != root && current != null)
        {
            path.Insert(0, current.name);
            current = current.parent;
        }

        return string.Join("/", path);
    }

    private Transform FindTransformByPath(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path)) return root;

        Transform current = root;
        foreach (string part in path.Split('/'))
        {
            current = current.Find(part);
            if (current == null) break;
        }

        return current;
    }

    private void NotifyDuplication(GameObject duplicatedObj)
    {
        _duplicateRoom?.onObjectPlaced?.Invoke(duplicatedObj);
        duplicatedObj.GetComponent<Selectable>()?.StartRaycastPlacementMode();
    }

    public void DuplicateObject(GameObject obj)
    {
        _ = DuplicateObjectAsync(obj);
    }
}