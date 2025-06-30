using RTG;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BoomOutletValidator : MonoBehaviour
{
    // Dictionary to track outlet counts per panel
    private Dictionary<GameObject, int> outletCounts = new Dictionary<GameObject, int>();

    // Constants
    private const string HV_POWER_OUTLET = "HV Power Outlet";
    private const string ETHERNET_OUTLET = "EthernetOutlet";
    private const string MISMATCH_ELECTRICAL_MESSAGE = "Mismatch electrical configurations are not permitted, electrical configurations must be consistent";
    private const string MISMATCH_ELECTRICAL_GAS_MESSAGE = "Mismatch configurations are not permitted, electrical and gas configurations cannot be placed back to back";

    // List of gas outlet types
    private static readonly List<string> outletTypes = new List<string>
    {
        "Gas Outlet (CO2)",
        "Gas Outlet (He-O2)",
        "Gas Outlet (Medical Air)",
        "Gas Outlet (Instrument Air)",
        "Gas Outlet (Nitrogen)",
        "Gas Outlet (Nitrous Oxide)",
        "Gas Outlet (O2-He)",
        "Gas Outlet (Oxygen)",
        "Gas Outlet (WAGD Evac)",
        "Gas Outlet (Vacuum Suction)"
    };

    // Panel mapping dictionaries
    private readonly Dictionary<string, string> frontToBackPanelMapping = new Dictionary<string, string>
    {
        { "BoomHeadPanel_1_front", "BoomHeadPanel_10_back" },
        { "BoomHeadPanel_2_front", "BoomHeadPanel_11_back" },
        { "BoomHeadPanel_3_front", "BoomHeadPanel_12_back" },
        { "BoomHeadPanel_4_front", "BoomHeadPanel_7_back" },
        { "BoomHeadPanel_5_front", "BoomHeadPanel_8_back" },
        { "BoomHeadPanel_6_front", "BoomHeadPanel_9_back" },
        { "BoomHeadPanel_15_front", "BoomHeadPanel_13_back" },
        { "BoomHeadPanel_13_front", "BoomHeadPanel_19_back" },
        { "BoomHeadPanel_18_front", "BoomHeadPanel_20_back" },
        { "BoomHeadPanel_16_front", "BoomHeadPanel_14_back" },
    };

    private readonly Dictionary<string, string> adjacentFrontPanelMapping = new Dictionary<string, string>
    {
        { "BoomHeadPanel_1_front", "BoomHeadPanel_4_front" },
        { "BoomHeadPanel_2_front", "BoomHeadPanel_5_front" },
        { "BoomHeadPanel_3_front", "BoomHeadPanel_6_front" },
        { "BoomHeadPanel_15_front", "BoomHeadPanel_17_front" },
        { "BoomHeadPanel_16_front", "BoomHeadPanel_18_front" },
    };

    private readonly Dictionary<string, string> adjacentBackPanelMapping = new Dictionary<string, string>
    {
        { "BoomHeadPanel_7_back", "BoomHeadPanel_10_back" },
        { "BoomHeadPanel_8_back", "BoomHeadPanel_11_back" },
        { "BoomHeadPanel_9_back", "BoomHeadPanel_12_back" },
        { "BoomHeadPanel_13_back", "BoomHeadPanel_19_back" },
        { "BoomHeadPanel_14_back", "BoomHeadPanel_20_back" },
    };

    // Validation properties
    private int frontCount;
    private int backCount;

    public void ValidateBoomConfiguration(GameObject parent)
    {
        // Count components on panel
        int electricalCount = CountComponentsOnPanel(parent, HV_POWER_OUTLET);
        int gasCount = CountGasOutlets(parent);
        int dataCount = CountComponentsOnPanel(parent, ETHERNET_OUTLET);

        // Store electrical counts for this panel for pair validation
        outletCounts[parent] = electricalCount;

        Debug.Log($"[{parent.name}] Electrical Count: {electricalCount}, Gas Count: {gasCount}, Data Count: {dataCount}");

        // Check electrical and gas incompatibilities
        ValidateAdjacentPanelCompatibility(parent, electricalCount, gasCount, adjacentFrontPanelMapping);
        ValidateAdjacentPanelCompatibility(parent, electricalCount, gasCount, adjacentBackPanelMapping);

        // Check electrical configuration mismatches between front and back panels
        if (electricalCount >= 2)
        {
            // Validate paired front/back panel
            ValidatePairedPanel(parent);
        }
    }

    private int CountComponentsOnPanel(GameObject panel, string componentName)
    {
        Debug.Log($"Counting components on panel: {panel.name} for component: {componentName}");
        return panel.GetComponentsInChildren<Selectable>()
                   .Count(s => s.MetaData.Name == componentName);
    }

    private int CountGasOutlets(GameObject panel)
    {
        var selectables = panel.GetComponentsInChildren<Selectable>();
        return selectables.Count(s => outletTypes.Contains(s.MetaData.Name));
    }

    private void ValidateAdjacentPanelCompatibility(GameObject panel, int electricalCount, int gasCount, Dictionary<string, string> panelMapping)
    {
        if (electricalCount == 0 && gasCount == 0) return;

        // Find matching panel name in mapping
        string currentPanelKey = null;
        string adjacentPanelName = null;

        foreach (var pair in panelMapping)
        {
            if (panel.name.Contains(pair.Key))
            {
                currentPanelKey = pair.Key;
                adjacentPanelName = pair.Value;
                break;
            }
            else if (panel.name.Contains(pair.Value))
            {
                currentPanelKey = pair.Value;
                adjacentPanelName = pair.Key;
                break;
            }
        }

        // If we found a matching adjacent panel, check for incompatibility
        if (!string.IsNullOrEmpty(adjacentPanelName))
        {
            CheckForIncompatibleConfiguration(panel, adjacentPanelName);
        }
    }

    private void CheckForIncompatibleConfiguration(GameObject currentPanel, string adjacentPanelName)
    {
        Transform parentTransform = currentPanel.transform.parent;
        GameObject adjacentPanel = FindChildPanelByName(parentTransform, adjacentPanelName);

        if (adjacentPanel != null)
        {
            int currentPanelElectricalCount = CountComponentsOnPanel(currentPanel, HV_POWER_OUTLET);
            int currentPanelGasCount = CountGasOutlets(currentPanel);

            int adjacentPanelElectricalCount = CountComponentsOnPanel(adjacentPanel, HV_POWER_OUTLET);
            int adjacentPanelGasCount = CountGasOutlets(adjacentPanel);

            // Show error if one panel has electrical and the other has gas
            if ((currentPanelElectricalCount > 0 && adjacentPanelGasCount > 0) ||
                (currentPanelGasCount > 0 && adjacentPanelElectricalCount > 0))
            {
                ShowErrorDialog(MISMATCH_ELECTRICAL_GAS_MESSAGE);
            }
        }
    }

    private GameObject FindChildPanelByName(Transform parent, string panelName)
    {
        return parent.GetChildren().FirstOrDefault(a => a.name.Contains(panelName))?.gameObject;
    }

    private void ValidatePairedPanel(GameObject currentPanel)
    {
        string matchedFrontPanel = null;
        string matchedBackPanel = null;

        // Check if this is a front panel
        if (frontToBackPanelMapping.TryGetValue(GetPanelBaseNameFrom(currentPanel.name), out string backPanelName))
        {
            matchedFrontPanel = GetPanelBaseNameFrom(currentPanel.name);
            matchedBackPanel = backPanelName;
        }
        // Check if this is a back panel
        else
        {
            var frontBackPair = frontToBackPanelMapping.FirstOrDefault(x =>
                GetPanelBaseNameFrom(currentPanel.name) == x.Value);

            if (!string.IsNullOrEmpty(frontBackPair.Key))
            {
                matchedFrontPanel = frontBackPair.Key;
                matchedBackPanel = GetPanelBaseNameFrom(currentPanel.name);
            }
        }

        if (matchedFrontPanel != null && matchedBackPanel != null)
        {
            ValidateFrontBackPairByName(currentPanel.transform.parent, matchedFrontPanel, matchedBackPanel);
        }
    }

    private string GetPanelBaseNameFrom(string fullName)
    {
        // Extract just the panel name pattern from the full GameObject name
        foreach (var frontName in frontToBackPanelMapping.Keys)
        {
            if (fullName.Contains(frontName)) return frontName;
        }
        foreach (var backName in frontToBackPanelMapping.Values)
        {
            if (fullName.Contains(backName)) return backName;
        }
        return fullName;
    }

    private void ValidateFrontBackPairByName(Transform parentTransform, string frontName, string backName)
    {
        GameObject front = FindChildPanelByName(parentTransform, frontName);
        GameObject back = FindChildPanelByName(parentTransform, backName);

        if (front != null && back != null)
        {
            ValidateFrontBackPair(front, back);
        }
    }

    public bool ValidateFrontBackPair(GameObject front, GameObject back)
    {
        frontCount = outletCounts.TryGetValue(front, out int fCount) ? fCount : 0;
        backCount = outletCounts.TryGetValue(back, out int bCount) ? bCount : 0;
        int totalCount = front.GetComponentsInChildren<Selectable>().Length + back.GetComponentsInChildren<Selectable>().Length; 
        //to check the total outlets count 7 because this will return the parent selectables as well so 2+3+3 = 8
        if (totalCount>7)
        {
            // Validate that electrical configurations match between front and back
            if (frontCount >= 2 && backCount >= 2 && frontCount != backCount)
            {
                Debug.LogError($"❌ Mismatch! Front ({front.name}) has {frontCount}, Back ({back.name}) has {backCount} electrical outlets.");
                ShowErrorDialog(MISMATCH_ELECTRICAL_MESSAGE);
                return false;
            }
        }
      

        Debug.Log($"✅ Match! Front has {frontCount} and back has {backCount} electrical outlets.");
        return true;
    }

    private void ShowErrorDialog(string message)
    {
        UI_DialogPrompt.Open(
            message,
            new ButtonAction("Ok")
        );
    }
}