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
using UnityEngine.UI;

public class Measurable : MonoBehaviour
{

    [System.Serializable]
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
    private Selectable proxyAlertWithORTABLE;
    public bool IsActive { get; private set; }
    [SerializeField] float minThreshold = 1;
    [SerializeField] float maxThreshold = 2;
    [SerializeField] bool isAdjustmentNeeded;


    [field: SerializeField]
    public bool Disabled { get; set; }

    private void OnEnable()
    {
        EventManager.OnCompareProximatryAlertWithOR_Table += CheckProximity;
     
    }

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
        proxyAlertWithORTABLE = FindObjectsByType<Selectable>(FindObjectsSortMode.None)
    .FirstOrDefault(x => x.MetaData.Name == "OR_Table_0");
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
            if (item.Measurer)
            {
                item.Measurer.gameObject.SetActive(IsActive);
                item.Measurer.LineRenderers.ForEach(renderer => renderer.enabled = item.MeasurementType == MeasurementType.ToArmAssemblyOrigin && IsActive);
            }
           
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
        EventManager.OnCompareProximatryAlertWithOR_Table -= CheckProximity;
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

    private static Dictionary<RoomBoundaryType, Vector3> _wallDirectionVectors = new()
    {
        { RoomBoundaryType.WallNorth, Vector3.forward },
        { RoomBoundaryType.WallSouth, -Vector3.forward },
        { RoomBoundaryType.WallEast, Vector3.right },
        { RoomBoundaryType.WallWest, -Vector3.forward }
    };

    private void UpdateMeasurementViaRaycast(Vector3 direction, Measurement measurement, bool ignoreSelectables = false)
    {
        Ray ray = new Ray(transform.position, direction);
        int mask = LayerMask.GetMask("Wall");
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
            if (obj.layer == LayerMask.NameToLayer("Wall") || obj.CompareTag("Wall") || obj.CompareTag("Baseboard"))
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
        //CheckProximity(GetComponentInParent<Selectable>().gameObject, 1, 2);

        if (!IsActive) return;
        float _ = 0;
        UpdateMeasurements(ref _);
    }

    public void CheckProximity(GameObject referenceObject, float minThresholdDistance, float maxThresholdDistance)
    {
        if (UI_ToggleProximityAlerts.IsActive)
        {

            Selectable selectable = referenceObject.GetComponent<Selectable>();

            if (selectable.SpecialTypes.Count == 0)
                return;
            if (selectable.SpecialTypes[0].Equals(SpecialSelectableType.Mount) && proxyAlertWithORTABLE)
            {
                Vector3 refPos = referenceObject.transform.position;
                Vector3 tablePos = proxyAlertWithORTABLE.transform.position;

                Vector3 adjustedRefPos = new Vector3(refPos.x, 0, refPos.z);
                Vector3 adjustedTablePos = new Vector3(tablePos.x, 0, tablePos.z);

                float distance = Vector3.Distance(adjustedRefPos, adjustedTablePos);
                float moveAwayBy = minThresholdDistance - distance;
                float moveCloserBy = distance - maxThresholdDistance;

                if (distance < minThresholdDistance)
                {
                    UI_DialogPrompt.Open($"{selectable.MetaData.Name} is TOO CLOSE to OR_Table_0 . Suggested Adjustment: Move it farther by {moveAwayBy:F2} meters.",
                    new ButtonAction
                    {
                        //ButtonText = "Auto Placement",
                        //Action = () =>
                        //{
                        //    isAdjustmentNeeded = false;
                        //    Vector3 newPosition = MoveAway(referenceObject, tablePos, moveAwayBy);
                        //    referenceObject.transform.position = newPosition;
                        //    Debug.Log($"Auto-Moving {selectable.MetaData.Name} farther by {moveAwayBy:F2} meters.");
                        //    UI_DialogPrompt.Close();
                        //},
                        ButtonText = "OK",
                        Action = () =>
                        {
                            isAdjustmentNeeded = true;
                            UI_DialogPrompt.Close();
                        },

                    }
                   //new ButtonAction
                   //{
                   //    ButtonText = "Cancel",
                   //    Action = () =>
                   //    {
                   //        UI_DialogPrompt.Close();
                   //    },
                   //}
                   );
                }
                else if (distance > maxThresholdDistance)
                {
                    Vector3 newPosition = MoveCloser(referenceObject, tablePos, moveCloserBy);
                    Debug.Log($"Auto-Moving {selectable.MetaData.Name} closer by {moveCloserBy:F2} meters.");
                    UI_DialogPrompt.Open($"{selectable.MetaData.Name} is TOO FAR from OR_Table_0.Suggested Adjustment: Move it closer by {moveCloserBy:F2} meters.",
                     new ButtonAction
                     {
                         ButtonText = "Ok",
                         Action = () =>
                         {
                             UI_DialogPrompt.Close();
                             isAdjustmentNeeded = true;
                         },
                     }
                    );
                }
                else
                {
                    if (isAdjustmentNeeded)
                    {
                        Debug.Log($"Anas => {selectable.MetaData.Name} is at an IDEAL DISTANCE from OR_Table_0");
                        UI_DialogPrompt.Open($"{selectable.MetaData.Name} is at an IDEAL DISTANCE from OR_Table_0.No adjustment needed",
                         new ButtonAction
                         {
                             ButtonText = "Ok",
                             Action = () =>
                             {
                                 UI_DialogPrompt.Close();
                                 isAdjustmentNeeded = false;
                             },
                         }
                        );
                    }
                }
            }
        }

    }
    private Vector3 MoveAway(GameObject obj, Vector3 tablePos, float moveBy)
    {
        Vector3 direction = (obj.transform.position - tablePos).normalized;
        Vector3 newPosition = new Vector3(obj.transform.position.x + (direction.x * moveBy), obj.transform.position.y, obj.transform.position.z + (direction.z * moveBy));

        return FindValidPosition(newPosition, obj);
    }
    private Vector3 MoveCloser(GameObject obj, Vector3 tablePos, float moveBy)
    {
        float offset = 3;
        Vector3 direction = (tablePos - obj.transform.position).normalized;
        Vector3 newPosition = new Vector3(obj.transform.position.x + (direction.x * moveBy) + offset, obj.transform.position.y, obj.transform.position.z + (direction.z * moveBy) + offset);

        return FindValidPosition(newPosition, obj);
    }
    private Vector3 FindValidPosition(Vector3 targetPosition, GameObject obj)
    {
        int maxAttempts = 8;
        float offset = 0.5f;
        int layerMask = ~LayerMask.GetMask("Wall");

        for (int i = 0; i < maxAttempts; i++)
        {
            if (!Physics.CheckSphere(targetPosition, 0.1f, layerMask))
            {
                Debug.Log($"Valid position found for {obj.name} at {targetPosition}");
                return targetPosition;
            }

            // Try shifting in multiple directions instead of just one
            targetPosition += new Vector3(offset * (i % 2 == 0 ? 1 : -1), 0, offset * (i % 3 == 0 ? 1 : -1));
        }

        Debug.LogWarning($"{obj.name} could not find a valid position after multiple attempts.");
        return obj.transform.position;
    }
    //private Vector3 FindValidPosition(Vector3 targetPosition, GameObject obj)
    //{
    //    int maxAttempts = 5; 
    //    float offset = 0.5f;
    //    Debug.Log("Checking the Adjusted Location");
    //    int layerMask = ~LayerMask.GetMask("Wall");

    //    for (int i = 0; i < maxAttempts; i++)
    //    {
    //        if (!Physics.CheckSphere(targetPosition, 0.2f, layerMask)) 
    //        {

    //            Debug.Log("Checking the Adjusted Location 1");
    //            return targetPosition;
    //        }

    //        targetPosition += new Vector3(offset, 0, offset);

    //        Debug.Log("Checking the Adjusted Location 2");
    //    }

    //    Debug.LogWarning($"{obj.name} could not find a valid position after multiple attempts.");
    //    return obj.transform.position;
    //}
}

public enum MeasurementType
{
    Walls,
    Floor,
    Ceiling,
    ToArmAssemblyOrigin
}
