using RTG;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;

public class Measurer : MonoBehaviour
{
    //public static List<Measurer> Measurers = new List<Measurer>();
    private static GameObject Prefab { get; set; }
    public UnityEvent ActiveStateToggled = new();
    public UnityEvent VisibilityToggled = new();
    public MeshRenderer Renderer { get; private set; }
    public List<LineRenderer> LineRenderers { get; private set; } = new();
    public MeasurementText MeasurementText;
    //public bool AllowInElevationPhotoMode => Measurement != null && Measurement.Measurable.ArmAssemblyActiveInElevationPhotoMode && Measurement.MeasurementType == MeasurementType.ToArmAssemblyOrigin;
    public Measurable.Measurement Measurement { get; private set; }
    public string Distance { get; private set; } = string.Empty;
    private Transform _childTransform;
    public Vector3 TextPosition => _childTransform.position;
    public static bool Initialized { get; private set; }

    private void OnEnable()
    {
        if (MeasurementText != null)
            MeasurementText.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        if (MeasurementText != null)
            MeasurementText.gameObject.SetActive(false);
    }

    private void Update()
    {
        UpdateTransform();
    }

    public static Measurer GetMeasurer(Measurable.Measurement measurement) 
    {
        var newObj = Instantiate(Prefab);
        var measurer = newObj.GetComponent<Measurer>();
        measurer.Measurement = measurement;
        return measurer;
    }

    public void UpdateTransform(Camera camera = null)
    {
        if (camera == null)
        {
            camera = Camera.main;
        }

        transform.position = Measurement.Origin;
        transform.LookAt(Measurement.HitPoint);
        float distanceMeters = Vector3.Distance(Measurement.Origin, Measurement.HitPoint);
        float distanceFeet = Mathf.Floor(distanceMeters.ToFeet());
        float distanceInches = Mathf.Round((distanceMeters.ToFeet() - distanceFeet) * 12f * 10f) / 10f;
        Distance = $"{distanceFeet}' {distanceInches}\"";
        transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y, distanceMeters);
        MeasurementText.UpdateVisibilityAndPosition(camera);
    }

    private void Awake()
    {
        if (Prefab == null)
        {
            Prefab = gameObject;
            Initialized = true;
            gameObject.SetActive(false);
            return;
        }

        MeasurementText = MeasurementText.GetMeasurementText(this);
        _childTransform = transform.GetChild(0);
        Renderer = GetComponentInChildren<MeshRenderer>();
        LineRenderers = GetComponentsInChildren<LineRenderer>(true).ToList();
        LineRenderers.ForEach(x => x.enabled = false);
    }

    private void OnDestroy()
    {
        if (MeasurementText != null)
            Destroy(MeasurementText.gameObject);
    }
}
