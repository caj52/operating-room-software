using UnityEngine;

public class Simplebillboard : MonoBehaviour
{
    [Tooltip("Reference to a specific camera. If null, Camera.main will be used.")]
    public Camera targetCamera;

    [Tooltip("Whether to use world up vector or a custom up direction")]
    public bool useWorldUp = true;

    [Tooltip("Custom up direction when useWorldUp is false")]
    public Vector3 customUpDirection = Vector3.up;

    [Tooltip("Adds a small rotation offset to prevent flipping when looking from above/below")]
    public float stabilityAngleOffset = 5f;

    [Tooltip("Whether this is a text object that needs to maintain correct orientation")]
    public bool isText = true;

    [Tooltip("Lock rotation around the X axis")]
    public bool lockX = false;

    [Tooltip("Lock rotation around the Y axis")]
    public bool lockY = false;

    [Tooltip("Lock rotation around the Z axis")]
    public bool lockZ = false;

    private Vector3 originalRotation;

    private void Start()
    {
        // Store the original rotation in case we need it for locked axes
        originalRotation = transform.eulerAngles;

        // Ensure we have a camera reference
        if (targetCamera == null)
        {
            targetCamera = Camera.main;

            // Log warning if we still don't have a camera
            if (targetCamera == null)
                Debug.LogWarning("TextBillboard on " + gameObject.name + " couldn't find a camera. Please assign one manually.");
        }
    }

    private void LateUpdate()
    {
        if (targetCamera == null)
        {
            // Try to get the main camera again in case it was initialized after Start
            targetCamera = Camera.main;
            if (targetCamera == null)
                return;
        }

        if (isText)
        {
            // For text, we want to directly match the camera's rotation
            // but keep the text upright
            Quaternion rotation = targetCamera.transform.rotation;

            // Remove any roll (rotation around the camera's forward axis)
            // to keep text upright
            Vector3 eulerAngles = rotation.eulerAngles;
            eulerAngles.z = 0;

            // Apply the rotation, considering locked axes
            if (lockX) eulerAngles.x = originalRotation.x;
            if (lockY) eulerAngles.y = originalRotation.y;
            if (lockZ) eulerAngles.z = originalRotation.z;

            transform.rotation = Quaternion.Euler(eulerAngles);
        }
        else
        {
            // Standard billboard behavior for non-text objects
            Vector3 directionToCamera = targetCamera.transform.position - transform.position;

            // Apply small offset to avoid gimbal lock when looking straight up/down
            if (Vector3.Angle(directionToCamera, Vector3.up) < stabilityAngleOffset ||
                Vector3.Angle(directionToCamera, Vector3.down) < stabilityAngleOffset)
            {
                directionToCamera += targetCamera.transform.right * 0.01f;
            }

            // Create the rotation to face the camera
            Quaternion lookRotation;
            if (useWorldUp)
            {
                lookRotation = Quaternion.LookRotation(directionToCamera, Vector3.up);
            }
            else
            {
                lookRotation = Quaternion.LookRotation(directionToCamera, customUpDirection);
            }

            // Apply the rotation, considering locked axes
            Vector3 newRotation = lookRotation.eulerAngles;

            if (lockX) newRotation.x = originalRotation.x;
            if (lockY) newRotation.y = originalRotation.y;
            if (lockZ) newRotation.z = originalRotation.z;

            transform.rotation = Quaternion.Euler(newRotation);
        }
    }
}