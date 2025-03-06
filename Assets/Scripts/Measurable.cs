using RTG;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class Measurable : MonoBehaviour
{
    public class Measurement
    {
        public Measurement(Measurable measurable) 
        {
            Measurable = measurable;
            Measurable.StartCoroutine(GetMeasurer());
        }

        private IEnumerator GetMeasurer()
        {
            if (!Measurer.Initialized)
            {
                yield return new WaitUntil(() => Measurer.Initialized);
            }
            Measurer = Measurer.GetMeasurer(this);
        }

        public Vector3 Origin { get; set; }
        public Vector3 Direction { get; set; }
        public Vector3 HitPoint { get; set; }
        public Measurer Measurer { get; private set; }
        public Measurable Measurable { get; }
        public MeasurementType MeasurementType { get; set; }
        public RoomBoundaryType RoomBoundaryType { get; set; }
    }

    public static UnityEvent ActiveMeasurablesChanged { get; } = new UnityEvent();
    private static readonly float _lineRendererSizeScalar = 0.005f;
    public List<Measurement> Measurements { get; } = new();
    [field: SerializeField] public List<MeasurementType> MeasurementTypes { get; private set; } = new();
    [field: SerializeField] private bool ForwardOnly { get; set; }
    [field: SerializeField] public bool ShowInElevationPhoto { get; private set; }
    private AttachmentPoint HighestAssemblyAttachmentPoint { get; set; }
    public bool ArmAssemblyActiveInElevationPhotoMode { get; set; }
    public bool IsActive { get; private set; }

    [field: SerializeField] 
    public bool Disabled { get; set; }

    private void Awake()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            enabled = false;
            return;
        }

        MeasurementTypes = MeasurementTypes.Distinct().ToList();
    }

    private void Start()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            enabled = false;
            return;
        }

        Initialize();
    }

    private void Initialize()
    {
        bool newMeasurement = false;
        MeasurementTypes.ForEach(item =>
        {
            if (item == MeasurementType.Walls)
            {
                if (ForwardOnly)
                {
                    Measurements.Add(new Measurement(this)
                    {
                        MeasurementType = item
                    });
                }
                else
                {
                    new List<RoomBoundaryType>()
                    {
                        RoomBoundaryType.WallEast,
                        RoomBoundaryType.WallWest,
                        RoomBoundaryType.WallSouth,
                        RoomBoundaryType.WallNorth,
                    }.ForEach(type =>
                    {
                        Measurements.Add(new Measurement(this)
                        {
                            MeasurementType = item,
                            RoomBoundaryType = type
                        });
                    });
                }
                
            }
            else
            {
                Measurements.Add(new Measurement(this)
                {
                    MeasurementType = item
                });
            }
            newMeasurement = true;
        });

        if (newMeasurement)
        {
            ActiveMeasurablesChanged?.Invoke();
        }

        if (MeasurementTypes.Contains(MeasurementType.ToArmAssemblyOrigin))
        {
            Transform parent = transform.parent;
            AttachmentPoint attachmentPoint = null;
            while (parent != null)
            {
                //Debug.Log($"{parent.gameObject.name}");
                if (parent.gameObject.TryGetComponent<AttachmentPoint>(out var point))
                {
                    attachmentPoint = point;
                }

                parent = parent.parent;
            }

            if (attachmentPoint == null)
            {
                throw new Exception("Could not find attachment point for arm origin measurable");
            }

            HighestAssemblyAttachmentPoint = attachmentPoint;
        }
    }

    public void SetActive(bool active)
    {
        IsActive = active && !Disabled;

        Measurements.ToList().ForEach(item =>
        {
            item.Measurer.gameObject.SetActive(IsActive);
            item.Measurer.LineRenderers.ForEach(renderer => renderer.enabled = item.MeasurementType == MeasurementType.ToArmAssemblyOrigin && IsActive);
        });
        ActiveMeasurablesChanged?.Invoke();
    }

    private void OnDestroy()
    {
        Measurements.ToList().ForEach(item =>
        {
            if (item.Measurer != null)
            {
                Destroy(item.Measurer.gameObject);
            }
        });
    }

    private int GetTotalNeededMeasurements()
    {
        int count = 0;
        MeasurementTypes.ForEach(item =>
        {
            switch (item)
            {
                case MeasurementType.Walls:
                    count += 4;
                    break;
                case MeasurementType.Floor:
                case MeasurementType.Ceiling:
                case MeasurementType.ToArmAssemblyOrigin:
                    count++;
                    break;
            }
        });
        return count;
    }

    private static Dictionary<RoomBoundaryType, Vector3> _wallDirectionVectors = new ()
    {
        { RoomBoundaryType.WallNorth, Vector3.forward },
        { RoomBoundaryType.WallSouth, -Vector3.forward },
        { RoomBoundaryType.WallEast, Vector3.right },
        { RoomBoundaryType.WallWest, -Vector3.forward }
    };

    private void UpdateMeasurementViaRaycast(Vector3 direction, Measurement measurement, bool ignoreSelectables = false)
    {
        Ray ray = new Ray(transform.position, direction);
        int mask = LayerMask.GetMask("Wall", "Selectable");
        if (ignoreSelectables)
        {
            mask = LayerMask.GetMask("Wall");
        }

        bool floorIsOn = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor).gameObject.activeSelf;

        if (!floorIsOn)
        {
            RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor).gameObject.SetActive(true);
        }

        if (Physics.Raycast(ray, out RaycastHit raycastHit, 1000f, mask))
        {
            var obj = raycastHit.collider.gameObject;
            if (obj.layer == LayerMask.NameToLayer("Wall") || obj.CompareTag("Wall"))
            {
                measurement.Origin = ray.origin;
                measurement.HitPoint = raycastHit.point;
            }
        }

        if (!floorIsOn)
        {
            RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor).gameObject.SetActive(false);
        }
    }

    private float GetDistanceToCameraPlane(Vector3 point, Camera camera = null)
    {
        if (camera == null) 
        {
            camera = Camera.main;
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
        Plane nearFrustrumPlane = planes[4];

        Vector3 planePoint = nearFrustrumPlane.ClosestPointOnPlane(point);
        return Vector3.Distance(point, planePoint);
    }

    public void UpdateMeasurements(ref float heightMod, Camera camera = null)
    {
        if (camera == null)
        {
            camera = Camera.main;
        }

        foreach (var item in Measurements)
        {
            switch (item.MeasurementType)
            {
                case MeasurementType.Walls:
                    if (ForwardOnly)
                    {
                        UpdateMeasurementViaRaycast(transform.forward, item);
                    }
                    else
                    {
                        UpdateMeasurementViaRaycast(_wallDirectionVectors[item.RoomBoundaryType], item);
                    }
                    break;
                case MeasurementType.Ceiling:
                    UpdateMeasurementViaRaycast(Vector3.up, item);
                    break;
                case MeasurementType.Floor:
                    UpdateMeasurementViaRaycast(Vector3.down, item, true);
                    if (Selectable.IsInElevationPhotoMode)
                    {
                        item.Measurer.UpdateTransform(camera);
                    }
                    break;
                case MeasurementType.ToArmAssemblyOrigin:
                    Vector3 addedHeight = Vector3.up * heightMod;
                    Vector3 origin = transform.position;
                    item.HitPoint = HighestAssemblyAttachmentPoint.transform.position + addedHeight;
                    origin.y = HighestAssemblyAttachmentPoint.transform.position.y;
                    item.Origin = origin + addedHeight;
                    
                    var measurer = item.Measurer;

                    if (Selectable.IsInElevationPhotoMode)
                    {
                        measurer.UpdateTransform(camera);
                    }

                    measurer.LineRenderers[0].enabled = true;
                    measurer.LineRenderers[0].positionCount = 2;
                    Vector3 line1Start = addedHeight + origin;
                    Vector3 line1End = transform.position;
                    measurer.LineRenderers[0].SetPosition(0, line1Start);
                    measurer.LineRenderers[0].SetPosition(1, line1End);
                    measurer.LineRenderers[0].startWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(line1Start, camera);
                    measurer.LineRenderers[0].endWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(line1End, camera);

                    measurer.LineRenderers[0].enabled = true;
                    measurer.LineRenderers[1].positionCount = 2;
                    Vector3 line2Start = addedHeight + HighestAssemblyAttachmentPoint.transform.position;
                    Vector3 line2End = HighestAssemblyAttachmentPoint.transform.position;
                    measurer.LineRenderers[1].SetPosition(0, line2Start);
                    measurer.LineRenderers[1].SetPosition(1, line2End);
                    measurer.LineRenderers[1].startWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(line2Start, camera);
                    measurer.LineRenderers[1].endWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(line2End, camera);
                    heightMod += heightMod;

                    break;
            }
        }
    }

    private void Update()
    {
        if (!IsActive) return;
        float _ = 0;
        UpdateMeasurements(ref _);
    }
}

public enum MeasurementType
{ 
    Walls,
    Floor,
    Ceiling,
    ToArmAssemblyOrigin
}
    