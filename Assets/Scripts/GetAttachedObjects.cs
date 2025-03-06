using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class GetAttachedObjects : MonoBehaviour
{
    [field: SerializeField] public string Id { get; private set; }
    public List<Selectable> selectables;
    private Selectable _selectable;
    public EnforceZScale zScale;
    public EnforceZScale[] zScales;
    public GameObject parent;
    public  int lightCount = 0;
    public  int flatPanel = 0;
    public bool isLightTop;
    public bool isPanelTop;
    private IEnumerator Start()
    {
        yield return new WaitUntil(() => !ConfigurationManager.IsLoading);

        selectables = GetComponentsInParent<Selectable>().ToList();
        zScale = GetComponentInParent<EnforceZScale>();

        foreach (var item in selectables)
        {
            if (item.name.Contains("ArmSegment_1"))
            {
                parent = item.gameObject;
            }
        }

        lightCount = parent.GetComponentsInChildren<Selectable>().Where(x => x.gameObject.name.Contains("Simeon_LightHead")).ToList().Count;
        flatPanel = parent.GetComponentsInChildren<Selectable>().Where(x => x.gameObject.name.Contains("ArmMonitorMount")).ToList().Count;
        _selectable = zScale.GetComponent<Selectable>();
 
        if (lightCount==1)
        {
            ApplyScaleFilter(new List<float> {0.8f,0.925f,1.04f,1.3f},_selectable);
            isLightTop = true;
        }
        else if (lightCount==2)
        {
            ApplyScaleFilter(new List<float> { 0.8f, 0.925f, 1.04f, 1.15f}, _selectable);
        }
        else if (flatPanel==1)
        {
            ApplyScaleFilter(new List<float> { 0.8f, 1.062f}, _selectable);
            isPanelTop = true;
        }
        else if (flatPanel == 2)
        {
            ApplyScaleFilter(new List<float> {0.8f, 0.822f,0.925f, 1.062f }, _selectable);
        }

        if (lightCount == 1 && flatPanel == 1) // UONE Flat Panel Configs
        {

            zScales = parent.GetComponentsInChildren<EnforceZScale>();
            if (isLightTop)//Light is on Top and Panel is on Bottom
            {
                ApplyScaleFilter(new List<float> { 0.8f, 0.925f }, zScales[0].GetComponent<Selectable>());
                ApplyScaleFilter(new List<float> { 0.8f, 0.925f }, zScales[1].GetComponent<Selectable>());
            }
            else if (isPanelTop)
            {
                ApplyScaleFilter(new List<float> { 0.8f, 0.925f, 1.062f }, zScales[0].GetComponent<Selectable>());
                ApplyScaleFilter(new List<float> { 0.8f, 0.925f, 1.062f }, zScales[1].GetComponent<Selectable>());
            }
        }
    }

    private void ApplyScaleFilter(List<float> allowedScales,Selectable selectable)
    {
        _selectable = selectable;
        if (_selectable.ScaleLevels == null || _selectable.ScaleLevels.Count == 0)
        {
            Debug.LogWarning($"No ScaleLevels found on {_selectable.name}", this);
            return;
        }

        // Keep only the specified scales
        _selectable.ScaleLevels = _selectable.ScaleLevels
            .Where(level => allowedScales.Contains(level.Size))
            .ToList();

        Debug.Log($"ApplyScaleFilter applied on {gameObject.name}", this);
    }
}
