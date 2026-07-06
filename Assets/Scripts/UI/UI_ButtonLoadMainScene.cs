using UnityEngine;
using UnityEngine.SceneManagement;

public class UI_ButtonLoadMainScene : MonoBehaviour
{
    public void LoadMainScene()
    {
        SceneLoadDiagnostics.MarkTransitionStart("Start→Main");
        SceneManager.LoadScene("Main");
    }
}
