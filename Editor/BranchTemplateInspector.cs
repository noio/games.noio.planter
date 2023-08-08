using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace games.noio.planter.Editor
{
    [CustomEditor(typeof(BranchTemplate))]    
    [CanEditMultipleObjects]
    public class BranchTemplateInspector : UnityEditor.Editor
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] VisualTreeAsset _visualTree;

        #endregion

        BranchTemplate _template;
        Button _createSocketButton;
        VisualElement _prefabStageWarning;

        public override VisualElement CreateInspectorGUI()
        {
            _template = target as BranchTemplate;

            var tree = _visualTree.CloneTree();

            var surfaceLayers = tree.Q<LayerMaskField>("surface-layers");
            var surfaceDistance = tree.Q<Slider>("surface-distance");
            surfaceLayers.RegisterValueChangedCallback(evt => surfaceDistance.SetEnabled(evt.newValue != 0));
            var surfaceLayersProp = serializedObject.FindProperty("_surfaceLayers");
            surfaceDistance.SetEnabled(surfaceLayersProp.intValue != 0);

            _template.FindSockets();
            _createSocketButton = tree.Q<Button>("create-socket-button");
            _createSocketButton.clicked += HandleCreateSocketButtonClicked;

            _prefabStageWarning = tree.Q<VisualElement>("prefab-stage-warning");

            var meshVariantList = tree.Q<ListView>("mesh-variants-list");
            meshVariantList.itemsAdded += HandleVariantsChanged;
            meshVariantList.itemsRemoved += HandleVariantsChanged;

            CheckCreateSocketButtonEnabled();

            // var defaultInspector = tree.Q<Foldout>("default-inspector");
            // InspectorElement.FillDefaultInspector(defaultInspector, serializedObject, this);
            return tree;
        }

        void HandleVariantsChanged(IEnumerable<int> obj)
        {
            _template.NormalizeMeshVariantProbabilities();
        }

        void CheckCreateSocketButtonEnabled()
        {
            var currentPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            var isOpenInPrefabmode = currentPrefabStage != null &&
                                     currentPrefabStage.prefabContentsRoot == _template.gameObject;

            _prefabStageWarning.style.display =
                new StyleEnum<DisplayStyle>(isOpenInPrefabmode ? DisplayStyle.None : DisplayStyle.Flex);

            var canCreate = _template.Sockets.Count < BranchTemplate.MaxSockets;

            _createSocketButton.SetEnabled(canCreate && isOpenInPrefabmode);
            _createSocketButton.text = $"Create Socket ({_template.Sockets.Count}/{BranchTemplate.MaxSockets})";
        }

        void HandleCreateSocketButtonClicked()
        {
            var createdSocket = _template.CreateSocket();
            Selection.activeGameObject = createdSocket.gameObject;
            CheckCreateSocketButtonEnabled();
        }
    }
}