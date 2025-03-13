using UnityEngine;
using System.IO;
using System.Collections;
// URP-specific imports
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Linq;
using System.Reflection;

public class ScreenshotCapture : MonoBehaviour
{
    public int resolutionMultiplier = 2;
    public string screenshotFileName = "Screenshot";
    public Camera captureCamera; // Assign your Camera
    public Transform[] cameraPositions; // Assign multiple Transform positions in Inspector
    public Canvas uiCanvas; // Assign UI Canvas if you want to hide it
    public AudioSource shutterSound; // Optional: Assign an AudioSource to play a sound on capture
    private MethodInfo setActiveMethod;
    public string folderPath;
    // URP volume reference
    public Volume postProcessingVolume;

    // Capture options
    [Tooltip("Method to capture screenshots")]
    public CaptureMethod captureMethod = CaptureMethod.CameraRender;
    public enum CaptureMethod
    {
        CameraRender,
        ScreenCapture,
        CameraStacking
    }

    private CanvasGroup canvasGroup;
    private UniversalAdditionalCameraData urpCameraData;

    // SSAO component reference


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
        captureCamera.enabled = false;
        // Try to get SSAO component
      
    }


    public void TakeScreenshot()
    {
        captureCamera.enabled = true;
        StartCoroutine(CaptureMultipleScreenshots());
    }

    IEnumerator CaptureMultipleScreenshots()
    {
        UI_GeneralLoadingScreen.instance.ShowLoadingScreen();
        // Hide UI before capturing
        if (uiCanvas != null)
            ToggleUI(false);

        // Store original camera settings
        CameraCaptureState originalState = new CameraCaptureState(captureCamera, urpCameraData);
   
        for (int i = 0; i < cameraPositions.Length; i++)
        {
            // Move Camera to New Position
            captureCamera.transform.position = cameraPositions[i].position;
            captureCamera.transform.rotation = cameraPositions[i].rotation;

            // Wait for the end of frame to ensure all rendering is complete
            yield return new WaitForEndOfFrame();

            // Capture screenshot
            TakeScreenshot(i + 1);

            yield return new WaitForSeconds(0.5f); // Short delay for smooth capturing
        }

        // Restore original camera settings
        originalState.Restore(captureCamera, urpCameraData);

        // Restore SSAO state

        UI_GeneralLoadingScreen.instance.HideLoadingScreen();
        // Re-enable UI after all screenshots are taken
        if (uiCanvas != null)
            ToggleUI(true);
        Debug.Log("Capturing Screenshot...");
        UI_DialogPrompt.Open(
                  $"Success! PDF saved to {folderPath}",
             new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = folderPath),
            new ButtonAction("Done"));
        
    }

    void TakeScreenshot(int index)
    {
         folderPath = Path.Combine(Application.persistentDataPath, "Screenshots");
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        string fileName = $"{screenshotFileName}_{index}_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        string filePath = Path.Combine(folderPath, fileName);

        switch (captureMethod)
        {
            case CaptureMethod.CameraRender:
                CaptureUsingCameraRender(filePath);
                break;

            case CaptureMethod.ScreenCapture:
                // Force immediate render to ensure all post-processing is visible
                captureCamera.Render();
                // Use Unity's built-in screenshot function
                ScreenCapture.CaptureScreenshot(filePath, resolutionMultiplier);
                break;

            case CaptureMethod.CameraStacking:
                CaptureUsingCameraStack(filePath);
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

    void CaptureUsingCameraStack(string filePath)
    {
        // This method ensures all cameras in the stack are rendered
        if (urpCameraData == null)
        {
            Debug.LogError("Cannot use camera stacking method without URP camera data");
            return;
        }

        // Create render texture
        RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(
            Screen.width * resolutionMultiplier,
            Screen.height * resolutionMultiplier,
            RenderTextureFormat.DefaultHDR,
            24);
        rtDesc.msaaSamples = QualitySettings.antiAliasing > 0 ? QualitySettings.antiAliasing : 1;

        RenderTexture rt = new RenderTexture(rtDesc);

        // Original values
        RenderTexture originalTarget = captureCamera.targetTexture;
        bool wasUsingStack = urpCameraData.renderPostProcessing;

        try
        {
            // Force render to texture with post-processing
            urpCameraData.renderPostProcessing = true;
            captureCamera.targetTexture = rt;

            // Setup and use custom rendering
            ScriptableRenderContext context = new ScriptableRenderContext();

            // URP-specific: Request to render the camera (and its stack if applicable)
            UniversalRenderPipeline.RenderSingleCamera(context, captureCamera);

            // Read the result
            RenderTexture.active = rt;
            Texture2D screenShot = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            screenShot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            screenShot.Apply(false);

            // Save to file
            byte[] bytes = screenShot.EncodeToPNG();
            File.WriteAllBytes(filePath, bytes);
            Destroy(screenShot);
        }
        finally
        {
            // Restore original settings
            captureCamera.targetTexture = originalTarget;
            urpCameraData.renderPostProcessing = wasUsingStack;
            RenderTexture.active = null;
            rt.Release();
            Destroy(rt);
        }

        Debug.Log("URP Camera Stack Screenshot saved: " + filePath);
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