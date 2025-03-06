using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class DoorTrigger : MonoBehaviour
{
    [field: SerializeField, HideInInspector] private Vector3 Destination;
    private bool isFloor;

    public DoorTrigger SetDestination(Vector3 dest)
    {
        Destination = dest;
        isFloor = CheckColliderUnderDestination();
        return this;
    }

    private void OnTriggerStay(Collider collider)
    {
        if((Input.GetMouseButtonUp(1) || Input.GetKeyUp(KeyCode.Space)) && collider.gameObject.layer == 8 && isFloor)
        {
            collider.gameObject.transform.root.position = Destination;
        }
    }

    private bool CheckColliderUnderDestination()
    {
        int layerMask = 1 << 7;
        if (Physics.Raycast(new Vector3(Destination.x, 1f, Destination.z), Vector3.down, out RaycastHit hit, 10, layerMask))
        {
            return hit.collider != null;
        }
        return false;
    }
}
