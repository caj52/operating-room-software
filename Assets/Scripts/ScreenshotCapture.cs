using UnityEngine;
using System.IO;
using System.Collections;
// URP-specific imports
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

public class ScreenshotCapture : MonoBehaviour
{
    public int resolutionMultiplier = 2;
    public string screenshotFileName = "Screenshot";
    public Camera captureCamera; // Assign your Camera
    public Transform[] cameraPositions; // Assign multiple Transform positions in Inspector
    public List<Vector3> cameraPostionsList;
    public Canvas uiCanvas; // Assign UI Canvas if you want to hide it
    public AudioSource shutterSound; // Optional: Assign an AudioSource to play a sound on capture
    public string folderPath;
    public float buffer;
    // URP volume reference
    public Volume postProcessingVolume;
    public DuplicateRoom rooms;
    // Ceiling position reference
    public Transform ceilingPosition; // Assign the ceiling transform in Inspector

    // Capture options
    [Tooltip("Method to capture screenshots")]
    public CaptureMethod captureMethod = CaptureMethod.CameraRender;
    public enum CaptureMethod
    {
        CameraRender,
    }

    private CanvasGroup canvasGroup;
    private UniversalAdditionalCameraData urpCameraData;

    void Start()
    {
        if (uiCanvas != null)
            canvasGroup = uiCanvas.GetComponent<CanvasGroup>();

        if (captureCamera == null)
            captureCamera = GetComponent<Camera>();

        // Get URP camera data component
        urpCameraData = captureCamera.GetComponent<UniversalAdditionalCameraData>();
        if (urpCameraData == null)
        {
            Debug.LogWarning("This camera doesn't have URP components. Make sure you're using URP.");
        }

        // Find post-processing volume
        if (postProcessingVolume == null)
        {
            // Try to find a volume in the scene
            postProcessingVolume = FindObjectOfType<Volume>();
            if (postProcessingVolume != null)
                Debug.Log("Found post-processing volume: " + postProcessingVolume.name);
        }
        rooms = FindObjectOfType<DuplicateRoom>();
        captureCamera.enabled = false;
    }

    Vector3[] roomCorners;
    Vector3 ceilingPoint;

    public void TakeScreenshot()
    {
        // Make sure cameraPositions array is large enough for all positions (4 walls + ceiling)
        if (cameraPositions.Length < 5)
        {
            Debug.LogError("Camera positions array needs to be at least size 5 (4 walls + ceiling)");
            return;
        }
        RoomBoundary[] roomBoundaries = rooms.currentRoom.GetComponentsInChildren<RoomBoundary>();
        cameraPositions[0] = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.WallSouth).transform;
        cameraPositions[1] = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.WallNorth).transform;
        cameraPositions[2] = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.WallEast).transform;
        cameraPositions[3] = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.WallWest).transform;

        // Assign ceiling position if available or calculate it
        if (ceilingPosition != null)
        {
            cameraPositions[4] = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.Ceiling).transform;
        }
        else
        {
            // If no ceiling transform is provided, we'll calculate a ceiling point later
        }

        GetRoomCorners(cameraPositions, out roomCorners, buffer, rooms.currentRoom.transform);

        // Calculate ceiling point at the center of the room but elevated
        Vector3 roomCenter = GetRoomCenter();
        float ceilingHeight = RoomSize.Instance.CurrentDimensions.Height.ToMeters()+9;
        ceilingPoint = new Vector3(roomCenter.x, ceilingHeight, roomCenter.z);

        // Add all positions to the list
        cameraPostionsList = roomCorners.ToList();
        cameraPostionsList.Add(ceilingPoint);

        captureCamera.enabled = true;
        StartCoroutine(CaptureMultipleScreenshots());
      
    }


    Vector3 GetCornerPosition(Transform wallA, Transform wallB, float buffer, Vector3 roomCenter)
    {
        // Calculate direction from room center to walls
        Vector3 directionA = (wallA.position - roomCenter).normalized;
        Vector3 directionB = (wallB.position - roomCenter).normalized;

        // Apply buffer in those directions
        return new Vector3(
            wallA.position.x + directionA.x * buffer,
            2, // Fixed height
            wallB.position.z + directionB.z * buffer
        );
    }
    Vector3 GetCornerPosition(Transform wallA, Transform wallB, float buffer)
    {
        // Use the actual wall positions to determine corner coordinates
        // This uses the x-coordinate from wallA and z-coordinate from wallB
        float cornerX = wallA.position.x;
        float cornerZ = wallB.position.z;

        // Determine whether to add or subtract the buffer based on wall orientation
        // For example, for the west wall, add buffer to X position
        // For the east wall, subtract buffer from X position
        if (wallA.name.Contains("Wall_W"))
            cornerX -= buffer;
        else if (wallA.name.Contains("Wall_E"))
            cornerX += buffer;

        if (wallB.name.Contains("Wall_S"))
            cornerZ -= buffer;
        else if (wallB.name.Contains("Wall_N"))
            cornerZ += buffer;

        return new Vector3(cornerX, 2, cornerZ);
    }

    void GetRoomCorners(Transform[] cameraPositions, out Vector3[] corners, float buffer, Transform room)
    {
        corners = new Vector3[4]; // 4 Corners: SW, SE, NW, NE

        // Calculate room center
        Vector3 roomCenter = room.position;

        // Assuming cameraPositions are assigned correctly:
        Transform southWall = cameraPositions[0];
        Transform northWall = cameraPositions[1];
        Transform eastWall = cameraPositions[2];
        Transform westWall = cameraPositions[3];

        // Compute corners with buffer
        corners[0] = GetCornerPosition(westWall, southWall, buffer); // Southwest corner
        corners[1] = GetCornerPosition(eastWall, southWall, buffer); // Southeast corner
        corners[2] = GetCornerPosition(westWall, northWall, buffer); // Northwest corner
        corners[3] = GetCornerPosition(eastWall, northWall, buffer); // Northeast corner
    }

    IEnumerator CaptureMultipleScreenshots()
    {
      
        UI_GeneralLoadingScreen.instance.ShowLoadingScreen();
        // Hide UI before capturing
        if (uiCanvas != null)
            ToggleUI(false);

        // Store original camera settings
        CameraCaptureState originalState = new CameraCaptureState(captureCamera, urpCameraData);

        // First capture the 4 corner positions
        for (int i = 0; i < roomCorners.Length; i++)
        {
            // Move Camera to New Position
            captureCamera.transform.position = roomCorners[i];
            Vector3 lookDirection = GetRoomCenter() - captureCamera.transform.position;
            lookDirection.y = 0; // Remove any vertical component to keep camera level
            captureCamera.transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);

            // Wait for the end of frame to ensure all rendering is complete
            yield return new WaitForEndOfFrame();

            // Capture screenshot
            TakeScreenshot(i + 1);

            yield return new WaitForSeconds(0.5f); // Short delay for smooth capturing
        }

        RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).MeshRenderer.enabled = false;
        // Now capture the ceiling position
        captureCamera.transform.position = ceilingPoint;
        // Look down from ceiling
        captureCamera.transform.rotation = Quaternion.Euler(90, 0, 0);

        yield return new WaitForEndOfFrame();

        // Capture ceiling screenshot
        TakeScreenshot(roomCorners.Length + 1);

        yield return new WaitForSeconds(0.5f);

        // Restore original camera settings
        originalState.Restore(captureCamera, urpCameraData);

        UI_GeneralLoadingScreen.instance.HideLoadingScreen();
        // Re-enable UI after all screenshots are taken
        if (uiCanvas != null)
            ToggleUI(true);
        Debug.Log("Capturing Screenshot...");
        UI_DialogPrompt.Open(
                  $"Success! PDF saved to {folderPath}",
             new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = folderPath),
            new ButtonAction("Done"));
        RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).MeshRenderer.enabled = true;
    }

    public Vector3 GetRoomCenter()
    {
        if (roomCorners == null || roomCorners.Length == 0)
        {
            Debug.LogWarning("Room corners are not assigned.");
            return Vector3.zero;
        }

        Bounds bounds = new Bounds(roomCorners[0], Vector3.zero);

        // Expand bounds to include all corners
        for (int i = 1; i < roomCorners.Length; i++)
        {
            bounds.Encapsulate(roomCorners[i]);
        }

        return bounds.center;
    }

    void TakeScreenshot(int index)
    {
        if (string.IsNullOrEmpty(FullRoomSave.GetRoomPath()))
        {
            folderPath = Path.Combine(Application.persistentDataPath, "Renders");
        }
        else
        {
            folderPath = Path.Combine(FullRoomSave.GetRoomPath(), "Renders");
        }
     
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        string fileName = $"{screenshotFileName}_{index}_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        string filePath = Path.Combine(folderPath, fileName);

        switch (captureMethod)
        {
            case CaptureMethod.CameraRender:
                CaptureUsingCameraRender(filePath);
                break;
        }

        // Play Shutter Sound (if assigned)
        if (shutterSound != null)
            shutterSound.Play();

        captureCamera.enabled = false;
    }

    void CaptureUsingCameraRender(string filePath)
    {
        // Key for URP: Set proper render texture format
        RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(
            Screen.width * resolutionMultiplier,
            Screen.height * resolutionMultiplier,
            RenderTextureFormat.DefaultHDR,
            24);
        rtDesc.sRGB = true;
        rtDesc.msaaSamples = 8; // Higher AA for quality
        rtDesc.enableRandomWrite = false;

        RenderTexture rt = new RenderTexture(rtDesc);

        // Save original camera settings
        RenderTexture originalTarget = captureCamera.targetTexture;
        bool originalAllowMSAA = QualitySettings.antiAliasing > 0;

        // For URP, specifically set rendering features
        if (urpCameraData != null)
        {
            // Ensure post-processing is enabled for this camera
            urpCameraData.renderPostProcessing = true;

            // Make sure to preserve framebuffer alpha
            urpCameraData.requiresColorOption = CameraOverrideOption.On;
            urpCameraData.requiresDepthOption = CameraOverrideOption.On;
        }

        // Set camera to render to RT
        captureCamera.targetTexture = rt;

        // Force a render that includes URP post-processing (including SSAO)
        captureCamera.Render();

        // Read pixels into texture
        RenderTexture.active = rt;
        Texture2D screenShot = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        screenShot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        screenShot.Apply(false);

        // Restore original camera settings
        captureCamera.targetTexture = originalTarget;
        RenderTexture.active = null;
        rt.Release();
        Destroy(rt);

        // Save to file
        byte[] bytes = screenShot.EncodeToPNG();
        File.WriteAllBytes(filePath, bytes);
        Destroy(screenShot);

        Debug.Log("URP Screenshot saved: " + filePath);
    }

    void ToggleUI(bool isVisible)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = isVisible ? 1 : 0;
            canvasGroup.interactable = isVisible;
            canvasGroup.blocksRaycasts = isVisible;
        }
        else if (uiCanvas != null)
        {
            uiCanvas.enabled = isVisible;
        }
    }

    // Helper class to store and restore camera state
    private class CameraCaptureState
    {
        public RenderTexture targetTexture;
        public bool postProcessingEnabled;
        public CameraOverrideOption requiresColorOption;
        public CameraOverrideOption requiresDepthOption;

        public CameraCaptureState(Camera camera, UniversalAdditionalCameraData urpData)
        {
            targetTexture = camera.targetTexture;

            if (urpData != null)
            {
                postProcessingEnabled = urpData.renderPostProcessing;
                requiresColorOption = urpData.requiresColorOption;
                requiresDepthOption = urpData.requiresDepthOption;
            }
        }

        public void Restore(Camera camera, UniversalAdditionalCameraData urpData)
        {
            camera.targetTexture = targetTexture;

            if (urpData != null)
            {
                urpData.renderPostProcessing = postProcessingEnabled;
                urpData.requiresColorOption = requiresColorOption;
                urpData.requiresDepthOption = requiresDepthOption;
            }
        }
    }
}