using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Add to a <see cref="Button"/> component to easily allow 
/// it to open a URL
/// </summary>
[RequireComponent(typeof(Button))]
public class UI_ButtonOpenURL : MonoBehaviour
{
    [field: SerializeField] public string Url { get; set; }

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener
            (() => Application.OpenURL(Url));
    }
}