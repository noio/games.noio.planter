using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace games.noio.planter.Editor
{
    [CustomEditor(typeof(Plant))]
    public class PlantInspector : UnityEditor.Editor
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] VisualTreeAsset _visualTree;

        #endregion

        #region MONOBEHAVIOUR METHODS

        void OnEnable()
        {
            EditorApplication.update -= EditorUpdate;
            EditorApplication.update += EditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update += EditorUpdate;
        }

        void EditorUpdate()
        {
        }

        #endregion

        public override VisualElement CreateInspectorGUI()
        {
            var plant = target as Plant;

            var tree = _visualTree.CloneTree();

            var resetButton = tree.Q<Button>("reset-button");
            resetButton.clicked += () => plant.Reset();

            var growButton = tree.Q<Button>("grow-button");
            growButton.clicked += () => plant.Grow();

            var speciesInspector = tree.Q<VisualElement>("species-inspector");
            var speciesProp = serializedObject.FindProperty("_species");
            var species = speciesProp.objectReferenceValue as PlantSpecies;
            if (species != null)
            {
                var speciesEditor = CreateEditor(species);
                var editorRoot = speciesEditor.CreateInspectorGUI();
                speciesInspector.Add(editorRoot);
                editorRoot.Bind(speciesEditor.serializedObject);
            }

            // var definitionSerializedObject = new SerializedObject(plantDefinition, plantDefinition);
            // Debug.Log($"Creating Inspector for: {plantDefinition}");
            // var defaultInspector = new VisualElement();
            // InspectorElement.FillDefaultInspector(defaultInspector, definitionSerializedObject,
            // definitionEditor);
            // tree.Add(defaultInspector);
            // definitionField.style.width = objectField[0].style.width;

            // var defaultInspector = new VisualElement();
            // InspectorElement.FillDefaultInspector(defaultInspector, serializedObject, this);

            return tree;
        }
    }
}