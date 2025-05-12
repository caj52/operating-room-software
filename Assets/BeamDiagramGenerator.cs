using UnityEngine;
using System.IO;

public class BeamDiagramGenerator : MonoBehaviour
{
    public int width = 1024;
    public int height = 256;

    void Start()
    {
        Texture2D tex = new Texture2D(width, height);
        Color bgColor = Color.white;
        Color lineColor = Color.black;

        // Fill background
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                tex.SetPixel(x, y, bgColor);

        int segments = 14;
        float segmentLength = width / (float)segments;
        int centerY = height / 2;

        // Draw beam
        for (int x = 0; x < width; x++)
            tex.SetPixel(x, centerY, lineColor);

        // Draw vertical markers
        for (int i = 0; i <= segments; i++)
        {
            int x = Mathf.RoundToInt(i * segmentLength);
            for (int y = centerY; y > centerY - 10; y--)
                tex.SetPixel(x, y, lineColor);
        }

        // Draw diagonal hatch lines
        int hatchLength = 20;
        for (int i = 0; i < segments; i++)
        {
            float xMid = (i + 0.5f) * segmentLength;
            for (int j = 0; j < hatchLength; j++)
            {
                int x = Mathf.RoundToInt(xMid - j);
                int y = centerY + j;
                if (x >= 0 && x < width && y >= 0 && y < height)
                    tex.SetPixel(x, y, lineColor);
            }
        }

        tex.Apply();
        byte[] bytes = tex.EncodeToPNG();
        File.WriteAllBytes(Application.dataPath + "/BeamDiagram.png", bytes);
        Debug.Log("Diagram saved as PNG");

        // Optionally trigger PDF generation from here
    }
}
