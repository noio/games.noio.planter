using System.Collections.Generic;
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
        [SerializeField] VisualTreeAsset _branchStatusVisualTree;
        SerializedProperty _speciesProp;
        VisualElement _speciesInspector;
        VisualElement _branchStatusParent;
        Button _createSpeciesButton;
        ObjectField _speciesField;
        Dictionary<BranchTemplate, VisualElement> _branchStatusElements;
        Plant _plant;

        #endregion

        #region MONOBEHAVIOUR METHODS

        void OnEnable()
        {
            EditorApplication.update -= EditorUpdate;
            EditorApplication.update += EditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
        }

        void EditorUpdate()
        {
            (target as Plant)?.CheckIfMovedAndRestart();
        }

        public override VisualElement CreateInspectorGUI()
        {
            _plant = target as Plant;
            _plant.BranchAdded += RefreshBranchStatus;

            var tree = _visualTree.CloneTree();

            var restartButton = tree.Q<Button>("restart-button");
            restartButton.clicked += () => _plant.Restart();

            _speciesInspector = tree.Q<VisualElement>("species-inspector");
            _speciesField = tree.Q<ObjectField>("species-field");
            _speciesField.RegisterValueChangedCallback(evt =>
            {
                CheckShowSpeciesInspector(evt.newValue as PlantSpecies);
            });

            _speciesProp = serializedObject.FindProperty("_species");

            _createSpeciesButton = tree.Q<Button>("create-button");
            _createSpeciesButton.clicked += () =>
            {
                var species = PlantSpecies.Create();
                _speciesProp.objectReferenceValue = species;
                serializedObject.ApplyModifiedProperties();
                CheckShowSpeciesInspector(species);

                _plant.Restart();
            };

            var stateProp = serializedObject.FindProperty("_state");
            var doneLabel = tree.Q<Label>("done-label");
            doneLabel.TrackPropertyValue(stateProp,
                prop => { SetVisibleIfEnumEquals(doneLabel, prop, PlantState.Done); });

            var missingDataLabel = tree.Q<Label>("missing-data-label");
            missingDataLabel.TrackPropertyValue(stateProp,
                prop => { SetVisibleIfEnumEquals(missingDataLabel, prop, PlantState.MissingData); });

            var growingLabel = tree.Q<Label>("growing-label");
            growingLabel.TrackPropertyValue(stateProp,
                prop => { SetVisibleIfEnumEquals(growingLabel, prop, PlantState.Growing); });

            _branchStatusElements = new Dictionary<BranchTemplate, VisualElement>();
            _branchStatusParent = tree.Q<VisualElement>("branch-status");
            _branchStatusParent.Clear();
            RefreshBranchStatus();

            return tree;
        }
        
        #endregion

        static void SetVisibleIfEnumEquals(Label doneLabel, SerializedProperty property,
            PlantState                           plantState)
        {
            doneLabel.style.display = property.intValue == (int)plantState
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        void RefreshBranchStatus()
        {
            if (_plant.BranchTypes == null)
            {
                return;
            }

            foreach (var branchType in _plant.BranchTypes)
            {
                var template = branchType.Template;
                if (_branchStatusElements.TryGetValue(template, out var element) == false)
                {
                    element = _branchStatusVisualTree.CloneTree();
                    _branchStatusElements[template] = element;
                    _branchStatusParent.Add(element);
                }

                var nameLabel = element.Q<Label>("name-label");
                nameLabel.text = template.name;

                var countLabel = element.Q<Label>("count-label");
                countLabel.text = $"Count: {branchType.TotalCount} / {template.MaxCount}";
            }
        }

        void CheckShowSpeciesInspector(PlantSpecies newValue)
        {
            _speciesInspector.Clear();
            if (newValue != null)
            {
                var speciesEditor = CreateEditor(newValue);
                var editorRoot = speciesEditor.CreateInspectorGUI();
                _speciesInspector.Add(editorRoot);
                editorRoot.Bind(speciesEditor.serializedObject);
                _createSpeciesButton.style.display = DisplayStyle.None;
            }
            else
            {
                _createSpeciesButton.style.display = DisplayStyle.Flex;
            }
        }
    }
}