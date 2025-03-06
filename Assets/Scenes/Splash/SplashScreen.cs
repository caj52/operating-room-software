using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SplashScreen : MonoBehaviour
{
    public int guiDepth = 0;
    public string levelToLoad = "";
    public Texture2D splashLogo;
    public float fadeSpeed = 0.3f;
    public float waitTime = 0.5f;
    public bool waitForInput = false;
    public bool startAutomatically = true;
    public float timeFadingInFinished = 0.0f;
    private float alpha = 0.0f;
    private FadeStatus status = FadeStatus.FadeIn;
    private Camera oldCam;
    private GameObject oldCamGO;
    private Rect splashLogoPos;
    private bool loadingNextLevel = false;
    private enum FadeStatus { Paused, FadeIn, FadeWaiting, FadeOut }
    private Texture2D whiteFill;

    private int halfScreenHeight;
    private int halfTexHeight;
    private int halfScreenWidth;
    private int halfTexWidth;
    private Rect whiteFillRect1;
    private Rect whiteFillRect2;
    private Rect whiteFillRect3;
    private Rect whiteFillRect4;

    private void Awake()
    {
        if (startAutomatically)
        {
            status = FadeStatus.FadeIn;
        }
        else
        {
            status = FadeStatus.Paused;
        }

        //whiteFill = new Texture2D(1, 1);
      //  whiteFill.SetPixel(0, 0, Color.white);
       // whiteFill.Apply();

        GameObject storageGB = new GameObject("Flash");
        storageGB.transform.localScale = new Vector3(0, 0, 1);

        oldCam = Camera.main;
        oldCamGO = Camera.main.gameObject;

        splashLogoPos.x = (Screen.width * 0.5f) - (splashLogo.width * 0.5f);
        splashLogoPos.y = (Screen.height * 0.5f) - (splashLogo.height * 0.5f);

        splashLogoPos.width = splashLogo.width;
        splashLogoPos.height = splashLogo.height;

        halfScreenHeight = Screen.height / 2;
        halfTexHeight = splashLogo.height / 2;
        halfScreenWidth = Screen.width / 2;
        halfTexWidth = splashLogo.width / 2;

        whiteFillRect1 = new Rect(0, 0, Screen.width, halfScreenHeight - halfTexHeight);
        whiteFillRect2 = new Rect(0, halfScreenHeight + halfTexHeight, Screen.width, Screen.height);
        whiteFillRect3 = new Rect(0, halfScreenHeight - halfTexHeight, halfScreenWidth - halfTexWidth, splashLogo.height);
        whiteFillRect4 = new Rect(halfScreenWidth + halfTexWidth, halfScreenHeight - halfTexHeight, halfScreenWidth - halfTexWidth, splashLogo.height);

        DontDestroyOnLoad(this);
        DontDestroyOnLoad(Camera.main);

        if (Application.levelCount <= 1)
        {
            Debug.LogWarning("Invalid levelToLoad value.");
        }
    }

    private void Update()
    {
        switch (status)
        {
            case FadeStatus.FadeIn:
                alpha += fadeSpeed * Time.deltaTime;
                break;

            case FadeStatus.FadeWaiting:
                if ((!waitForInput && Time.time >= timeFadingInFinished + waitTime) || (waitForInput && Input.anyKey))
                {
                    status = FadeStatus.FadeOut;
                }
                break;

            case FadeStatus.FadeOut:
                alpha += -fadeSpeed * Time.deltaTime;
                break;
        }
    }

    private void OnGUI()
    {
     
        GUI.depth = guiDepth;

        if (splashLogo != null)
        {
            GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, Mathf.Clamp01(alpha));
            GUI.DrawTexture(whiteFillRect1, whiteFill);
            GUI.DrawTexture(whiteFillRect2, whiteFill);
            GUI.DrawTexture(whiteFillRect3, whiteFill);
            GUI.DrawTexture(whiteFillRect4, whiteFill);
            GUI.DrawTexture(splashLogoPos, splashLogo);

            if (alpha > 1.0)
            {
                status = FadeStatus.FadeWaiting;
                timeFadingInFinished = Time.time;
                alpha = 1.0f;

                oldCam.depth = -1000;
                loadingNextLevel = true;

                if (Application.levelCount >= 1)
                {
                    if (levelToLoad != "")
                    {
                        Application.LoadLevel(levelToLoad);
                    }
                    else
                    {
                        Application.LoadLevel(1);
                    }
                }
            }

            if (alpha < 0.0)
            {
                // oldCamGO.SetActive(false);
                Destroy(oldCamGO);
            }
        }
    }

    void OnLevelWasLoaded(int lvlIdx)
    {
        if (loadingNextLevel)
        {
            Destroy(oldCam);
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1, 0, 0, 0.5f);
        Gizmos.DrawCube(transform.position, new Vector3(1, 1, 1));
    }

    void StartSplash()
    {
        status = FadeStatus.FadeIn;
    }
}
