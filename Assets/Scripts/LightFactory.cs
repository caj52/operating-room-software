using Unity.VisualScripting;
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
    [Tooltip("Set the object containing the material to be emissive when ON")]
    [SerializeField] GameObject emissiveObject;
    Material emissiveMaterial; // Internal reference, cannot assign it directly
    [Tooltip("The HDR color to emit from the emissiveObject's material")]
    [SerializeField] Color emissionColor; 

    void Start()
    {
        // We need to get reference to the material though the object's renderer
        emissiveMaterial = emissiveObject.GetComponent<Renderer>().material;
        // Set the emissive color to the material's emission channel
        emissiveMaterial.SetColor("_EmissionColor", emissionColor);

        BuildLights();
    }

    /// <summary>
    /// This will switch the current On/Off state of the Light
    /// Called from the UI 
    /// </summary>
    public void SwitchLight()
    {
        on = !on; //using a simple flip of the initial on boolean's state
        ToggleEmissive();
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
        // We attach the light component to the attach point
        _light = lightAttachPoint.AddComponent<Light>();

        // Then we go through each setting and set them to the light
        _light.type = LightType.Spot;
        _light.useColorTemperature = true; // This switches it from "Color" to a realistic "Temperature" style
        _light.colorTemperature = tempature;
        _light.intensity = intensity;
        _light.innerSpotAngle = innerAngle;
        _light.spotAngle = outerAngle;
        _light.shadows = LightShadows.Soft;
        _light.enabled = on; // As this runs on Start only, here we set the initial ON/OFF state
    }

    /// <summary>
    /// Sets the emissive boolean state in the material to match the light's ON/OFF state
    /// </summary>
    void ToggleEmissive()
    {
        if(on)
        {
            emissiveMaterial.EnableKeyword("_EMISSION");
        }
        else
        {
            emissiveMaterial.DisableKeyword("_EMISSION");
        }
    }
}
