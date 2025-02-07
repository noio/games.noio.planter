// (C)2025 @noio_games
// Thomas van den Berg

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
    #region SERIALIZED FIELDS

    [SerializeField] VisualTreeAsset _visualTree;
    [SerializeField] VisualTreeAsset _branchStatusVisualTree;

    #endregion

    SerializedProperty _speciesProp;
    SerializedProperty _seedProp;
    VisualElement _speciesInspector;
    VisualElement _branchStatusParent;
    Button _createSpeciesButton;
    ObjectField _speciesField;
    Dictionary<BranchTemplate, VisualElement> _branchStatusElements;
    Plant _plant;
    double _plantRegrowTime;
    Vector3 _lastPosition;
    Quaternion _lastRotation;
    bool _locked;
    Button _lockButton;

    #region MONOBEHAVIOUR METHODS

    void OnEnable()
    {
        _plant = target as Plant;

        _locked = true;

        _seedProp = serializedObject.FindProperty("_seed");
        _speciesProp = serializedObject.FindProperty("_species");

        _lastPosition = _plant.GrownAtPosition;
        _lastRotation = _plant.GrownAtRotation;

        _plantRegrowTime = double.PositiveInfinity;

        EditorApplication.update -= EditorUpdate;
        EditorApplication.update += EditorUpdate;
    }

    void OnDisable()
    {
        EditorApplication.update -= EditorUpdate;
    }

    #endregion

    public override VisualElement CreateInspectorGUI()
    {
        _plant.BranchAdded += RefreshBranchStatus;

        var tree = _visualTree.CloneTree();

        var incrementSeedButton = tree.Q<Button>("increment-seed-button");
        incrementSeedButton.clicked += HandleIncrementSeedButtonClicked;

        var restartButton = tree.Q<Button>("restart-button");
        restartButton.clicked += () => _plant.Regrow();

        _lockButton = tree.Q<Button>("regrow-lock");
        _lockButton.clicked += () => ToggleLock();

        _speciesInspector = tree.Q<VisualElement>("species-inspector");
        _speciesField = tree.Q<ObjectField>("species-field");
        _speciesField.RegisterValueChangedCallback(evt =>
        {
            CheckShowSpeciesInspector(evt.newValue as PlantSpecies);
        });

        _createSpeciesButton = tree.Q<Button>("create-button");
        _createSpeciesButton.clicked += () =>
        {
            var species = PlantSpecies.Create();
            _speciesProp.objectReferenceValue = species;
            serializedObject.ApplyModifiedProperties();
            CheckShowSpeciesInspector(species);

            _plant.Regrow();
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

    void ToggleLock()
    {
        _locked = !_locked;
        if (_locked)
        {
            _lockButton.RemoveFromClassList("regrow-unlocked");
        }
        else
        {
            _lockButton.AddToClassList("regrow-unlocked");
        }
    }

    void HandleIncrementSeedButtonClicked()
    {
        _seedProp.intValue++;
        serializedObject.ApplyModifiedProperties();
        _plant?.Regrow();
    }

    void EditorUpdate()
    {
        if (_plant == null)
        {
            return;
        }

        if (_locked)
        {
            return;
        }

        /*
         * Regrown on move, but debounced.
         *
         * - get plant last grow position/rotation on init
         * - everytime plant is moved (here), set a delay
         * - keep setting delay as long as plant is moving
         * - at the end of delay, reset plant + remove delay
         */

        var pos = _plant.transform.localPosition;
        var rot = _plant.transform.localRotation;

        var didMove = Vector3.Distance(pos, _lastPosition) > .01f ||
                      Quaternion.Angle(rot, _lastRotation) > 1;

        var time = EditorApplication.timeSinceStartup;
        if (didMove)
        {
            _lastPosition = pos;
            _lastRotation = rot;

            _plantRegrowTime = time + .15f;
        }

        /*
         * Automatic reset when moving object.
         * It's debounced with a small delay to prevent the
         * editor becoming less responsive.
         */

        if (time > _plantRegrowTime)
        {
            Undo.RecordObject(this, $"Regrow {_plant.name}");
            _plant.Regrow();
            _plantRegrowTime = double.PositiveInfinity; // removes delay
        }
    }

    static void SetVisibleIfEnumEquals(
        Label doneLabel,
        SerializedProperty property,
        PlantState plantState
    )
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