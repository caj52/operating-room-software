using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class UIFader : MonoBehaviour
{
    public Image fadeImage;

    public IEnumerator FadeToBlack(float duration)
    {
        float time = 0f;
        Color startColor = fadeImage.color;
        Color endColor = new Color(startColor.r, startColor.g, startColor.b, 1f);

        while (time < duration)
        {
            fadeImage.color = Color.Lerp(startColor, endColor, time / duration);
            time += Time.deltaTime;
            yield return null;
        }

        fadeImage.color = endColor;
    }
}
