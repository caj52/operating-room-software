using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Selectable;

/// <summary>
/// Manages object configuration and scale filtering based on attached components.
/// Handles various combinations of LED lights and Flat Panels.
/// </summary>
public class GetAttachedObjects : MonoBehaviour
{
    [field: SerializeField] public string Id { get; private set; }

    public GameObject ArmSegmentParent { get; private set; }
    public bool IsLightAtTop { get; private set; }
    public bool IsPanelAtTop { get; private set; }

    private List<Selectable> _selectables;
    private EnforceZScale _primaryZScale;
    private EnforceZScale[] _allZScales;
    private Selectable[] _allZSelectables;
    private SelectablePrice[] _prices;

    private int _lightCount = 0;
    private int _flatPanelCount = 0;
    private int _totalComponentCount = 0;
    private bool _needsTurningCover = false;

    private readonly Dictionary<string, List<List<float>>> _configurationScales = new Dictionary<string, List<List<float>>>
    {
        {"1_0", new List<List<float>> { new List<float> { 0.8f, 0.925f, 1.04f, 1.3f } } },
        {"0_1", new List<List<float>> { new List<float> { 0.8f, 1.062f } } },
        {"2_0", new List<List<float>> {
            new List<float> { 0.925f, 1.04f, 1.3f },
            new List<float> { 0.8f, 0.925f, 1.15f }
        }},
        {"1_1_FP_TOP", new List<List<float>> {
            new List<float> { 0.925f, 1.062f },
            new List<float> { 0.8f, 0.925f }
        }},
        {"1_1_LED_TOP", new List<List<float>> {
            new List<float> { 0.925f },
            new List<float> { 0.8f }
        }},
        {"0_2", new List<List<float>> {
            new List<float> { 0.925f, 1.062f },
            new List<float> { 0.8f, 0.822f }
        }},
        {"3_0", new List<List<float>> {
            new List<float> { 1.04f, 1.15f },
            new List<float> { 0.925f, 1.04f },
            new List<float> { 0.8f, 0.925f }
        }},
        {"2_1_FP_TOP", new List<List<float>> {
            new List<float> { 1.04f },
            new List<float> { 0.925f },
            new List<float> { 0.8f }
        }},
        {"2_1_LED_TOP", new List<List<float>> {
            new List<float> { 1.04f },
            new List<float> { 0.925f },
            new List<float> { 0.8f }
        }},
        {"1_2", new List<List<float>> {
            new List<float> { 1.04f },
            new List<float> { 0.925f },
            new List<float> { 0.8f }
        }}
    };

    private IEnumerator Start()
    {
        yield return new WaitUntil(() => !ConfigurationManager.IsLoading);

        try
        {
            GatherComponentReferences();
            CountAttachedComponents();
            _totalComponentCount = _lightCount + _flatPanelCount;
            DetectComponentPositions();
            DetermineTurningCoverRequirement();
            ApplyScaleFiltersBasedOnConfiguration();
            LogConfigurationDetails();
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error in Start method: {e.Message}", this);
        }
    }

    /// <summary>
    /// True for Imagine U|ONE / U|002 surgical lights (and legacy Simeon / U202 catalog names).
    /// </summary>
    public static bool IsSurgicalLightPricingName(string pricingObjectName)
    {
        if (string.IsNullOrEmpty(pricingObjectName))
            return false;

        string n = pricingObjectName;
        if (n.IndexOf("Flat Panel", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        // Imagine catalog forms
        if (n.IndexOf("U | ONE", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("U|ONE", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("U ONE", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("U | 002", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("U|002", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("U 002", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("U202", StringComparison.OrdinalIgnoreCase) >= 0) return true;

        // Prefab / legacy Simeon names
        if (n.IndexOf("Simeon_Light_7000", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Simeon_Light_8000", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Simeon Light 7000", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Simeon Light 8000", StringComparison.OrdinalIgnoreCase) >= 0) return true;

        return false;
    }

    public static bool IsFlatPanelPricingName(string pricingObjectName)
    {
        return !string.IsNullOrEmpty(pricingObjectName)
            && pricingObjectName.IndexOf("Flat Panel", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void GatherComponentReferences()
    {
        _selectables = GetComponentsInParent<Selectable>().ToList();
        _primaryZScale = GetComponentInParent<EnforceZScale>();

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

        _allZScales = ArmSegmentParent.GetComponentsInChildren<EnforceZScale>();
        _allZSelectables = _allZScales.Select(x => x.gameObject.GetComponent<Selectable>()).Where(s => s != null).ToArray();
        _prices = ArmSegmentParent.GetComponentsInChildren<SelectablePrice>();
    }

    private void CountAttachedComponents()
    {
        if (_prices == null || _prices.Length == 0)
            return;

        try
        {
            _lightCount = _prices.Count(x =>
                x != null && IsSurgicalLightPricingName(x.pricingObjectName));

            _flatPanelCount = _prices.Count(x =>
                x != null && IsFlatPanelPricingName(x.pricingObjectName));
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error counting components: {e.Message}", this);
        }
    }

    private void DetermineTurningCoverRequirement()
    {
        string configKey = DetermineConfigurationKey();
        _needsTurningCover = configKey.Equals("1_1_FP_TOP") ||
                            configKey.Equals("0_2") ||
                            configKey.Equals("2_1_FP_TOP") ||
                            configKey.Equals("1_2");
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
            string configKey = DetermineConfigurationKey();
            if (_configurationScales.ContainsKey(configKey))
            {
                ApplyConfigurationScales(_configurationScales[configKey]);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error applying scale filters: {e.Message}", this);
        }
    }

    private string DetermineConfigurationKey()
    {
        string baseKey = $"{_lightCount}_{_flatPanelCount}";

        if (_lightCount > 0 && _flatPanelCount > 0)
        {
            if (_lightCount == 1 && _flatPanelCount == 1)
            {
                if (IsLightAtTop) baseKey += "_LED_TOP";
                else if (IsPanelAtTop) baseKey += "_FP_TOP";
            }
            else if (_lightCount == 2 && _flatPanelCount == 1)
            {
                if (IsLightAtTop) baseKey += "_LED_TOP";
                else if (IsPanelAtTop) baseKey += "_FP_TOP";
            }
        }

        return baseKey;
    }

    private void ApplyConfigurationScales(List<List<float>> scaleConfigs)
    {
        var sortedZScales = SortZScalesByPosition();
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

            ApplyScaleFilter(scaleConfigs[i], selectable, _allZSelectables.ToList());
        }
    }

    private List<(EnforceZScale zScale, float position)> SortZScalesByPosition()
    {
        var scalePairs = new List<(EnforceZScale zScale, float position)>();
        foreach (var zScale in _allZScales)
        {
            if (zScale == null) continue;
            scalePairs.Add((zScale, zScale.transform.position.y));
        }
        return scalePairs.OrderByDescending(pair => pair.position).ToList();
    }

    private void DetectComponentPositions()
    {
        if (_prices == null || _prices.Length == 0) return;

        try
        {
            var componentPositions = new List<(SelectablePrice price, float position, bool isLight, bool isPanel)>();

            foreach (var price in _prices)
            {
                if (price == null || string.IsNullOrEmpty(price.pricingObjectName)) continue;

                bool isLight = IsSurgicalLightPricingName(price.pricingObjectName);
                bool isPanel = IsFlatPanelPricingName(price.pricingObjectName);
                if (!isLight && !isPanel) continue;

                componentPositions.Add((price, price.transform.position.y, isLight, isPanel));
            }

            var sortedComponents = componentPositions.OrderByDescending(c => c.position).ToList();
            if (sortedComponents.Count > 0)
            {
                var topComponent = sortedComponents[0];
                IsLightAtTop = topComponent.isLight;
                IsPanelAtTop = topComponent.isPanel;
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error detecting component positions: {e.Message}", this);
        }
    }

    private float globalReferenceSize = 1.0f;
    private bool referenceSizeCalculated = false;

    private void ApplyScaleFilter(List<float> allowedScales, Selectable selectable, List<Selectable> allSelectables)
    {
        try
        {
            if (selectable == null)
            {
                Debug.LogWarning("Cannot apply scale filter to null selectable", this);
                return;
            }

            if (selectable.ScaleLevels == null || selectable.ScaleLevels.Count == 0)
            {
                Debug.LogWarning($"ScaleLevels missing or empty on {selectable.name}", this);
                return;
            }

            if (!referenceSizeCalculated && allSelectables != null && allSelectables.Count > 0)
            {
                globalReferenceSize = GetMostCommonModelDefaultSize(allSelectables);
                referenceSizeCalculated = true;
            }

            float previousSize = selectable.CurrentScaleLevel != null
                ? selectable.CurrentScaleLevel.Size
                : (selectable.ScaleLevels.FirstOrDefault(l => l != null && l.Selected)?.Size
                   ?? selectable.ScaleLevels.FirstOrDefault(l => l != null && l.ModelDefault)?.Size
                   ?? allowedScales.FirstOrDefault());

            selectable.ScaleLevels.RemoveAll(level => level == null || !allowedScales.Contains(level.Size));

            var existingSizes = selectable.ScaleLevels.Select(l => l.Size).ToHashSet();
            foreach (var size in allowedScales)
            {
                if (!existingSizes.Contains(size))
                {
                    selectable.ScaleLevels.Add(new ScaleLevel
                    {
                        Size = size,
                        ScaleZ = 0f,
                        ModelDefault = false,
                        Selected = false
                    });
                }
            }

            // Load / duplicate already have the correct live length + child isolation.
            // Only fresh placement may rewrite ScaleZ ratios and call SetScaleLevel.
            bool preserveHierarchy = selectable.ShouldPreserveLiveLengthScale;
            float liveZ = selectable.transform.localScale.z;

            // Arms/tubes: ScaleZ = Size / authored mesh. Heads/rails (rows meta): historic
            // ModelDefault-relative bake (MD ScaleZ=1) — same as pre-mesh-length init.
            bool rowConfig = selectable.UsesScaleLevelsAsRowConfig();
            float authored = 1f;
            float rowRefSize = 1f;
            if (rowConfig)
            {
                var md = selectable.ScaleLevels.FirstOrDefault(l => l != null && l.ModelDefault)
                    ?? selectable.ScaleLevels.FirstOrDefault(l => l != null && l.Size > 0f);
                rowRefSize = md != null && md.Size > 1e-4f ? md.Size : 1f;
            }
            else
            {
                authored = selectable.GetAuthoredLengthMeters();
                if (authored < 1e-4f)
                    authored = globalReferenceSize > 1e-4f ? globalReferenceSize : 1f;
            }

            foreach (var level in selectable.ScaleLevels)
            {
                if (!preserveHierarchy || level.ScaleZ <= 0.0001f)
                {
                    if (rowConfig)
                        level.ScaleZ = (level.ModelDefault || level.Size <= 0f) ? 1f : level.Size / rowRefSize;
                    else
                        level.ScaleZ = level.Size / authored;
                }
                level.Selected = false;
                level.ModelDefault = false;
            }

            var closest = selectable.ScaleLevels
                .OrderBy(level => Mathf.Abs(level.Size - previousSize))
                .FirstOrDefault();

            if (closest != null)
            {
                closest.ModelDefault = true;
                closest.Selected = true;
            }

            selectable.ScaleLevels = selectable.ScaleLevels.OrderBy(level => level.Size).ToList();

            if (closest != null)
            {
                if (preserveHierarchy)
                {
                    // Re-bake level ScaleZ from the live tube so Size/ref never collapses
                    // a lengthened arm to ScaleZ=1 (duplicate bug).
                    if (rowConfig && closest.Size > 1e-4f)
                    {
                        // New ModelDefault is closest — same ratios as historic init.
                        foreach (var level in selectable.ScaleLevels)
                        {
                            if (level == null) continue;
                            level.ScaleZ = level.Size <= 0f ? 1f : level.Size / closest.Size;
                        }
                        closest.ScaleZ = 1f;
                    }
                    else if (liveZ > 0.0001f && closest.Size > 0.0001f)
                    {
                        foreach (var level in selectable.ScaleLevels)
                        {
                            if (level == null) continue;
                            level.ScaleZ = liveZ * (level.Size / closest.Size);
                        }
                        closest.ScaleZ = liveZ;
                    }

                    ScaleAuditLog.Event("GetAttached.ApplyScaleFilter",
                        $"preserve name={selectable.name} size={closest.Size} liveZ={liveZ:G6} " +
                        $"scaleZ={closest.ScaleZ:G6} rowConfig={rowConfig} dup={selectable.isDuplicated}");
                    selectable.RestoreScaleLevelFromSave(closest);
                }
                else
                {
                    ScaleAuditLog.Event("GetAttached.ApplyScaleFilter",
                        $"apply name={selectable.name} size={closest.Size} scaleZ={closest.ScaleZ:G6}");
                    selectable.SetScaleLevel(closest, setSelected: true, fireEvent: true);
                }
            }

            if (selectable.ScaleLevels.Count == 0)
            {
                Debug.LogWarning($"Filtering resulted in 0 scale levels for {selectable.name}", this);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error filtering scales for {selectable?.name}: {e.Message}", this);
        }
    }

    private float GetMostCommonModelDefaultSize(List<Selectable> allSelectables)
    {
        var defaultSizes = allSelectables
            .SelectMany(s => s.ScaleLevels)
            .Where(l => l != null && l.ModelDefault)
            .GroupBy(l => l.Size)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        return defaultSizes?.Key ?? 1.0f;
    }

    private void LogConfigurationDetails()
    {
        _ = DetermineConfigurationKey();
    }
}
