using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BoomHeadBottomPostEnabler : MonoBehaviour
{
    public AttachmentPoint attachmentPoint;
    public void EnableGameObject(bool enable)
    {
        Selectable selectable = attachmentPoint.AttachedSelectable.FirstOrDefault();
        if (selectable == null)
        {
            gameObject.SetActive(enable);
            return;
        }

        if (selectable.name.Contains("NitrogenRegulator"))
        {
            gameObject.SetActive(false);
        }
        else
        {
            gameObject.SetActive(true);
        }
    }

}
