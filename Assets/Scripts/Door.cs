using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Door : MonoBehaviour
{
    [field: SerializeField] private float DoorWidthInFT { get; set; } = 2f;
    private DoorTrigger frontTrigger;
    private DoorTrigger backTrigger;
    private void Awake()
    {
        CreateTriggerZones();
        GetComponent<Selectable>().OnPlaced.AddListener(CreateTriggerZones);
    }

    private void OnDestroy()
    {
        GetComponent<Selectable>().OnPlaced.RemoveListener(CreateTriggerZones);
    }

    private void CreateTriggerZones()
    {
        Vector3 doorPosition = transform.position;
        Vector3 doorForward = transform.forward;

        Vector3 frontTriggerPosition = doorPosition + doorForward * DoorWidthInFT.ToMeters() * 0.5f;
        Vector3 backTriggerPosition = doorPosition - doorForward * DoorWidthInFT.ToMeters() * 0.5f;

        if (frontTrigger == null)
        {
            frontTrigger = CreateTrigger(frontTriggerPosition, backTriggerPosition);
        }
        else
        {
            frontTrigger.SetDestination(backTriggerPosition);
        }

        if (backTrigger == null)
        {
            backTrigger = CreateTrigger(backTriggerPosition, frontTriggerPosition);
        }
        else
        {
            backTrigger.SetDestination(frontTriggerPosition);
        }
    }

    private DoorTrigger CreateTrigger(Vector3 position, Vector3 dest)
    {
        GameObject trigger = new GameObject("TriggerZone");
        trigger.transform.position = position;
        BoxCollider coll = trigger.AddComponent<BoxCollider>();
        coll.isTrigger = true;
        coll.size = new Vector3(DoorWidthInFT.ToMeters(), 2f, 1f);
        coll.center = new Vector3(0, 1, 0);
        trigger.transform.SetParent(transform);
        return trigger.AddComponent<DoorTrigger>().SetDestination(dest);
    }
}