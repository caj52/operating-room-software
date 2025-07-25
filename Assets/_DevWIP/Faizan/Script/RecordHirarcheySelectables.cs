using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Threading.Tasks;

/// <summary>
/// Records and manages hierarchical relationships between selectables in a configuration
/// </summary>
public class RecordHirarcheySelectables : MonoBehaviour
{
    #region Fields and Properties

    [Header("Attached Selectables")]
    [SerializeField] private List<AttachedSelectable> _attachedSelectables = new List<AttachedSelectable>();
    [SerializeField] private List<AttachedSelectable> _attachedSelectables2 = new List<AttachedSelectable>();

    [Header("Tandem Mount References")]
    [SerializeField] private GameObject _tandemMount1;
    [SerializeField] private GameObject _tandemMount2;

    // Public properties for access
    public IReadOnlyList<AttachedSelectable> AttachedSelectables => _attachedSelectables;
    public IReadOnlyList<AttachedSelectable> AttachedSelectables2 => _attachedSelectables2;
    public GameObject TandemMount1 => _tandemMount1;
    public GameObject TandemMount2 => _tandemMount2;

    // Scale configurations
    private static readonly Dictionary<string, ScaleConfiguration> ScaleConfigurations = new Dictionary<string, ScaleConfiguration>
    {
        { "Standard_Top", new ScaleConfiguration { Scales = new[] { 0.6f, 0.8f, 1f, 1.2f } } },
        { "XL_Top", new ScaleConfiguration { Scales = new[] { 0.6f, 0.8f, 1f, 1.2f, 1.4f, 1.6f } } },
        { "Articulating_Bottom", new ScaleConfiguration { Scales = new[] { 1.0f } } },
        { "Fixed_Bottom", new ScaleConfiguration { Scales = new[] { 0.6f, 0.8f, 1f, 1.2f, 1.4f, 1.6f } } }
    };

    #endregion

    #region Public Methods

    /// <summary>
    /// Adds a selectable to the hierarchy and configures it appropriately
    /// </summary>
    public async Task AddAttachedSelectableAsync(Selectable selectable, string objectName)
    {
        if (selectable == null || string.IsNullOrEmpty(objectName))
        {
            Debug.LogWarning("Cannot add null selectable or empty object name");
            return;
        }

        if (objectName == "Tandem Mount")
        {
            await HandleTandemMount(selectable, objectName);
        }
        else
        {
            await HandleStandardSelectable(selectable, objectName);
        }
    }

    /// <summary>
    /// Synchronous version for backward compatibility
    /// </summary>
    public void AddAttachedSelectables(Selectable selectable, string objectName)
    {
        _ = AddAttachedSelectableAsync(selectable, objectName);
    }

    /// <summary>
    /// Gets all attached selectables across both lists
    /// </summary>
    public List<AttachedSelectable> GetAllAttachedSelectables()
    {
        var allSelectables = new List<AttachedSelectable>();
        allSelectables.AddRange(_attachedSelectables);
        allSelectables.AddRange(_attachedSelectables2);
        return allSelectables;
    }

    /// <summary>
    /// Finds an attached selectable by name
    /// </summary>
    public AttachedSelectable FindAttachedSelectable(string name)
    {
        return GetAllAttachedSelectables()
            .FirstOrDefault(a => a.GameObject != null && a.GameObject.name.Contains(name));
    }

    /// <summary>
    /// Clears all attached selectables
    /// </summary>
    public void ClearAll()
    {
        _attachedSelectables.Clear();
        _attachedSelectables2.Clear();
        _tandemMount1 = null;
        _tandemMount2 = null;
    }

    #endregion

    #region Private Methods

    private async Task HandleTandemMount(Selectable selectable, string objectName)
    {
        if (selectable.AttachmentPointDatas.Count == 2)
        {
            _tandemMount1 = selectable.AttachmentPointDatas[0].AttachmentPoint.gameObject;
            _tandemMount2 = selectable.AttachmentPointDatas[1].AttachmentPoint.gameObject;

            Debug.Log($"Tandem Mount configured with two attachment points");

            // Add pricing if needed
           // await AddPricingIfRequired(selectable, objectName, true);
        }
        else
        {
            Debug.LogWarning($"Tandem Mount expected 2 attachment points but found {selectable.AttachmentPointDatas.Count}");
        }
    }

    private async Task HandleStandardSelectable(Selectable selectable, string objectName)
    {
        if (_tandemMount1 == null && _tandemMount2 == null)
        {
            // Normal object
            await AddToMainList(selectable, objectName);
            await ApplyScaleConfigurations(objectName);
        }
        else
        {
            // Object attached to tandem mount
            await HandleTandemAttachment(selectable, objectName);
        }
    }

    private async Task AddToMainList(Selectable selectable, string objectName)
    {
        var attachedSelectable = new AttachedSelectable(selectable.gameObject, objectName);
        _attachedSelectables.Add(attachedSelectable);

        Debug.Log($"Added {objectName} to main attached selectables list");

        // Add pricing if needed
        //await AddPricingIfRequired(selectable, objectName, false);
    }

    private async Task HandleTandemAttachment(Selectable selectable, string objectName)
    {
        GameObject selectableGo = selectable.gameObject;

        if (_tandemMount1 != null && selectableGo.transform.IsChildOf(_tandemMount1.transform))
        {
            var attachedSelectable = new AttachedSelectable(selectableGo, objectName);
            _attachedSelectables.Add(attachedSelectable);
            Debug.Log($"Added {objectName} to first tandem mount");
        }
        else if (_tandemMount2 != null && selectableGo.transform.IsChildOf(_tandemMount2.transform))
        {
            var attachedSelectable = new AttachedSelectable(selectableGo, objectName);
            _attachedSelectables2.Add(attachedSelectable);
            Debug.Log($"Added {objectName} to second tandem mount");
        }
        else
        {
            Debug.LogWarning($"Object {objectName} is not a child of either tandem mount");
        }

        // Add pricing if needed
       // await AddPricingIfRequired(selectable, objectName, false);
    }

    private async Task AddPricingIfRequired(Selectable selectable, string objectName, bool isBoom)
    {
        // Check if pricing component already exists
        if (selectable.GetComponent<SelectablePrice>() != null)
        {
            return;
        }

        // Use PricingManager if available
        if (PricingManager.Instance != null)
        {
            string sheetName = isBoom ? DataFilePaths.sheetNameBoomIndividual : DataFilePaths.sheetNameLight;

            await PricingManager.Instance.AddPricingComponent(
                selectable.gameObject,
                isBoom,
                objectName,
                objectName,
                sheetName
            );
        }
    }

    private async Task ApplyScaleConfigurations(string objectName)
    {
        await Task.Yield(); // Ensure UI doesn't freeze

        if (objectName.Contains("Fixed"))
        {
            ApplyScaleToArm("TopArm", objectName.Contains("XL") ? "XL_Top" : "Standard_Top");
            ApplyScaleToArm("BottomArm", "Fixed_Bottom");
        }
        else if (objectName.Contains("Powered") || objectName.Contains("Spring"))
        {
            ApplyScaleToArm("TopArm", objectName.Contains("XL") ? "XL_Top" : "Standard_Top");
            ApplyScaleToArm("BottomArm", "Articulating_Bottom");
        }
    }

    private void ApplyScaleToArm(string armKeyword, string configKey)
    {
        var arm = _attachedSelectables
            .Where(a => a?.GameObject != null)
            .FirstOrDefault(a => a.GameObject.name.Contains(armKeyword));

        if (arm == null)
        {
            Debug.LogWarning($"Could not find arm with keyword: {armKeyword}");
            return;
        }

        var selectables = arm.GameObject
            .GetComponentsInChildren<Selectable>()
            .Where(x => x.ScaleLevels != null && x.ScaleLevels.Count > 0);

        var target = selectables.FirstOrDefault();
        if (target != null && ScaleConfigurations.TryGetValue(configKey, out var config))
        {
            Debug.Log($"Applying scale configuration '{configKey}' to {armKeyword}: {target.name}");
            ApplyScaleFilter(config.Scales.ToList(), target);
        }
    }

    private void ApplyScaleFilter(List<float> allowedScales, Selectable selectable)
    {
        try
        {
            if (!ValidateScaleFilter(selectable, allowedScales))
            {
                return;
            }

            // Log before filtering
            LogScaleLevels("Before filtering", selectable);

            // Create filtered list
            var filteredScales = selectable.ScaleLevels
                .Where(level => level != null && allowedScales.Contains(level.Size))
                .ToList();

            // Apply filtered list
            selectable.ScaleLevels = filteredScales;

            // Log after filtering
            LogScaleLevels("After filtering", selectable);

            // Ensure at least one scale level remains
            if (filteredScales.Count == 0)
            {
                Debug.LogError($"Filtering removed all scale levels from {selectable.name}! Reverting to original.", this);
                // Consider reverting or adding a default scale
            }
            else
            {
                // Ensure one scale is selected
                if (!filteredScales.Any(s => s.Selected))
                {
                    filteredScales[0].Selected = true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error applying scale filter to {selectable?.name}: {e.Message}", this);
            Debug.LogException(e, this);
        }
    }

    private bool ValidateScaleFilter(Selectable selectable, List<float> allowedScales)
    {
        if (selectable == null)
        {
            Debug.LogWarning("Cannot apply scale filter to null selectable", this);
            return false;
        }

        if (selectable.ScaleLevels == null || selectable.ScaleLevels.Count == 0)
        {
            Debug.LogWarning($"No scale levels to filter on {selectable.name}", this);
            return false;
        }

        if (allowedScales == null || allowedScales.Count == 0)
        {
            Debug.LogWarning("No allowed scales specified", this);
            return false;
        }

        return true;
    }

    private void LogScaleLevels(string prefix, Selectable selectable)
    {
        var scales = string.Join(", ", selectable.ScaleLevels.Select(l => $"{l.Size}m"));
        Debug.Log($"{prefix}: {selectable.name} has {selectable.ScaleLevels.Count} scale levels: [{scales}]", this);
    }

    #endregion

    #region Nested Types

    [Serializable]
    public class AttachedSelectable
    {
        [SerializeField] private GameObject gameObject;
        [SerializeField] private string btName;
        [SerializeField] private string excelKey;

        public GameObject GameObject => gameObject;
        public string BtName => btName;
        public string ExcelKey => excelKey;

        // Excel key mappings
        private static readonly Dictionary<string, string> ExcelKeyMappings = new Dictionary<string, string>
        {
            { "Boom Bottom Arm - Powered", "Powered Boom" },
            { "Boom Bottom Arm - Spring", "Spring Boom" },
            { "Boom Bottom Arm - Fixed", "Fixed Boom" },
            { "Boom Drop Tube", "Ceiling Flange" },
            { "Boom Top Arm(XL)", "XL Top" },
            { "Boom Column Tube", "Ceiling Flange" }
        };

        public AttachedSelectable(GameObject go, string name)
        {
            gameObject = go;
            btName = name;
            excelKey = GetExcelKey(name);
        }

        private string GetExcelKey(string uiName)
        {
            return ExcelKeyMappings.TryGetValue(uiName, out string key) ? key : uiName;
        }

        public void UpdateExcelKey()
        {
            excelKey = GetExcelKey(btName);
        }
    }

    private class ScaleConfiguration
    {
        public float[] Scales { get; set; }
    }

    #endregion
}

// Backward compatibility extension
public static class RecordHierarchyExtensions
{
    /// <summary>
    /// Extension method for backward compatibility with the misspelled property name
    /// </summary>
    public static List<RecordHirarcheySelectables.AttachedSelectable> GetAttachedSelectables(this RecordHirarcheySelectables record)
    {
        return record.AttachedSelectables.ToList();
    }

    /// <summary>
    /// Extension method for backward compatibility with the misspelled property name
    /// </summary>
    public static List<RecordHirarcheySelectables.AttachedSelectable> GetAttachedSelectables2(this RecordHirarcheySelectables record)
    {
        return record.AttachedSelectables2.ToList();
    }
}

// For backward compatibility - redirect old class name references
[Obsolete("Use RecordHirarcheySelectables.AttachedSelectable instead")]
public class AttachedSelectables : RecordHirarcheySelectables.AttachedSelectable
{
    public AttachedSelectables() : base(null, "") { }
}