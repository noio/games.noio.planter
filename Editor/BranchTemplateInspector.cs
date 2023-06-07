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
        BranchTemplate _template;
        Button _addSocketButton;

        #endregion

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
            CheckCreateSocketButtonEnabled();
            _addSocketButton.clicked += HandleCreateSocketButtonClicked;
            

            var defaultInspector = tree.Q<Foldout>("default-inspector");
            InspectorElement.FillDefaultInspector(defaultInspector, serializedObject, this);
            return tree;
        }

        void CheckCreateSocketButtonEnabled()
        {
            var currentPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            var isOpenInPrefabmode = (currentPrefabStage != null &&
                                      currentPrefabStage.prefabContentsRoot == _template.gameObject);
                
            
            var canCreate = _template.Sockets.Count < 4;
            
            
            _addSocketButton.SetEnabled(canCreate);
        }

        void HandleCreateSocketButtonClicked()
        {
            _template.CreateSocket();
            _addSocketButton.SetEnabled(_template.Sockets.Count < 4);
        }
    }
}