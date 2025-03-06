//
// SplashScreen Script
//
// This is the same script as above (only a few redundant things were removed), just converted to UnityScript
// to help out those sticking to unity script, and to help those just starting out with it.
// Quantum Fusion Studios
 
var guiDepth : int = 0;
var levelToLoad : String = "";
var splashLogo : Texture2D;
var fadeSpeed : float = 0.3;
var waitTime : float = 0.5;
var waitForInput : boolean = false;
var startAutomatically : boolean = true;
var timeFadingInFinished : float = 0.0;
private var alpha : float = 0.0;
private var status : FadeStatus = FadeStatus.FadeIn;
private var oldCam : Camera;
private var oldCamGO : GameObject;
private var splashLogoPos : Rect;
private var loadingNextLevel : boolean = false;
private enum FadeStatus {Paused, FadeIn, FadeWaiting, FadeOut}
private var whiteFill : Texture2D;

private var halfScreenHeight : int;
private var halfTexHeight : int;
private var halfScreenWidth : int;
private var halfTexWidth : int;
private var whiteFillRect1 : Rect;
private var whiteFillRect2 : Rect;
private var whiteFillRect3 : Rect;
private var whiteFillRect4 : Rect;


function Start()
{
	if (startAutomatically)
	{
		status = FadeStatus.FadeIn;
	}
 
	else
	{
		status = FadeStatus.Paused;
	}
 	
    whiteFill = new Texture2D ( 1 , 1 );
    whiteFill.SetPixel( 0 , 0 , Color.white );
    whiteFill.Apply();

    var storageGB = new GameObject("Flash");
    storageGB.transform.localScale = new Vector3(0 , 0 , 1);

    // flash = storageGB.AddComponent(GUITexture);
    // flash.pixelInset = new Rect(0 , 0 , Screen.width , Screen.height );
    // // flash.color = flashColor;
    // flash.texture = whiteFill;
    // flash.enabled = true;
 	
	oldCam = Camera.main;
	oldCamGO = Camera.main.gameObject;
 
	splashLogoPos.x = (Screen.width * 0.5) - (splashLogo.width * 0.5);
	splashLogoPos.y = (Screen.height * 0.5) - (splashLogo.height * 0.5);

	splashLogoPos.width = splashLogo.width;
	splashLogoPos.height = splashLogo.height;
	
	halfScreenHeight = Screen.height / 2;
	halfTexHeight = splashLogo.height / 2;
	halfScreenWidth = Screen.width / 2;
	halfTexWidth = splashLogo.width / 2;
	whiteFillRect1 = Rect(0, 0, Screen.width, halfScreenHeight - halfTexHeight);
	whiteFillRect2 = Rect(0, halfScreenHeight + halfTexHeight, Screen.width, Screen.height);
	whiteFillRect3 = Rect(0, halfScreenHeight - halfTexHeight, halfScreenWidth - halfTexWidth, splashLogo.height);
	whiteFillRect4 = Rect(halfScreenWidth + halfTexWidth, halfScreenHeight - halfTexHeight, halfScreenWidth - halfTexWidth, splashLogo.height);	

	DontDestroyOnLoad(this);
	DontDestroyOnLoad(Camera.main);
	// DontDestroyOnLoad(flash);
 	
	if (Application.levelCount <= 1)
	{
		Debug.LogWarning("Invalid levelToLoad value.");
	}
}
 
 
 
function Update()
{
	switch(status)
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
 
 
 
function OnGUI()
// function OnPostRender()
{
	GUI.depth = guiDepth;
 
	if (splashLogo != null)
	{
		GUI.color = Color(GUI.color.r, GUI.color.g, GUI.color.b, Mathf.Clamp01(alpha));
	 	GUI.DrawTexture(whiteFillRect1, whiteFill);
	 	GUI.DrawTexture(whiteFillRect2, whiteFill);
	 	GUI.DrawTexture(whiteFillRect3, whiteFill);
	 	GUI.DrawTexture(whiteFillRect4, whiteFill);
		GUI.DrawTexture(splashLogoPos, splashLogo);
 
			if (alpha > 1.0)
			{
				status = FadeStatus.FadeWaiting;
				timeFadingInFinished = Time.time;
				alpha = 1.0;
 
				oldCam.depth = -1000;
				loadingNextLevel = true;

				if (Application.levelCount >= 1)
				{
					if (levelToLoad != "") {
						Application.LoadLevel(levelToLoad);
					} else {
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



 
 
function OnLevelWasLoaded(lvlIdx : int)
{
	if (loadingNextLevel)
	{
		Destroy(oldCam);
	}
}
 
 
function OnDrawGizmos()
{
	Gizmos.color = Color(1, 0, 0, 0.5);
	Gizmos.DrawCube(transform.position, Vector3(1, 1, 1));
}
 
 
function StartSplash()
{
	status = FadeStatus.FadeIn;
}