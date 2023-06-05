using UnityEditor;
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

        public override VisualElement CreateInspectorGUI()
        {
            var tree = _visualTree.CloneTree();

            var surfaceLayers = tree.Q<LayerMaskField>("surface-layers");
            var surfaceDistance = tree.Q<Slider>("surface-distance");
            surfaceLayers.RegisterValueChangedCallback(evt => surfaceDistance.SetEnabled(evt.newValue != 0));
            var surfaceLayersProp = serializedObject.FindProperty("_surfaceLayers");
            surfaceDistance.SetEnabled(surfaceLayersProp.intValue != 0);

            // var maxPivotAngle = tree.Q<Slider>("max-pivot-angle");
            // var maxPivotAngleDisplay = tree.Q<CircleFillElement>("max-pivot-angle-display");
            // maxPivotAngle.RegisterValueChangedCallback(evt => maxPivotAngleDisplay.Arc = evt.newValue);

            var defaultInspector = tree.Q<Foldout>("default-inspector");
            InspectorElement.FillDefaultInspector(defaultInspector, serializedObject, this);
            return tree;
        }
    }
}