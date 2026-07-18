using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static UIAlertManager;

/// <summary>
/// Manages boom model configurations, enforcing length restrictions and providing
/// validation for various boom types including Powered, Spring, Fixed, and XL variants.
/// </summary>
public class BoomConfigurationManager : MonoBehaviour
{
    [field: SerializeField] public string Id { get; private set; }

    // Enums for boom types and arm configurations
    public enum BoomType
    {
        PoweredBoom,
        PoweredXLBoom,
        SpringBoom,
        SpringXLBoom,
        FixedBoom,
        FixedXLBoom,
        FixedXXLBoom,
        ServiceHead
    }

    // Configuration/State properties
    [SerializeField] private BoomType _boomType;
    public BoomType CurrentBoomType => _boomType;

    [SerializeField] private int _ceilingTubeLength = 100; // Default 100mm
    public int CeilingTubeLength => _ceilingTubeLength;

    // Arm length properties
    [SerializeField] private int _topArmLength;
    [SerializeField] private int _bottomArmLength;
    public int TopArmLength => _topArmLength;
    public int BottomArmLength => _bottomArmLength;

    // Connected light configuration
    [SerializeField] private int _lightCeilingTubeLength = 350; // Default 350mm
    [SerializeField] private bool _hasTandemLight = false;
    [SerializeField] private bool _hasMultipleArmsOnLight = false;

    // Reference to selectables for arm lengths
    private Selectable _topArmSelectable;
    private Selectable _bottomArmSelectable;
    private List<Selectable> _allSelectables;

    // Valid arm length options by boom type
    private readonly Dictionary<BoomType, List<int>> _validTopArmLengths = new Dictionary<BoomType, List<int>>
    {
        { BoomType.PoweredBoom, new List<int> { 600, 800, 1000, 1200 } },
        { BoomType.PoweredXLBoom, new List<int> { 600, 800, 1000, 1200, 1400, 1600 } },
        { BoomType.SpringBoom, new List<int> { 600, 800, 1000, 1200 } },
        { BoomType.SpringXLBoom, new List<int> { 600, 800, 1000, 1200, 1400, 1600 } },
        { BoomType.FixedBoom, new List<int> { 600, 700, 800, 900, 1000, 1100, 1200 } },
        { BoomType.FixedXLBoom, new List<int> { 600, 700, 800, 900, 1000, 1100, 1200, 1300, 1400, 1500, 1600 } },
        { BoomType.FixedXXLBoom, new List<int> { 600, 700, 800, 900, 1000, 1100, 1200, 1300, 1400 } },
        { BoomType.ServiceHead, new List<int> { 200, 400, 600, 800, 1000 } }
    };

    private readonly Dictionary<BoomType, List<int>> _validBottomArmLengths = new Dictionary<BoomType, List<int>>
    {
        { BoomType.PoweredBoom, new List<int> { 1000 } }, // Fixed articulating length
        { BoomType.PoweredXLBoom, new List<int> { 1000 } }, // Fixed articulating length
        { BoomType.SpringBoom, new List<int> { 1000 } }, // Fixed articulating length
        { BoomType.SpringXLBoom, new List<int> { 1000 } }, // Fixed articulating length
        { BoomType.FixedBoom, new List<int> { 600, 700, 800, 900, 1000, 1100, 1200, 1300, 1400, 1500, 1600 } },
        { BoomType.FixedXLBoom, new List<int> { 600, 700, 800, 900, 1000, 1100, 1200 } },
        { BoomType.FixedXXLBoom, new List<int> { 600, 700, 800, 900, 1000, 1100, 1200, 1300, 1400, 1500, 1600 } },
        { BoomType.ServiceHead, new List<int>() } // Service head doesn't have a bottom arm
    };

    // Ceiling tube length options
    private readonly List<int> _validCeilingTubeLengths = new List<int> { 100, 300 };

    // Start is called before the first frame update
    private IEnumerator Start()
    {
        // Wait for configuration to be loaded
        yield return new WaitUntil(() => !ConfigurationManager.IsLoading);

        try
        {
            // Gather component references
            GatherComponentReferences();

            // Catalog UIButtonName is source of truth for family; prefab _boomType can be stale
            // (e.g. Powered default with a Spring bottom arm installed from the menu).
            ReconcileBoomTypeFromCatalog();

            // Apply valid arm length options based on boom type
            ApplyValidArmLengthOptions();

            // Check for tandem configuration restrictions
            ValidateTandemConfiguration();

            // Check for valid arm length combinations on fixed booms
            ValidateFixedBoomArmLengths();

            // Log configuration details
            LogConfigurationDetails();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error in Start method: {e.Message}", this);
        }
    }

    // GameObject names vary by boom prefab (e.g. "BoomSegment_1", "1000SH_XXL_BoomArm _2XLFixed")
    // and rarely match "TopArm"/"BottomArm" literally. UIButtonName ("Boom - Fixed Top Arm (XL)")
    // is the reliable identifier set by the boom configurator, so check it first.
    private static bool MatchesArmRole(Selectable s, string roleToken, params string[] nameHints)
    {
        if (s == null)
            return false;

        if (!string.IsNullOrEmpty(s.UIButtonName)
            && s.UIButtonName.IndexOf(roleToken, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        string goName = s.gameObject.name;
        return nameHints.Any(h => goName.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsCatalogBottomArm(Selectable s)
    {
        if (s == null || string.IsNullOrEmpty(s.UIButtonName))
            return false;

        string n = s.UIButtonName.ToLowerInvariant();
        if (n.Contains("top arm") || n.Contains("toparm"))
            return false;

        // Catalog SKUs for articulating / bottom arms.
        return n.Contains("bottom arm")
               || n.Contains("bottomarm")
               || n.Contains("arm (powered")
               || n.Contains("spring bottom")
               || n.Contains("fixed bottom");
    }

    private void GatherComponentReferences()
    {
        _allSelectables = GetComponentsInChildren<Selectable>(true).ToList();

        // Find top arm selectable — catalog UIButtonName first.
        _topArmSelectable = _allSelectables.FirstOrDefault(s =>
            MatchesArmRole(s, "Top Arm", "TopArm", "BoomSegment_1", "Segment_1", "UpperArm", "Upper_Arm"));

        // Bottom / articulating arm: catalog labels first (includes "Boom - Arm (Powered XL)"),
        // then mesh hints. Do not match bare "BoomArm" before catalog — that hits MCP geometry.
        _bottomArmSelectable = _allSelectables.FirstOrDefault(IsCatalogBottomArm)
            ?? _allSelectables.FirstOrDefault(s =>
                MatchesArmRole(s, "Bottom Arm", "BottomArm", "LowerArm", "Lower_Arm", "BoomSegment_2", "Segment_2"));

        if (_topArmSelectable == null)
        {
            Debug.LogWarning("Could not find Top Arm selectable", this);
        }

        if (_bottomArmSelectable == null && _boomType != BoomType.ServiceHead)
        {
            Debug.LogWarning("Could not find Bottom Arm selectable", this);
        }
    }

    /// <summary>
    /// Align serialized _boomType with the installed bottom-arm catalog SKU.
    /// Pricing and length rules must follow what was actually placed, not the prefab default.
    /// </summary>
    private void ReconcileBoomTypeFromCatalog()
    {
        if (_bottomArmSelectable == null || string.IsNullOrEmpty(_bottomArmSelectable.UIButtonName))
            return;

        string n = _bottomArmSelectable.UIButtonName.ToLowerInvariant();
        bool topXl = _topArmSelectable != null
                     && !string.IsNullOrEmpty(_topArmSelectable.UIButtonName)
                     && _topArmSelectable.UIButtonName.IndexOf("xl", System.StringComparison.OrdinalIgnoreCase) >= 0;

        BoomType? fromCatalog = null;
        if (n.Contains("spring"))
        {
            fromCatalog = topXl ? BoomType.SpringXLBoom : BoomType.SpringBoom;
        }
        else if (n.Contains("powered") || n.Contains("arm (powered"))
        {
            bool bottomXl = n.Contains("xl");
            fromCatalog = (topXl || bottomXl) ? BoomType.PoweredXLBoom : BoomType.PoweredBoom;
        }
        else if (n.Contains("fixed"))
        {
            // XXL is uncommon; keep FixedXL when either arm is XL.
            fromCatalog = topXl ? BoomType.FixedXLBoom : BoomType.FixedBoom;
        }

        if (fromCatalog.HasValue && fromCatalog.Value != _boomType)
            _boomType = fromCatalog.Value;
    }

    private void ApplyValidArmLengthOptions()
    {
        try
        {
            // Apply length options to top arm
            if (_topArmSelectable != null && _validTopArmLengths.ContainsKey(_boomType))
            {
                ApplyLengthOptionsToSelectable(_topArmSelectable, _validTopArmLengths[_boomType]);
            }

            // Apply length options to bottom arm
            if (_bottomArmSelectable != null && _validBottomArmLengths.ContainsKey(_boomType))
            {
                ApplyLengthOptionsToSelectable(_bottomArmSelectable, _validBottomArmLengths[_boomType]);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error applying arm length options: {e.Message}", this);
        }
    }

    private void ApplyLengthOptionsToSelectable(Selectable selectable, List<int> validLengths)
    {
        if (selectable == null || selectable.ScaleLevels == null)
        {
            Debug.LogWarning($"Cannot apply length options to null selectable or ScaleLevels", this);
            return;
        }

        try
        {
            // Log before filtering
            // Debug.Log($"Before filtering: {selectable.name} has {selectable.ScaleLevels.Count} scale levels", this);

            // Filter scale levels to only include valid lengths
            // Assuming the Size property of scale levels corresponds to arm length in mm
            var filteredScales = selectable.ScaleLevels
                .Where(level => level != null && validLengths.Contains((int)(level.Size * 1000))) // Convert scale to mm
                .ToList();

            // Assign the filtered list
            selectable.ScaleLevels = filteredScales;

            // Log after filtering
            // Debug.Log($"After filtering: {selectable.name} has {selectable.ScaleLevels.Count} scale levels", this);
            // Debug.Log($"Applied scale filter to {selectable.name} - Allowed lengths: {string.Join(", ", validLengths)}", this);

            if (selectable.ScaleLevels.Count == 0)
            {
                Debug.LogWarning($"WARNING: Filtering resulted in zero scale levels for {selectable.name}!", this);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error applying length options to {selectable?.name}: {e.Message}", this);
        }
    }

    private void ValidateTandemConfiguration()
    {
        if (!_hasTandemLight)
            return;

        // Check for valid ceiling tube length combinations
        bool isValidTandemConfig = true;
        string alertMessage = null;

        if (_ceilingTubeLength == 100 && _lightCeilingTubeLength != 350)
        {
            isValidTandemConfig = false;
            alertMessage = "Atypical Configuration: With 100mm Boom ceiling tube, Light ceiling tube should be 350mm";
        }
        else if (_ceilingTubeLength == 300 && _lightCeilingTubeLength != 200)
        {
            isValidTandemConfig = false;
            alertMessage = "Atypical Configuration: With 300mm Boom ceiling tube, Light ceiling tube should be 200mm";
        }
        else if (_ceilingTubeLength == 300 && _hasMultipleArmsOnLight)
        {
            isValidTandemConfig = false;
            alertMessage = "Atypical Configuration: With 300mm Boom ceiling tube, Light should have only single or dual arm";
        }

        // Display alert for atypical configurations but still allow the build
        if (!isValidTandemConfig && alertMessage != null)
        {
            Debug.LogWarning(alertMessage, this);

            // Show an in-game alert if UI system available
            if (UIAlertManager.Instance != null)
            {
                UIAlertManager.Instance.ShowAlert(alertMessage, UIAlertType.Warning);
            }
        }
    }

    private void ValidateFixedBoomArmLengths()
    {
        // Only apply to fixed boom types
        if (_boomType != BoomType.FixedBoom &&
            _boomType != BoomType.FixedXLBoom &&
            _boomType != BoomType.FixedXXLBoom)
            return;

        // Check for maximum total length
        int totalLength = _topArmLength + _bottomArmLength;
        if (totalLength > 2600)
        {
            string alertMessage = $"Atypical Configuration: Total arm length ({totalLength}mm) exceeds maximum of 2600mm";
            Debug.LogWarning(alertMessage, this);

            // Show an in-game alert if UI system available
            if (UIAlertManager.Instance != null)
            {
                UIAlertManager.Instance.ShowAlert(alertMessage, UIAlertType.Warning);
            }
        }
    }

    // Public method to update ceiling tube length
    public void SetCeilingTubeLength(int length)
    {
        if (_validCeilingTubeLengths.Contains(length))
        {
            _ceilingTubeLength = length;

            // Re-validate tandem configuration
            ValidateTandemConfiguration();
        }
        else
        {
            Debug.LogWarning($"Invalid ceiling tube length: {length}mm", this);
        }
    }

    // Public method to update tandem light configuration
    public void SetTandemLightConfiguration(bool hasTandem, int lightCeilingTubeLength, bool hasMultipleArms)
    {
        _hasTandemLight = hasTandem;
        _lightCeilingTubeLength = lightCeilingTubeLength;
        _hasMultipleArmsOnLight = hasMultipleArms;

        // Re-validate tandem configuration
        ValidateTandemConfiguration();
    }

    // Public method to update boom type
    public void SetBoomType(BoomType boomType)
    {
        _boomType = boomType;

        // Re-apply valid arm length options
        ApplyValidArmLengthOptions();

        // Re-validate fixed boom arm lengths
        ValidateFixedBoomArmLengths();
    }

    /// <summary>
    /// Updates serialized boom identity only. Does not mutate arm length options —
    /// used when catalog UIButtonName disagrees with a stale prefab _boomType
    /// (e.g. Spring bottom arm under a Powered BCM default).
    /// </summary>
    public void SyncBoomTypeIdentity(BoomType boomType)
    {
        if (_boomType == boomType)
            return;
        _boomType = boomType;
    }

    // Public method to update arm lengths
    public void SetArmLengths(int topLength, int bottomLength)
    {
        _topArmLength = topLength;
        _bottomArmLength = bottomLength;

        // Re-validate fixed boom arm lengths
        ValidateFixedBoomArmLengths();
    }

    private void LogConfigurationDetails()
    {
        // Debug.Log($"Boom Configuration Summary for {gameObject.name}:", this);
        // Debug.Log($"- Boom Type: {_boomType}", this);
        // Debug.Log($"- Top Arm Length: {_topArmLength}mm", this);
        // Debug.Log($"- Bottom Arm Length: {_bottomArmLength}mm", this);
        // Debug.Log($"- Ceiling Tube Length: {_ceilingTubeLength}mm", this);
        // Debug.Log($"- Has Tandem Light: {_hasTandemLight}", this);

        if (_hasTandemLight)
        {
            // Debug.Log($"- Light Ceiling Tube Length: {_lightCeilingTubeLength}mm", this);
            // Debug.Log($"- Light Has Multiple Arms: {_hasMultipleArmsOnLight}", this);
        }
    }
}

/// <summary>
/// Placeholder class for UI alert system - replace with your actual implementation
/// </summary>
public class UIAlertManager : MonoBehaviour
{
    public static UIAlertManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    public enum UIAlertType
    {
        Info,
        Warning,
        Error
    }

    public void ShowAlert(string message, UIAlertType alertType)
    {
        // Implementation would show the alert in the UI
        // Debug.Log($"UI ALERT [{alertType}]: {message}");
    }
}
