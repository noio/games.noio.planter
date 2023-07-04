using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace games.noio.planter.Editor
{
    [CustomEditor(typeof(BranchTemplate))]
    public class BranchTemplateInspector : UnityEditor.Editor
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] VisualTreeAsset _visualTree;

        #endregion

        BranchTemplate _template;
        Button _addSocketButton;
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

            // var maxPivotAngle = tree.Q<Slider>("max-pivot-angle");
            // var maxPivotAngleDisplay = tree.Q<CircleFillElement>("max-pivot-angle-display");
            // maxPivotAngle.RegisterValueChangedCallback(evt => maxPivotAngleDisplay.Arc = evt.newValue);

            _template.FindSockets();
            _addSocketButton = tree.Q<Button>("create-socket-button");
            _addSocketButton.clicked += HandleCreateSocketButtonClicked;

            _prefabStageWarning = tree.Q<VisualElement>("prefab-stage-warning");

            CheckCreateSocketButtonEnabled();

            var defaultInspector = tree.Q<Foldout>("default-inspector");
            InspectorElement.FillDefaultInspector(defaultInspector, serializedObject, this);
            return tree;
        }

        void CheckCreateSocketButtonEnabled()
        {
            var currentPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            var isOpenInPrefabmode = currentPrefabStage != null &&
                                     currentPrefabStage.prefabContentsRoot == _template.gameObject;

            _prefabStageWarning.style.display =
                new StyleEnum<DisplayStyle>(isOpenInPrefabmode ? DisplayStyle.None : DisplayStyle.Flex);

            var canCreate = _template.Sockets.Count < BranchTemplate.MaxSockets;

            _addSocketButton.SetEnabled(canCreate && isOpenInPrefabmode);
            _addSocketButton.text = $"Create Socket ({_template.Sockets.Count}/{BranchTemplate.MaxSockets})";
        }

        void HandleCreateSocketButtonClicked()
        {
            var createdSocket = _template.CreateSocket();
            Selection.activeGameObject = createdSocket.gameObject;
            CheckCreateSocketButtonEnabled();
        }
    }
}