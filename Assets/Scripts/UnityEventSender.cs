using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sends Unity events to specific targets. See <see cref="GameObject.SendMessage(string)"/>
/// </summary>
public class UnityEventSender : MonoBehaviour
{
    [field: SerializeField] private GameObject Target { get; set; }

    //private void OnMouseDown()
    //{
    //    Target.SendMessage("OnMouseDown");
    //}

    //private void OnMouseUp()
    //{
    //    Target.SendMessage("OnMouseUp");
    //}

    //private void OnMouseDrag()
    //{
    //    Target.SendMessage("OnMouseDrag");
    //}

    private void OnMouseEnter()
    {
        Target.SendMessage("OnMouseEnter");
    }

    private void OnMouseExit()
    {
        Target.SendMessage("OnMouseExit");
    }

    private void OnMouseUpAsButton()
    {
        Target.SendMessage("OnMouseUpAsButton");
    }
}