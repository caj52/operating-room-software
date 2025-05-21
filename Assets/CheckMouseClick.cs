using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CheckMouseClick : MonoBehaviour
{


    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit))
            {
                if (hit.transform.CompareTag("Selectable"))
                {
                Debug.Log("Hit: " + hit.transform.name);
                }
            }
        }
    }
   /* private void AttachModelToPoint()
    {
        preParedObject.transform.position = AttachPoint.transform.position;
        preParedObject.transform.rotation = AttachPoint.transform.rotation;
        preParedObject.SetActive(true);
    }*/
}
