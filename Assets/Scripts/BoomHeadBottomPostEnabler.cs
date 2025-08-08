using UnityEngine;

public class BoomHeadBottomPostEnabler : MonoBehaviour
{
    public AttachmentPoint attachmentPoint;
    public Material blackMat;
    public Material whiteMat;
    public MeshRenderer mat;
    private void Start()
    {
        gameObject.SetActive(false);
    }
    public void EnableGameObject(bool enable)
    {

        Selectable[] selectable = attachmentPoint.AttachedSelectable.ToArray();

        if (selectable.Length < 1)
        {
            gameObject.SetActive(false);
           // return;
        }
        Debug.LogError("EnableGameObject");
        bool onlyNitrogen = selectable.Length == 1 && selectable[0].name.Contains("NitrogenRegulator");

        if (onlyNitrogen)
        {
            gameObject.SetActive(false);
            return;
        }

        foreach (var item in selectable)
        {
            if (item.name.Contains("Standard_Rail_v1") || item.name.Contains("Rear_Rail"))
            {
                gameObject.SetActive(true);
                mat.sharedMaterial =  whiteMat;
            }
            else if(item.name.Contains("SHP_Rails"))
            {
                gameObject.SetActive(true);
                mat.sharedMaterial = blackMat;
            }
        }

    }

}
