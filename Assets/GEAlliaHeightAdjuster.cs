using UnityEngine;

public class GEAlliaHeightAdjuster : MonoBehaviour
{
/*    [Header("Auto-set from RoomBoundary")]
    public float currentRoomHeightM = 3.05f; // Default to 10 ft

    [Header("Scene References")]
    public Transform geAlliaAnchor;       // Parent: will move up/down
    public Transform geAlliaModelRoot;    // Child: will be scaled uniformly

    [Header("Calibration")]
    public float mountOffsetFromCeiling = 0.175f;  // Distance below ceiling to place the mount
    public float defaultDesignHeightM = 3.05f;     // Your model was designed for 10ft room

    private void Start()
    {
        RoomSize.RoomSizeChanged.AddListener(dim =>
        {
           AdjustToRoomHeight(dim.Height.ToMeters());
        });
    }



    public void AdjustToRoomHeight(float roomHeightM)
    {
        currentRoomHeightM = roomHeightM;


        // 2. Scale model proportionally to room height
        float scaleFactor = currentRoomHeightM / defaultDesignHeightM;
        geAlliaModelRoot.localScale = new Vector3(1, 1 * scaleFactor,1);
    }*/
}
