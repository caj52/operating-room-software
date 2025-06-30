#pragma warning disable 649
using TriLibCore.General;
using UnityEngine;
using TriLibCore.Extensions;
using UnityEngine.UI;
using Unity.VisualScripting;
using RTG;
using static RTG.Object2ObjectSnap;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace TriLibCore.Samples
{
    /// <summary>
    /// Represents a sample that loads a Model from a file-picker.
    /// </summary>
    public class LoadModelFromFilePickerSample : MonoBehaviour
    {
        private GameObject _loadedGameObject;

        public GameObject preparedObject;
        public GameObject AttachPoint;

        [SerializeField]
        private Button _loadModelButton;

        public void LoadModel()
        {
            UI_GeneralLoadingScreen.instance.ShowLoadingScreen();

            var assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions();
            var assetLoaderFilePicker = AssetLoaderFilePicker.Create();
            assetLoaderFilePicker.LoadModelFromFilePickerAsync(
                "Select a Model file",
                OnLoad,
                OnMaterialsLoad,
                OnProgress,
                OnBeginLoad,
                OnError,
                null,
                assetLoaderOptions);
        }

        private void OnBeginLoad(bool filesSelected)
        {
            if (!filesSelected)
            {
                UI_DialogPrompt.Open("No Model has been Selected to Import", new ButtonAction
                {
                    ButtonText = "OK",
                });
                Debug.Log("User canceled the model selection.");
                UI_GeneralLoadingScreen.instance.HideLoadingScreen();
                return;
            }

            _loadModelButton.interactable = false;
        }


        private void OnError(IContextualizedError error)
        {
            Debug.LogError($"An error occurred while loading your Model: {error.GetInnerException()}");
            UI_GeneralLoadingScreen.instance.HideLoadingScreen();
        }

        private void OnProgress(AssetLoaderContext context, float progress)
        {
            UI_GeneralLoadingScreen.instance.SetProgress(progress);
        }

        private void OnMaterialsLoad(AssetLoaderContext context)
        {
            if (context.RootGameObject != null)
            {
                Debug.Log("Model fully loaded.");
            }
            else
            {
                Debug.LogWarning("Model could not be loaded.");
            }

            _loadModelButton.interactable = true;
            UI_GeneralLoadingScreen.instance.HideLoadingScreen();
        }

        private void OnLoad(AssetLoaderContext context)
        {
            if (_loadedGameObject != null)
            {
                Destroy(_loadedGameObject);
            }

            GameObject newObject = Instantiate(preparedObject);
            SetupSelectableObject(newObject);
            _loadedGameObject = context.RootGameObject;
            if (_loadedGameObject == null)
            {
                Debug.LogWarning("No root GameObject found in loaded model.");
                return;
            }
            _loadedGameObject.transform.position = Vector3.one;
            _loadedGameObject.transform.SetParent(newObject.transform, false);
            SetupModelChildren(_loadedGameObject,newObject);
        }

        private void SetupSelectableObject(GameObject obj)
        {
            var selectable = obj.GetComponent<Selectable>();
            if (selectable != null)
            {
                selectable.StartRaycastPlacementMode();
                var recordHierarchy = obj.AddComponent<RecordHirarcheySelectables>();
                recordHierarchy.AddAttachedSelectables(selectable, preparedObject.name);
            }
            else
            {
                Debug.LogWarning("Selectable component is missing on the prepared object.");
            }
        }

        private void SetupModelChildren(GameObject modelRoot,GameObject newObject)
        {
            int targetLayer = LayerMask.NameToLayer("Selectable");

            foreach (var child in modelRoot.GetAllChildren())
            {
                child.gameObject.SetLayerRecursively(targetLayer);

                if (child.GetMesh() != null)
                {
                    child.AddComponent<MeshCollider>();
                    var eventSender = child.AddComponent<UnityEventSender>();
                    eventSender.Target = newObject;
                }

                if (child.name.StartsWith("AttachPoint"))
                {
                    child.transform.localScale = Vector3.one;
                    GameObject attachPointInstance = Instantiate(AttachPoint);
                    attachPointInstance.transform.SetParent(child.transform, false);
                }
            }
        }
    }
}
