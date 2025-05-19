using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages restrictions between boom and light ceiling tubes in tandem configurations.
/// </summary>
public class TandomRestrictions : MonoBehaviour
{
    // Components
    private Selectable[] selectables;
    private Selectable lightObject;
    private Selectable boomObject;

    // Selected scale levels
    private Selectable.ScaleLevel selectedLightScale;
    private Selectable.ScaleLevel selectedBoomScale;

    // Constants for scale sizes
    private const float BOOM_SIZE_100MM = 0.1f;
    private const float BOOM_SIZE_300MM = 0.3f;
    private const float LIGHT_SIZE_200MM = 0.2f;
    private const float LIGHT_SIZE_350MM = 0.35f;

    /// <summary>
    /// Validates tandem restrictions between boom and light objects.
    /// </summary>
    /// <param name="selectableObject">The parent game object containing selectable components</param>
    public void CheckTandemRestrictions(GameObject selectableObject)
    {
        if (selectableObject == null)
        {
            Debug.LogError("CheckTandemRestrictions: Selectable object is null");
            return;
        }

        Debug.Log($"Checking Tandem Restrictions... {selectableObject.GetComponent<Selectable>()?.MetaData?.Name}");

        // Find all selectable components with scale levels
        FindSelectableComponents(selectableObject);

        // Find and extract specific components
        if (!FindRequiredComponents())
        {
            return; // Required components not found
        }

        // Get selected scale levels
        ExtractSelectedScaleLevels();

        // Check for restriction violations
        string warning = CheckForRestrictionViolations();

        // Show warning if needed
        if (!string.IsNullOrEmpty(warning))
        {
            ShowTandemWarning(warning);
        }
    }

    /// <summary>
    /// Finds all selectable components with scale levels in the given object.
    /// </summary>
    private void FindSelectableComponents(GameObject selectableObject)
    {
        selectables = selectableObject.GetComponentsInChildren<Selectable>()
            .Where(a => a.ScaleLevels != null && a.ScaleLevels.Count > 0)
            .ToArray();
    }

    /// <summary>
    /// Finds the required light and boom components.
    /// </summary>
    /// <returns>True if all required components were found, false otherwise</returns>
    private bool FindRequiredComponents()
    {
        // Find light object
        lightObject = selectables.FirstOrDefault(a =>
            a.gameObject.name.StartsWith("Sim.FLEX Ceiling") ||
            a.name.Contains("ArmDropTube.001"));

        if (lightObject == null)
        {
            Debug.LogWarning("Light object not found");
            return false;
        }

        // Find boom object
        boomObject = selectables.FirstOrDefault(a =>
            a.name.StartsWith("BoomDropTube.001"));

        if (boomObject == null)
        {
            Debug.LogWarning("Boom object not found");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts the currently selected scale levels for light and boom objects.
    /// </summary>
    private void ExtractSelectedScaleLevels()
    {
        var lightScales = lightObject.ScaleLevels.Where(a => a.Selected).ToArray();
        var boomScales = boomObject.ScaleLevels.Where(a => a.Selected).ToArray();

        selectedLightScale = lightScales.FirstOrDefault();
        selectedBoomScale = boomScales.FirstOrDefault();
    }

    /// <summary>
    /// Checks for violations of tandem restrictions.
    /// </summary>
    /// <returns>A warning message if restrictions are violated, null otherwise</returns>
    private string CheckForRestrictionViolations()
    {
        if (selectedBoomScale == null || selectedLightScale == null)
        {
            return null;
        }

        if (Mathf.Approximately(selectedBoomScale.Size, BOOM_SIZE_100MM) &&
            !Mathf.Approximately(selectedLightScale.Size, LIGHT_SIZE_350MM))
        {
            return "Recommended: If Boom ceiling tube is 100mm, Light ceiling tube should be 350mm.";
        }
        else if (Mathf.Approximately(selectedBoomScale.Size, BOOM_SIZE_300MM) &&
                 !Mathf.Approximately(selectedLightScale.Size, LIGHT_SIZE_200MM))
        {
            return "Recommended: If Boom ceiling tube is 300mm, Light ceiling tube should be 200mm. \n" +
                   "With a 300mm Boom ceiling tube, only Single or Dual light arms are suggested.";
        }

        return null;
    }

    /// <summary>
    /// Displays a warning dialog to the user about tandem restrictions.
    /// </summary>
    /// <param name="message">The warning message to display</param>
    private void ShowTandemWarning(string message)
    {
        Debug.LogWarning($"[Tandem Restriction] {message}");
        UI_DialogPrompt.Open(message, new ButtonAction("OK"));
    }
}