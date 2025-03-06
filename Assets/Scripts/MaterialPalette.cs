using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class MaterialPalette : MonoBehaviour
{
    public static UnityEvent<MaterialUpdateEvent> 
    OnMaterialChanged { get; } = new();

    /// <summary>
    /// Maximum time, in seconds, that the instance will wait 
    /// for a master to retrieve materials before putting a 
    /// warning in console.
    /// </summary>
    private const float _maximumWaitTime = 10;

    private static List<MaterialPalette> _instances = new();

    [field: SerializeField] 
    public MaterialElement[] elements { get; private set; }

    [field: SerializeField] 
    public MeshRenderer meshRenderer { get; private set; }

    [field: SerializeField]
    public Material currentMat { get; private set; }

    [field: SerializeField]
    public MaterialGroup MaterialGroup { get; private set; }

    /// <summary>
    /// Determines if this instance can be read by other 
    /// members of the group on <see cref="Start"/>. 
    /// Read by Additional Walls being added to a scene in 
    /// progress (but should be false for Additional Walls)
    /// </summary>
    [field: SerializeField, 
    Tooltip("Should be false for Additional Wall selectable (and its children) " +
    "and true for permanent scene objects (room walls, room " +
    "baseboards, room wall protectors)")]
    public bool CanBeReadByGroup { get; private set; }

    private Material[] _currentMaterials;

    private void Awake()
    {
        _instances.Add(this);

        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        OnMaterialChanged.AddListener(UpdateMaterials);
    }

    private void UpdateMaterials(MaterialUpdateEvent updateEvent)
    {
        if (updateEvent.Sender == this) 
            return;

        if (updateEvent.Group == MaterialGroup.None) 
            return;

        if (updateEvent.Group != MaterialGroup)
            return;

        Assign(updateEvent.Material, updateEvent.Index, fireEvent: false);
       
    }

    private IEnumerator Start()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            yield break;
        }

        yield return new WaitUntil(() => 
            !ConfigurationManager.IsLoading);

        if (MaterialGroup != MaterialGroup.None && !CanBeReadByGroup)
        {
            float timer = 0;
            int times = 1;
            while (true)
            {
                var master = _instances
                    .FirstOrDefault(x => x.MaterialGroup == MaterialGroup && x.CanBeReadByGroup);

                if (master != default)
                {
                    meshRenderer.sharedMaterials = master._currentMaterials;
                    break;
                }
                
                timer += Time.deltaTime;

                if (timer > _maximumWaitTime)
                {
                    timer -= _maximumWaitTime;
                    Debug.LogWarning($"MaterialPalette on {gameObject.name} has been waiting {_maximumWaitTime * times++} seconds for a group master. It's likely that some scene object (wall, baseboard, wallprotector) is missing its group assignment");
                }

                yield return null;
            }
        }

        if (CanBeReadByGroup || MaterialGroup == MaterialGroup.None)
        {
            for (int i = 0; i < elements.Count(); i++)
            {
                if (elements[i]._zeroStart)
                {
                    Assign(elements[i].materials[0], i);
                }
            }
        }
    }

    private void OnDestroy()
    {
        _instances.Remove(this);
        OnMaterialChanged.RemoveListener(UpdateMaterials);
    }

    public void Assign(Material material, int i, bool fireEvent = true)
    {
        Material[] mats = meshRenderer.sharedMaterials;
        mats[i] = material;
        _currentMaterials = mats;
        currentMat = _currentMaterials[0];
        meshRenderer.sharedMaterials = mats;

        if (fireEvent)
        {
            OnMaterialChanged?.Invoke
                (new MaterialUpdateEvent(material, MaterialGroup, i, this));
           
        }
    }

    public void Assign(string material, int i)
    {
        Material[] mats = meshRenderer.sharedMaterials;

        try
        {
            var mat = elements[i].materials.Single(x => x.name == material);
            mats[i] = mat;
            meshRenderer.sharedMaterials = mats;
            _currentMaterials = mats;

            OnMaterialChanged?.Invoke
                (new MaterialUpdateEvent(mat, MaterialGroup, i, this));
        }
        catch (IndexOutOfRangeException)
        {
            Debug.LogWarning("An IndexOutOfRangeException occurred. Materials configuration or the amount of materials on this object may have changed since this save was created.");
        }
        catch (InvalidOperationException)
        {
            Debug.LogError($"Failed to find material \"{material}\" within element array {i}");
        }

    }
}

[Serializable]
public class MaterialElement
{
    [field: SerializeField] public bool _zeroStart { get; private set; }
    [field: SerializeField] public Material[] materials;
}

public class MaterialUpdateEvent
{
    public MaterialUpdateEvent(
    Material material, MaterialGroup group, 
    int index, MaterialPalette sender)
    {
        Material = material;
        Group = group;
        Index = index;
        Sender = sender;
    }

    public MaterialPalette Sender { get; }
    public Material Material { get; }
    public MaterialGroup Group { get; }
    public int Index { get; }
}
