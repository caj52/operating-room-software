// Deprecated in favor of SetUVToWorld

//using System.Collections;
//using System.Collections.Generic;
//using System.Threading.Tasks;
//using UnityEngine;

//public class ScaleMaterialTextureWithTransform : MonoBehaviour
//{
//    [SerializeField] private MeshRenderer _meshRenderer;
//    [SerializeField] private bool _useX;
//    [SerializeField] private bool _useY;
//    [SerializeField] private bool _useZ;
//    //[SerializeField] private float _multiplier;

//    private void Awake()
//    {
//        if (_meshRenderer == null)
//        {
//            _meshRenderer = GetComponent<MeshRenderer>();
//        }   
        
//        RoomSize.RoomSizeChanged.AddListener(OnRoomSizeChanged);
//    }

//    private void OnDestroy()
//    {
//        RoomSize.RoomSizeChanged.RemoveListener(OnRoomSizeChanged);
//    }

//    private async void OnRoomSizeChanged(RoomDimension rd)
//    {
//        await Task.Yield();
//        Scale();
//    }

//    public void Scale()
//    {
//        bool factor1Set = false;
//        bool factor2Set = false;
//        float factor1 = 1f;
//        float factor2 = 1f;
//        if (_useX)
//        {
//            factor1 = transform.localScale.x;
//            factor1Set = true;
//        }

//        if (_useZ)
//        {
//            if (!factor1Set)
//            {
//                factor1 = transform.localScale.z;
//                factor1Set = true;
//            }
//            else
//            {
//                factor2 = transform.localScale.z;
//                factor2Set = true;
//            }
//        }

//        if (_useY)
//        {
//            if (!factor2Set)
//            {
//                factor2 = transform.localScale.y;
//            }
//        }

//        foreach (Material m in _meshRenderer.materials)
//        {
//            m.SetTextureScale("_BaseMap", new Vector2(factor1, factor2));
//        }
//    }
//}