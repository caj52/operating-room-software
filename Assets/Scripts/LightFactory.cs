using System;
using UnityEngine;

/// <summary>
/// Light Factory is a simple runtime light generation system that enables lights to be
/// controlled by UI toggle. 
/// </summary>
public class LightFactory : MonoBehaviour
{
    [Tooltip("Controls if the light should start on or off")]
    [SerializeField] bool on = false;
    [Header("Light Builder")]
    [Tooltip("The point where the light will be positioned on Start")]
    [SerializeField] Transform lightAttachPoint;
    [Tooltip("Sets the tempature(color) of the light")]
    [SerializeField] [Range(1500, 20000)] float tempature = 3000;
    [Tooltip("Sets the intensity of the light's projection")]
    [SerializeField] [Range(1, 50)] float intensity = 1;
    [Tooltip("Sets the inner angle of the light, this is the brightest area without fade")]
    [SerializeField] float innerAngle = 15;
    [Tooltip("Sets the outer angle of the light, this is the fading edges")]
    [SerializeField] float outerAngle = 20;
    Light _light; // Internal reference to the instantiated light

    [Header("Emission Settings")]
    [Tooltip("Set the object containing the material(s) to be emissive when ON")]
    [SerializeField] GameObject emissiveObject;
    Material[] emissiveMaterials; // Internal refs; instances all slots so multi-mat cups glow together
    [Tooltip("The HDR color to emit from the emissiveObject's materials")]
    [SerializeField] Color emissionColor; 

    void Start()
    {
        if (emissiveObject != null)
        {
            var rend = emissiveObject.GetComponent<Renderer>();
            if (rend != null)
            {
                // Instance all materials so U|ONE / U|002 don't share emission state,
                // and every slot on multi-material cups (e.g. LightCups) can glow.
                emissiveMaterials = rend.materials;
                foreach (var mat in emissiveMaterials)
                {
                    if (mat != null && mat.HasProperty("_EmissionColor"))
                        mat.SetColor("_EmissionColor", emissionColor);
                }
            }
        }

        BuildLights();
        // Keep emission in sync with initial on/off (U|ONE was stuck "off" visually).
        ToggleEmissive();
    }

    /// <summary>
    /// This will switch the current On/Off state of the Light
    /// Called from the UI 
    /// </summary>
    public void SwitchLight()
    {
        SetLight(!on);
    }

    /// <summary>Sets the light/emission state explicitly (avoids toggle-listener double flips).</summary>
    public void SetLight(bool isOn)
    {
        on = isOn;
        ToggleEmissive();
        if (_light != null)
            _light.enabled = on;
    }

    /// <summary>
    /// Check if the current state of the light is ON or OFF
    /// </summary>
    /// <returns>True = ON, False = OFF</returns>
    public bool isOn()
    {
        return on;
    }

    /// <summary>
    /// Instantiates the Light component & assigns values from factory settings
    /// </summary>
    void BuildLights()
    {
        if (lightAttachPoint == null)
            lightAttachPoint = FindLightAttachPoint();

        if (lightAttachPoint == null)
        {
            Debug.LogWarning($"[LightFactory] {name}: lightAttachPoint is not assigned — skipping spot light.", this);
            return;
        }

        _light = lightAttachPoint.GetComponent<Light>();
        if (_light == null)
            _light = lightAttachPoint.gameObject.AddComponent<Light>();

        _light.type = LightType.Spot;
        _light.useColorTemperature = true;
        _light.colorTemperature = tempature;
        _light.intensity = intensity;
        _light.innerSpotAngle = innerAngle;
        _light.spotAngle = outerAngle;
        _light.shadows = LightShadows.Soft;
        _light.enabled = on;
    }

    /// <summary>
    /// Older bundles / duplicate factories sometimes leave lightAttachPoint unassigned.
    /// Resolve by name under the owning selectable (or this transform).
    /// </summary>
    Transform FindLightAttachPoint()
    {
        Transform root = transform;
        if (TryGetComponent(out Selectable selfSel))
            root = selfSel.transform;
        else
        {
            Selectable parentSel = GetComponentInParent<Selectable>();
            if (parentSel != null)
                root = parentSel.transform;
        }

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && t.name.StartsWith("LightAttachPoint", StringComparison.OrdinalIgnoreCase))
                return t;
        }

        return null;
    }

    /// <summary>
    /// Sets the emissive boolean state on all materials to match the light's ON/OFF state
    /// </summary>
    void ToggleEmissive()
    {
        if (emissiveMaterials == null || emissiveMaterials.Length == 0)
            return;

        foreach (var mat in emissiveMaterials)
        {
            if (mat == null || !mat.HasProperty("_EmissionColor"))
                continue;

            if (on)
            {
                // URP Lit Emission checkbox is driven by GI flags, not keyword alone.
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emissionColor);
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                mat.SetColor("_EmissionColor", Color.black);
            }
        }
    }
}
