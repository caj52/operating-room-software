using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class RoomsPositionBtn : MonoBehaviour
{
    public Button button;
    public Button deleteRoombutton;
    public Vector3 roomLocation;
    public GameObject currentRoom;
    private DuplicateRoom room;
    public List<GameObject> roomGameObjects;
    public TextMeshProUGUI headingText;

  


    private void Start()
    {
        room = FindObjectOfType<DuplicateRoom>(true);
        button = GetComponent<Button>();
        button.onClick.AddListener(()=>OnButtonClick());
        headingText = room.currentRoomHeading;
    }

    private void OnButtonClick()
    {
        FreeLookCam.Instance.transform.position = roomLocation;
        room.currentRoom = currentRoom;
        headingText.text = "CurrentRoom : " + button.GetComponentInChildren<TextMeshProUGUI>().text;
       
    }
}
