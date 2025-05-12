using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages object configuration and scale filtering based on attached components.
/// Handles various combinations of LED lights and Flat Panels.
/// </summary>
public class GetAttachedObjects : MonoBehaviour
{
    [field: SerializeField] public string Id { get; private set; }

    // Configuration/State properties
    public GameObject ArmSegmentParent { get; private set; }
    public bool IsLightAtTop { get; private set; }
    public bool IsPanelAtTop { get; private set; }

    // Component references
    private List<Selectable> _selectables;
    private EnforceZScale _primaryZScale;
    private EnforceZScale[] _allZScales;
    private SelectablePrice[] _prices;

    // Cached counts
    private int _lightCount = 0;
    private int _flatPanelCount = 0;
    private int _totalComponentCount = 0;

    // Configuration flags
    private bool _needsTurningCover = false;

    // Scale configurations based on specific combinations
    private readonly Dictionary<string, List<List<float>>> _configurationScales = new Dictionary<string, List<List<float>>>
    {
        // Single component configurations
        {"1_0", new List<List<float>> { new List<float> { 0.8f, 0.925f, 1.04f, 1.3f } } },     // Single LED
        {"0_1", new List<List<float>> { new List<float> { 0.8f, 1.062f } } },                  // Single FP
        
        // Two component configurations
        {"2_0", new List<List<float>> {                                                        // LED/LED
            new List<float> { 0.925f, 1.04f, 1.3f },   // Top LED
            new List<float> { 0.8f, 0.925f, 1.15f }    // Bottom LED
        }},
        {"1_1_FP_TOP", new List<List<float>> {                                                 // FP/LED (Turning cover required)
            new List<float> { 0.925f, 1.062f },        // Top FP
            new List<float> { 0.8f, 0.925f }           // Bottom LED
        }},
        {"1_1_LED_TOP", new List<List<float>> {                                                // LED/FP
            new List<float> { 0.925f },                // Top LED
            new List<float> { 0.8f }                   // Bottom FP
        }},
        {"0_2", new List<List<float>> {                                                        // FP/FP (Turning cover required)
            new List<float> { 0.925f, 1.062f },        // Top FP
            new List<float> { 0.8f, 0.822f }           // Bottom FP
        }},
        
        // Three component configurations
        {"3_0", new List<List<float>> {                                                        // LED/LED/LED
            new List<float> { 1.04f, 1.15f },          // Top LED
            new List<float> { 0.925f, 1.04f },         // Middle LED
            new List<float> { 0.8f, 0.925f }           // Bottom LED
        }},
        {"2_1_FP_TOP", new List<List<float>> {                                                 // FP/LED/LED (Turning cover required)
            new List<float> { 1.04f },                 // Top FP
            new List<float> { 0.925f },                // Middle LED
            new List<float> { 0.8f }                   // Bottom LED
        }},
        {"2_1_LED_TOP", new List<List<float>> {                                                // LED/LED/FP
            new List<float> { 1.04f },                 // Top LED
            new List<float> { 0.925f },                // Middle LED
            new List<float> { 0.8f }                   // Bottom FP
        }},
        {"1_2", new List<List<float>> {                                                        // FP/LED/FP (Turning cover required)
            new List<float> { 1.04f },                 // Top FP
            new List<float> { 0.925f },                // Middle LED
            new List<float> { 0.8f }                   // Bottom FP
        }}
    };

    private IEnumerator Start()
    {
        // Wait for configuration to be loaded
        yield return new WaitUntil(() => !ConfigurationManager.IsLoading);

        try
        {
            // Gather component references
            GatherComponentReferences();

            // Count attached components
            CountAttachedComponents();

            // Calculate total components
            _totalComponentCount = _lightCount + _flatPanelCount;

            // Determine component positions
            DetectComponentPositions();

            // Check if turning cover is required
            DetermineTurningCoverRequirement();

            // Apply appropriate scale filters based on configuration
            ApplyScaleFiltersBasedOnConfiguration();

            // Log configuration details
            LogConfigurationDetails();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error in Start method: {e.Message}", this);
        }
    }

    private void GatherComponentReferences()
    {
        _selectables = GetComponentsInParent<Selectable>().ToList();
        _primaryZScale = GetComponentInParent<EnforceZScale>();

        // Find the parent arm segment
        foreach (var selectable in _selectables)
        {
            if (selectable != null && selectable.name.Contains("ArmDropTube"))
            {
                ArmSegmentParent = selectable.gameObject;
                break;
            }
        }

        if (ArmSegmentParent == null)
        {
            Debug.LogWarning("Could not find ArmSegment_1 parent", this);
            return;
        }

        // Get all zScales
        _allZScales = ArmSegmentParent.GetComponentsInChildren<EnforceZScale>();

        // Get all price components
        _prices = ArmSegmentParent.GetComponentsInChildren<SelectablePrice>();

        Debug.Log($"Found {_allZScales?.Length ?? 0} ZScale components and {_prices?.Length ?? 0} price components", this);
    }

    private void CountAttachedComponents()
    {
        if (_prices == null || _prices.Length == 0)
        {
            Debug.LogWarning("No SelectablePrice components found in children", this);
            return;
        }

        try
        {
            // Count light components with null safety
            _lightCount = _prices.Count(x =>
                x != null &&
                x.pricingObjectName != null &&
                (x.pricingObjectName.Contains("U | ONE (Low Ceiling)") ||
                x.pricingObjectName.Contains("U | ONE (Standard)")));

            // Count flat panel components with null safety
            _flatPanelCount = _prices.Count(x =>
                x != null &&
                x.pricingObjectName != null &&
                x.pricingObjectName.Contains("Flat Panel Arm"));

            Debug.Log($"Found {_lightCount} lights and {_flatPanelCount} flat panels", this);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error counting components: {e.Message}", this);
        }
    }

    private void DetermineTurningCoverRequirement()
    {
        string configKey = DetermineConfigurationKey();

        // Configurations requiring turning cover
        _needsTurningCover = configKey.Equals("1_1_FP_TOP") ||
                            configKey.Equals("0_2") ||
                            configKey.Equals("2_1_FP_TOP") ||
                            configKey.Equals("1_2");

        Debug.Log($"Turning cover requirement determined: {_needsTurningCover}", this);
    }

    private void ApplyScaleFiltersBasedOnConfiguration()
    {
        if (_allZScales == null || _allZScales.Length == 0)
        {
            Debug.LogWarning("No ZScale components found", this);
            return;
        }

        try
        {
            // Determine configuration key based on component counts and positions
            string configKey = DetermineConfigurationKey();

            Debug.Log($"Configuration key determined: {configKey}", this);

            // Apply scale filters based on configuration
            if (_configurationScales.ContainsKey(configKey))
            {
                var scaleConfigs = _configurationScales[configKey];
                ApplyConfigurationScales(scaleConfigs);
            }
            else
            {
                Debug.LogWarning($"No predefined scale configuration found for key {configKey}", this);
                // Apply default/fallback scales if needed
                ApplyDefaultScales();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error applying scale filters: {e.Message}", this);
        }
    }

    private string DetermineConfigurationKey()
    {
        // Basic configuration key based on component counts
        string baseKey = $"{_lightCount}_{_flatPanelCount}";

        // For mixed configurations where component position matters
        if (_lightCount > 0 && _flatPanelCount > 0)
        {
            if (_lightCount == 1 && _flatPanelCount == 1)
            {
                if (IsLightAtTop)
                {
                    baseKey += "_LED_TOP";
                }
                else if (IsPanelAtTop)
                {
                    baseKey += "_FP_TOP";
                }
            }
            else if (_lightCount == 2 && _flatPanelCount == 1)
            {
                if (IsLightAtTop)
                {
                    baseKey += "_LED_TOP";
                }
                else if (IsPanelAtTop)
                {
                    baseKey += "_FP_TOP";
                }
            }
        }

        return baseKey;
    }

    private void ApplyConfigurationScales(List<List<float>> scaleConfigs)
    {
        // Sort ZScales by position to match with the appropriate scale configurations
        var sortedZScales = SortZScalesByPosition();

        // Apply scale configurations to the corresponding ZScales
        int count = Mathf.Min(sortedZScales.Count, scaleConfigs.Count);
        for (int i = 0; i < count; i++)
        {
            var zScale = sortedZScales[i].zScale;
            var selectable = zScale.GetComponent<Selectable>();

            if (selectable == null)
            {
                Debug.LogWarning($"ZScale at position {i} has no Selectable component", this);
                continue;
            }

            var allowedScales = scaleConfigs[i];

            
            ApplyScaleFilter(allowedScales, selectable);

            Debug.Log($"Applied scales {string.Join(", ", allowedScales)} to component at position {i}", this);
        }
    }

    private List<(EnforceZScale zScale, float position)> SortZScalesByPosition()
    {
        var scalePairs = new List<(EnforceZScale zScale, float position)>();

        foreach (var zScale in _allZScales)
        {
            if (zScale == null) continue;

            // Use Y position for sorting (higher Y = top)
            float position = zScale.transform.position.y;
            scalePairs.Add((zScale, position));
        }

        // Sort by Y position, highest first (top to bottom)
        return scalePairs.OrderByDescending(pair => pair.position).ToList();
    }

    private void ApplyDefaultScales()
    {
        Debug.Log("Applying default scales to all components", this);

        foreach (var zScale in _allZScales)
        {
            if (zScale == null) continue;

            var selectable = zScale.GetComponent<Selectable>();
            if (selectable == null) continue;

            // Apply a default set of scales
            ApplyScaleFilter(new List<float> { 0.8f, 0.925f }, selectable);
        }
    }

    // Helper method to detect which component is at the top position
    private void DetectComponentPositions()
    {
        if (_prices == null || _prices.Length == 0) return;

        try
        {
            // Create a list to track components by position
            var componentPositions = new List<(SelectablePrice price, float position, bool isLight, bool isPanel)>();

            // Collect component information
            foreach (var price in _prices)
            {
                if (price == null || string.IsNullOrEmpty(price.pricingObjectName)) continue;

                bool isLight = price.pricingObjectName.Contains("U | ONE (Low Ceiling)") ||
                              price.pricingObjectName.Contains("U | ONE (Standard)");
                bool isPanel = price.pricingObjectName.Contains("Flat Panel Arm");

                if (!isLight && !isPanel) continue;

                // Use Y position for ordering
                float position = price.transform.position.y;
                componentPositions.Add((price, position, isLight, isPanel));
            }

            // Sort by position (highest Y = top)
            var sortedComponents = componentPositions.OrderByDescending(c => c.position).ToList();

            // Check top component type if we have any components
            if (sortedComponents.Count > 0)
            {
                var topComponent = sortedComponents[0];
                IsLightAtTop = topComponent.isLight;
                IsPanelAtTop = topComponent.isPanel;

                Debug.Log($"Top component detected: " +
                        (IsLightAtTop ? "Light" : (IsPanelAtTop ? "Panel" : "Unknown")), this);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error detecting component positions: {e.Message}", this);
        }
    }

    private void ApplyScaleFilter(List<float> allowedScales, Selectable selectable)
    {
        try
        {
            if (selectable == null)
            {
                Debug.LogWarning("Cannot apply scale filter to null selectable", this);
                return;
            }

            if (selectable.ScaleLevels == null)
            {
                Debug.LogWarning($"ScaleLevels is null on {selectable.name}", this);
                return;
            }

            if (selectable.ScaleLevels.Count == 0)
            {
                Debug.LogWarning($"ScaleLevels is empty on {selectable.name}", this);
                return;
            }

            // Log before filtering
            Debug.Log($"Before filtering: {selectable.name} has {selectable.ScaleLevels.Count} scale levels: {string.Join(", ", selectable.ScaleLevels.Select(l => l.Size))}", this);

            // Create a new filtered list to avoid modifying during enumeration
            var filteredScales = selectable.ScaleLevels
                .Where(level => level != null && allowedScales.Contains(level.Size))
                .ToList();

            // Assign the filtered list
            selectable.ScaleLevels = filteredScales;

            // Log after filtering
            Debug.Log($"After filtering: {selectable.name} has {selectable.ScaleLevels.Count} scale levels: {string.Join(", ", selectable.ScaleLevels.Select(l => l.Size))}", this);
            Debug.Log($"Applied scale filter to {selectable.name} - Allowed scales: {string.Join(", ", allowedScales)}", this);

            if (selectable.ScaleLevels.Count == 0)
            {
                Debug.LogWarning($"WARNING: Filtering resulted in zero scale levels for {selectable.name}!", this);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error applying scale filter to {selectable?.name}: {e.Message}", this);
        }
    }

    private void LogConfigurationDetails()
    {
        Debug.Log($"Configuration Summary for {gameObject.name}:", this);
        Debug.Log($"- Component Counts: {_lightCount} LED lights, {_flatPanelCount} Flat Panels", this);
        Debug.Log($"- Total Components: {_totalComponentCount}", this);
        Debug.Log($"- Component Positions: LED at top: {IsLightAtTop}, Panel at top: {IsPanelAtTop}", this);
        Debug.Log($"- Turning Cover Required: {_needsTurningCover}", this);
        Debug.Log($"- Found {_allZScales?.Length ?? 0} ZScale components", this);

        // Log the configuration key
        string configKey = DetermineConfigurationKey();
        Debug.Log($"- Configuration Key: {configKey}", this);

        // Log whether this configuration is supported
        Debug.Log($"- Has Predefined Configuration: {_configurationScales.ContainsKey(configKey)}", this);
    }
}