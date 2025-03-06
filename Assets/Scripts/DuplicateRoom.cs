using HighlightPlus;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class DuplicateRoom : MonoBehaviour
{
    public GameObject currentRoom;
    public GameObject uiRoomNameBtnPrefab;
    public GameObject contentPanel;
    public GameObject roomListPanel;
    public TextMeshProUGUI currentRoomHeading;
    public UnityAction<GameObject> onObjectPlaced;

    private Dictionary<int, RoomData> roomObjectsMapping = new Dictionary<int, RoomData>();
    private int roomCount = 1; // Keeps track of total rooms
    private const int roomOffset = 20; // Fixed offset

    private void Awake()
    {
        roomListPanel = contentPanel.transform.parent.parent.parent.gameObject;

        if (currentRoom != null)
        {
            roomObjectsMapping[roomCount] = new RoomData(currentRoom);
            StartCoroutine(SetUpUI(currentRoom, roomCount));
        }

        onObjectPlaced += OnObjectPlaced;
    }

    private void OnDestroy()
    {
        onObjectPlaced -= OnObjectPlaced;
    }

    private void OnObjectPlaced(GameObject obj)
    {
        Debug.Log("Object Added To The List: " + obj);
        if (roomObjectsMapping.TryGetValue(roomCount, out RoomData room))
        {
            room.ObjectsInRoom.Add(obj);
        }
    }

    public void DuplicateThisRoom()
    {
        UI_DialogPrompt.Open("Are you sure you want to Duplicate this Room?",
            new ButtonAction
            {
                ButtonText = "Duplicate Room",
                Action = () =>
                {
                    // Calculate newRoomIndex based on current keys instead of a counter.
                    int newRoomIndex = roomObjectsMapping.Keys.Any() ? roomObjectsMapping.Keys.Max() + 1 : 1;

                    GameObject newRoom = DuplicateEntireRoom(currentRoom, newRoomIndex);
                    if (newRoom == null) return;

                    roomObjectsMapping[newRoomIndex] = new RoomData(newRoom);

                    if (roomObjectsMapping.TryGetValue(newRoomIndex - 1, out RoomData prevRoom))
                    {
                        Vector3 originalRoomPos = prevRoom.RoomObject.transform.position;
                        Vector3 newRoomPos = newRoom.transform.position;

                        foreach (var obj in prevRoom.ObjectsInRoom)
                        {
                            DuplicateObject(obj, newRoomPos, originalRoomPos, newRoomIndex);
                        }
                    }

                    UI_DialogPrompt.Close();
                    // Update currentRoom to the new room.
                    currentRoom = newRoom;
                },
            },
            new ButtonAction { ButtonText = "Cancel" }
        );
    }


    private GameObject DuplicateEntireRoom(GameObject room, int newRoomIndex)
    {
        if (room == null)
        {
            Debug.LogWarning("DuplicateEntireRoom: The object to duplicate is null.");
            return null;
        }

        // Use consistent room placement based on index
        Vector3 newRoomPos = new Vector3((newRoomIndex - 1) * roomOffset, room.transform.position.y, room.transform.position.z);
        GameObject newRoom = Instantiate(room, newRoomPos, room.transform.rotation);
        newRoom.name = "Room " + newRoomIndex;

        StartCoroutine(ApplyChildScalesDelayed(room.transform, newRoom.transform));
        StartCoroutine(ApplyModifiedMaterials(room.transform,newRoom.transform));
        StartCoroutine(SetUpUI(newRoom, newRoomIndex));

        FreeLookCam.Instance.transform.position = newRoomPos;
        currentRoom = newRoom;
        return newRoom;
    }


    public  List<MaterialPalette> originalMats;
    public List<MaterialPalette> duplicateMats;
    IEnumerator ApplyModifiedMaterials(Transform original, Transform duplicate)
    {
        yield return new WaitForSeconds(0.1f); // Ensure everything is fully initialized

        List<MaterialPalette> originalMats = new List<MaterialPalette>(original.GetComponentsInChildren<MaterialPalette>());
        List<MaterialPalette> duplicateMats = new List<MaterialPalette>(duplicate.GetComponentsInChildren<MaterialPalette>());

        int minCount = Mathf.Min(originalMats.Count, duplicateMats.Count);
        Debug.Log($"Original count: {originalMats.Count}, Duplicate count: {duplicateMats.Count}");

        for (int i = 0; i < minCount; i++)
        {
            if (originalMats[i] == null || duplicateMats[i] == null)
            {
                Debug.LogWarning($"MaterialPalette missing at index {i}");
                continue;
            }

            if (originalMats[i].meshRenderer == null || duplicateMats[i].meshRenderer == null)
            {
                Debug.LogWarning($"MeshRenderer missing on object {originalMats[i].gameObject.name} or {duplicateMats[i].gameObject.name}");
                continue;
            }

            Material[] originalMaterials = originalMats[i].meshRenderer.sharedMaterials;
            Material[] duplicateMaterials = duplicateMats[i].meshRenderer.sharedMaterials;

            Debug.Log($"Checking object {originalMats[i].gameObject.name}: Original materials count = {originalMaterials.Length}, Duplicate materials count = {duplicateMaterials.Length}");

            int matCount = Mathf.Min(originalMaterials.Length, duplicateMaterials.Length);

            for (int j = 0; j < matCount; j++)
            {
                Material originalMat = originalMaterials[j];
                Material duplicateMat = duplicateMaterials[j];

                Debug.Log($"Comparing index {j}: Original = {originalMat?.name}, Duplicate = {duplicateMat?.name}");

                if (originalMat != duplicateMat)
                {
                    Debug.Log($"Assigning {originalMat?.name} to {duplicateMats[i].gameObject.name} at index {j}");

                    // Clone material if CanBeReadByGroup is true, to avoid modifying the original reference
                    Material newMat = (originalMats[i].CanBeReadByGroup) ? Instantiate(originalMat) : originalMat;
                    duplicateMats[i].Assign(newMat, j);
                }
            }
        }
    }

    private void DuplicateObject(GameObject obj, Vector3 newRoomPos, Vector3 originalRoomPos, int newRoomIndex)
    {
        if (obj == null)
        {
            Debug.LogWarning("DuplicateObject: Passed object is null!");
            return;
        }
        // Maintain correct relative position
        Vector3 relativePosition = obj.transform.position - originalRoomPos;
        Vector3 newObjectPos = newRoomPos + relativePosition;
        GameObject newObj = Instantiate(obj, newObjectPos, obj.transform.rotation);
        newObj.transform.localScale = obj.transform.localScale;
        HighlightEffect highlight = newObj.GetComponent<HighlightEffect>();
        if (highlight != null)
        {
            highlight.highlighted = false;
        }
        StartCoroutine(ApplyChildScalesDelayed(obj.transform, newObj.transform));
        if (roomObjectsMapping.TryGetValue(newRoomIndex, out RoomData room))
        {
            room.ObjectsInRoom.Add(newObj);
        }
    }

    private IEnumerator ApplyChildScalesDelayed(Transform original, Transform duplicate)
    {
        yield return new WaitForEndOfFrame();
        CopyChildScales(original, duplicate);
    }

    private void CopyChildScales(Transform original, Transform duplicate)
    {
        Dictionary<string, Transform> duplicateChildren = BuildChildLookup(duplicate);
        ApplyChildScales(original, duplicateChildren);
    }

    private void ApplyChildScales(Transform original, Dictionary<string, Transform> duplicateChildren)
    {
        foreach (Transform originalChild in original)
        {
            if (duplicateChildren.TryGetValue(originalChild.name, out Transform newChild))
            {
                newChild.localScale = originalChild.localScale;
               
                Debug.Log($"Child: {originalChild.name}, Applied Scale: {newChild.localScale}");
                ApplyChildScales(originalChild, BuildChildLookup(newChild));
            }
            else
            {
                Debug.LogWarning($"Missing child in duplicate: {originalChild.name}");
            }
        }
    }

    private Dictionary<string, Transform> BuildChildLookup(Transform parent)
    {
        Dictionary<string, Transform> lookup = new Dictionary<string, Transform>();
        foreach (Transform child in parent)
        {
            lookup[child.name] = child;
        }
        return lookup;
    }

    private IEnumerator SetUpUI(GameObject obj, int roomIndex)
    {
        yield return new WaitForEndOfFrame();
        GameObject positionBtn = Instantiate(uiRoomNameBtnPrefab, contentPanel.transform);
        RoomsPositionBtn roomsPosition = positionBtn.GetComponentInChildren<RoomsPositionBtn>();
        roomsPosition.currentRoom = obj;
        roomsPosition.roomLocation = obj.transform.position;
        positionBtn.GetComponentInChildren<TextMeshProUGUI>().text = "Room " + roomIndex;
        currentRoomHeading.text = "CurrentRoom : Room " + roomIndex;
        roomsPosition.deleteRoombutton.onClick.AddListener(() => DeleteRoom(roomIndex));
        EnableRoomListPanel(roomIndex != 1);
    }

    private void EnableRoomListPanel(bool enable)
    {
        roomListPanel.SetActive(enable);
    }

    public void DeleteRoom(int roomIndex)
    {
        // Check if the room exists
        if (!roomObjectsMapping.ContainsKey(roomIndex))
        {
            Debug.LogWarning($"DeleteRoom: Room {roomIndex} does not exist.");
            return;
        }

        // Check if the user is trying to delete the current room
        if (currentRoom != null && roomObjectsMapping[roomIndex].RoomObject == currentRoom)
        {
            Debug.LogWarning("DeleteRoom: Cannot delete the currently selected room.");
            UI_DialogPrompt.Open("You cannot delete the currently selected room.",
                new ButtonAction { ButtonText = "OK" }
            );
            return;
        }

        // Show confirmation prompt
        UI_DialogPrompt.Open($"Are you sure you want to delete Room {roomIndex}?",
            new ButtonAction
            {
                ButtonText = "Delete Room",
                Action = () =>
                {
                    if (roomObjectsMapping.TryGetValue(roomIndex, out RoomData roomData))
                    {
                        // Destroy all objects in the room
                        foreach (var obj in roomData.ObjectsInRoom)
                        {
                            Destroy(obj);
                        }

                        // Destroy the room itself
                        Destroy(roomData.RoomObject);

                        // Remove from dictionary
                        roomObjectsMapping.Remove(roomIndex);

                        // Update UI
                        RefreshRoomListUI();
                    }

                    UI_DialogPrompt.Close();
                },
            },
            new ButtonAction { ButtonText = "Cancel" }
        );
    }


    private void RefreshRoomListUI()
    {
        foreach (Transform child in contentPanel.transform)
        {
            Destroy(child.gameObject);
        }

        foreach (var entry in roomObjectsMapping)
        {
           
                if (entry.Key > 0) // ✅ Ensures only positive keys are processed
                {
                    StartCoroutine(SetUpUI(entry.Value.RoomObject, entry.Key));
                }
            }

        }
    }



public class RoomData
{
    public GameObject RoomObject { get; private set; }
    public List<GameObject> ObjectsInRoom { get; private set; }

    public RoomData(GameObject room)
    {
        RoomObject = room;
        ObjectsInRoom = new List<GameObject>();
    }
}
