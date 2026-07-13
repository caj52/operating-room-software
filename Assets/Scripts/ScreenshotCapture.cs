using UnityEngine;
using System.IO;
using System.Collections;
// URP-specific imports
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System;

public class ScreenshotCapture : MonoBehaviour
{
    [Range(1, 8)]
    public int resolutionMultiplier = 4; // Increased from 2 to 4 for higher resolution
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

    // Enhanced quality settings
    [Range(0.5f, 3.0f)]
    public float exposureAdjustment = 1.2f; // Brightness adjustment (>1 = brighter)
    [Range(1, 16)]
    public int antiAliasingLevel = 8; // MSAA level
    [Range(0.0f, 1.0f)]
    public float jpegQuality = 1.0f; // Image quality (1.0 = highest)
    public bool useHDR = true;
    public bool linearColorSpace = true;

    // Capture options
    [Tooltip("Method to capture screenshots")]
    public CaptureMethod captureMethod = CaptureMethod.CameraRender;
    public enum CaptureMethod
    {
        CameraRender,
    }

    private CanvasGroup canvasGroup;
    private UniversalAdditionalCameraData urpCameraData;
    private Tonemapping tonemapping;
    private ColorAdjustments colorAdjustments;
    private float originalExposure = 0f;

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

        // Try to get Tonemapping and Color Adjustment settings
        if (postProcessingVolume != null)
        {
            postProcessingVolume.profile.TryGet(out tonemapping);
            postProcessingVolume.profile.TryGet(out colorAdjustments);
            if (colorAdjustments != null)
                originalExposure = colorAdjustments.postExposure.value;
        }

        rooms = FindObjectOfType<DuplicateRoom>();
    }

    Vector3[] roomCorners;
    Vector3 ceilingPoint;

    public void TakeScreenshot()
    {
        LastPresentationBatchOk = false;

        // Make sure cameraPositions array is large enough for all positions (4 walls + ceiling)
        if (cameraPositions == null || cameraPositions.Length < 5)
        {
            Debug.LogError("Camera positions array needs to be at least size 5 (4 walls + ceiling)");
            screenshotBatchCompleted = true;
            return;
        }

        if (rooms == null || rooms.currentRoom == null)
        {
            Debug.LogError("ScreenshotCapture: no current room — cannot capture presentation snapshots.");
            screenshotBatchCompleted = true;
            return;
        }

        RoomBoundary[] roomBoundaries = rooms.currentRoom.GetComponentsInChildren<RoomBoundary>();
        var south = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.WallSouth);
        var north = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.WallNorth);
        var east = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.WallEast);
        var west = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.WallWest);

        if (south == null || north == null || east == null || west == null)
        {
            Debug.LogError("ScreenshotCapture: room is missing one or more walls — cannot capture presentation snapshots.");
            screenshotBatchCompleted = true;
            return;
        }

        cameraPositions[0] = south.transform;
        cameraPositions[1] = north.transform;
        cameraPositions[2] = east.transform;
        cameraPositions[3] = west.transform;

        // Assign ceiling position if available or calculate it
        if (ceilingPosition != null)
        {
            cameraPositions[4] = ceilingPosition;
        }
        else
        {
            var ceilingRB = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.Ceiling);
            if (ceilingRB != null) cameraPositions[4] = ceilingRB.transform;
        }

        GetRoomCorners(cameraPositions, out roomCorners, buffer, rooms.currentRoom.transform);

        // Calculate ceiling point at the center of the room but elevated
        Vector3 roomCenter = GetRoomCenter();
        float roomHeight = RoomSize.Instance != null
            ? RoomSize.Instance.CurrentDimensions.Height.ToMeters()
            : 3f;
        float ceilingHeight = roomHeight + 9;
        ceilingPoint = new Vector3(roomCenter.x, ceilingHeight, roomCenter.z);

        // Add all positions to the list
        cameraPostionsList = roomCorners.ToList();
        cameraPostionsList.Add(ceilingPoint);

        captureCamera.enabled = true;
        StartCoroutine(CaptureMultipleScreenshots());
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
        screenshotBatchCompleted = false;
        bool ownLoadingScreen = !ExportOrchestrator.SuppressIndividualDialogs;
        if (ownLoadingScreen && UI_GeneralLoadingScreen.instance != null)
            UI_GeneralLoadingScreen.instance.ShowLoadingScreen();
        // Hide UI before capturing
        if (uiCanvas != null)
            ToggleUI(false);

        // Store original camera settings
        CameraCaptureState originalState = new CameraCaptureState(captureCamera, urpCameraData);

        // Apply enhanced brightness settings
        AdjustExposureForScreenshot(true);

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
            yield return new WaitUntil(() => screenshotCompleted); // Short delay for smooth capturing
        }

        var ceilingBoundary = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling);
        if (ceilingBoundary != null && ceilingBoundary.MeshRenderer != null)
            ceilingBoundary.MeshRenderer.enabled = false;
        // Now capture the ceiling position
        captureCamera.transform.position = ceilingPoint;
        // Look down from ceiling
        captureCamera.transform.rotation = Quaternion.Euler(90, 0, 0);

        yield return new WaitForEndOfFrame();

        // Capture ceiling screenshot
        TakeScreenshot(roomCorners.Length + 1);
        yield return new WaitUntil(() => screenshotCompleted);

        // Restore original camera settings and exposure settings
        originalState.Restore(captureCamera, urpCameraData);
        AdjustExposureForScreenshot(false);

        if (ownLoadingScreen && UI_GeneralLoadingScreen.instance != null)
            UI_GeneralLoadingScreen.instance.HideLoadingScreen();
        // Re-enable UI after all screenshots are taken
        if (uiCanvas != null)
            ToggleUI(true);
        Debug.Log("Capturing Screenshot...");
        if (!ExportOrchestrator.SuppressIndividualDialogs)
        {
            UI_DialogPrompt.Open(
                  "Enhanced screenshots saved.",
            new ButtonAction("Done"));
            ExportFolderUtility.RevealInFileManager(folderPath);
        }
        if (OperatingRoomCamera.LiveCamera != null
            && OperatingRoomCamera.LiveCamera.CameraType == OperatingRoomCameraType.FreeLook
            && ceilingBoundary != null
            && ceilingBoundary.MeshRenderer != null)
        {
            ceilingBoundary.MeshRenderer.enabled = true;
        }
        LastPresentationBatchOk = true;
        screenshotBatchCompleted = true;
    }

    /// <summary>Result of the most recent presentation-snapshot batch.</summary>
    public bool LastPresentationBatchOk { get; private set; }

    public IEnumerator ExportPresentationSnapshots(string outputDirectory)
    {
        folderPath = outputDirectory;
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        screenshotBatchCompleted = false;
        TakeScreenshot();
        yield return new WaitUntil(() => screenshotBatchCompleted);
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

    // Add this flag as a class member variable
    private bool screenshotCompleted = false;
    private bool screenshotBatchCompleted = false;

    // Modified TakeScreenshot method
    string TakeScreenshot(int index)
    {
        if (string.IsNullOrEmpty(folderPath))
            folderPath = ExportPaths.RendersDir;
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);
        string fileName = $"{screenshotFileName}_HD_{index}_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        string filePath = Path.Combine(folderPath, fileName);

        // Reset completion flag before starting capture
        screenshotCompleted = false;

        switch (captureMethod)
        {
            case CaptureMethod.CameraRender:
                StartCoroutine(CaptureAfterFrame(filePath));
                break;
        }
        // Play Shutter Sound (if assigned)
        if (shutterSound != null)
            shutterSound.Play();
        return filePath;
    }

    // Modified CaptureAfterFrame method with completion flag
    private IEnumerator CaptureAfterFrame(string filePath)
    {
        // Wait for full frame render to avoid lighting/post issues
        yield return new WaitForEndOfFrame();
        // Optionally wait another frame for heavier setups
        // yield return null;
        // Set up enhanced render texture format
        RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(
            Screen.width * resolutionMultiplier,
            Screen.height * resolutionMultiplier,
            useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.ARGB32,
            24)
        {
            sRGB = !linearColorSpace,
            msaaSamples = antiAliasingLevel,
            enableRandomWrite = false,
            depthBufferBits = 24
        };
        RenderTexture rt = new RenderTexture(rtDesc);
        rt.Create();
        // Save original camera state
        RenderTexture originalTarget = captureCamera.targetTexture;
        if (urpCameraData != null)
        {
            urpCameraData.renderPostProcessing = true;
            urpCameraData.requiresColorOption = CameraOverrideOption.On;
            urpCameraData.requiresDepthOption = CameraOverrideOption.On;
        }
        captureCamera.targetTexture = rt;
        // Force render (after pipeline is fully ready)
        captureCamera.Render();
        // Read pixels
        RenderTexture.active = rt;
        Texture2D screenShot = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, linearColorSpace);
        screenShot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        screenShot.Apply(false);
        // Cleanup
        captureCamera.targetTexture = originalTarget;
        RenderTexture.active = null;
        rt.Release();
        Destroy(rt);
        byte[] bytes = filePath.ToLower().EndsWith(".jpg") || filePath.ToLower().EndsWith(".jpeg")
            ? screenShot.EncodeToJPG(Mathf.RoundToInt(jpegQuality * 100))
            : screenShot.EncodeToPNG();
        File.WriteAllBytes(filePath, bytes);
        Destroy(screenShot);
        Debug.Log("Screenshot saved: " + filePath);

        // Set completion flag when done
        screenshotCompleted = true;
    }
    string filePath = "";
    // Modified CaptureCeilingOnly method
    public IEnumerator CaptureCeilingOnly(Vector3 position, Quaternion? rotation, Action<string> onComplete)
    {

        if (uiCanvas != null)
            ToggleUI(false);
        captureCamera.enabled = true;
        CameraCaptureState originalState = new CameraCaptureState(captureCamera, urpCameraData);
        AdjustExposureForScreenshot(true);
        captureCamera.transform.position = position;
        captureCamera.transform.rotation = rotation ?? Quaternion.Euler(90, 0, 0);
        captureCamera.orthographic = true;
        RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).MeshRenderer.enabled = false;
        yield return new WaitForEndOfFrame();

        filePath = TakeScreenshot(999);

        // Wait until the screenshot is ACTUALLY completed
        yield return new WaitUntil(() => screenshotCompleted);

        // Now restore ceiling visibility after screenshot is truly saved
        if (OperatingRoomCamera.LiveCamera.CameraType == OperatingRoomCameraType.FreeLook)
        {
            RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).MeshRenderer.enabled = true;
        }

        AdjustExposureForScreenshot(false);
        originalState.Restore(captureCamera, urpCameraData);
        if (uiCanvas != null)
            ToggleUI(true);

        Debug.Log("Custom ceiling shot captured.");
        onComplete?.Invoke(filePath);
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

    // Method to adjust exposure for brighter screenshots
    void AdjustExposureForScreenshot(bool forScreenshot)
    {
        if (colorAdjustments != null)
        {
            if (forScreenshot)
            {
                // Save original exposure and set brightness higher for screenshot
                colorAdjustments.postExposure.Override(originalExposure + Mathf.Log(exposureAdjustment, 2f));
            }
            else
            {
                // Restore original exposure
                colorAdjustments.postExposure.Override(originalExposure);
            }
        }

        // Also adjust tonemapping if available
        if (tonemapping != null && forScreenshot)
        {
            // Store original settings but adjust for screenshots
            // Could add more sophisticated tonemapping adjustments here
        }
    }

    // Helper class to store and restore camera state
    private class CameraCaptureState
    {// Added stored camera projection/transform
        public bool orthographic;
        public float fieldOfView;
        public float orthographicSize;
        public Vector3 position;
        public Quaternion rotation;
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
            }// Store camera projection/transform
            orthographic = camera.orthographic;
            fieldOfView = camera.fieldOfView;
            orthographicSize = camera.orthographicSize;
            position = camera.transform.position;
            rotation = camera.transform.rotation;

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

            // Restore camera projection/transform
            camera.orthographic = orthographic;
            camera.fieldOfView = fieldOfView;
            camera.orthographicSize = orthographicSize;
            camera.transform.SetPositionAndRotation(position, rotation);
        }
    }
}