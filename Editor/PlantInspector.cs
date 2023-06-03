using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace games.noio.planter.Editor
{
    [CustomEditor(typeof(Plant))]
    public class PlantInspector : UnityEditor.Editor
    {
        [SerializeField] VisualTreeAsset _visualTree;

        public override VisualElement CreateInspectorGUI()
        {
            var plant = target as Plant;

            var tree = _visualTree.CloneTree();

            var resetButton = tree.Q<Button>("reset-button");
            resetButton.clicked += () => plant.Reset();

            var growButton = tree.Q<Button>("grow-button");
            growButton.clicked += () => plant.Grow();

            var definitionInspector = tree.Q<VisualElement>("definition-inspector");
            var definition = serializedObject.FindProperty("_definition");
            if (definition != null)
            {
                Debug.Log($"hello: {definition.objectReferenceValue}");
                var definitionSerializedObject = new SerializedObject(definition.objectReferenceValue);
                Debug.Log($"{definitionSerializedObject}");
                var defaultInspector = new VisualElement();
                InspectorElement.FillDefaultInspector(defaultInspector, definitionSerializedObject, this);
                tree.Add(defaultInspector);
            }

            // var defaultInspector = new VisualElement();
            // InspectorElement.FillDefaultInspector(defaultInspector, serializedObject, this);

            return tree;
        }
    }
}