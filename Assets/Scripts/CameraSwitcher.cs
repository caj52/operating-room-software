using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using TMPro;

public class CameraSwitcher : MonoBehaviour
{
    public List<Camera> cameras = new List<Camera>();
    private int currentCameraIndex = 0;
    public GameObject camFlyGameObject;
    public GameObject objectToDisableWhenCamFly;
    public TextMeshProUGUI buttonText;
    public float ceilingHeight = 2.5f; // Adjustable ceiling height

    void Start()
    {
        buttonText = GetComponentInChildren<TextMeshProUGUI>();
    
        if (cameras.Count == 0)
        {
            Debug.LogError("No cameras found in the scene.");
            return;
        }
        SetActiveCamera(currentCameraIndex);
    }

    public void FindAllCameras()
    {
        cameras.Clear();
        cameras.AddRange(FindObjectsOfType<Camera>());
    }

    public void SwitchCamera()
    {
        if (cameras.Count == 0) return;

        int nextCameraIndex = (currentCameraIndex + 1) % cameras.Count;
        cameras[nextCameraIndex].transform.position = cameras[currentCameraIndex].transform.position;
        cameras[nextCameraIndex].transform.rotation = cameras[currentCameraIndex].transform.rotation;

       

        SetActiveCamera(nextCameraIndex);

    
    }


    private void SetActiveCamera(int index)
    {
        for (int i = 0; i < cameras.Count; i++)
        {
            cameras[i].enabled = (i == index);
        }

        currentCameraIndex = index;

        if (cameras[currentCameraIndex].gameObject == camFlyGameObject)
        {
            if (objectToDisableWhenCamFly != null)
            {
                objectToDisableWhenCamFly.SetActive(false);
                buttonText.text = "CamFly";
            }
        }
        else
        {
            if (objectToDisableWhenCamFly != null)
            {
                objectToDisableWhenCamFly.SetActive(true);
                buttonText.text = "CamWalk";
            }
        }
    }
}
