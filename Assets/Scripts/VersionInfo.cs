using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VersionInfo : MonoBehaviour
{
    private void Start()
    {
        string versionText = "OR Software V " + Application.version;

        if (TryGetComponent(out Text text))
        {
            text.text = versionText;
        }
        else if (TryGetComponent(out TextMeshProUGUI textMesh)) 
        { 
            textMesh.text = versionText;
        }
    }
}
