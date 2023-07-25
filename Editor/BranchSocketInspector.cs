using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace games.noio.planter.Editor
{
    [CustomEditor(typeof(BranchSocket))]
    public class BranchSocketInspector : UnityEditor.Editor
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] VisualTreeAsset _visualTree;
        [SerializeField] VisualTreeAsset _branchOptionElement;

        #endregion

        VisualElement _branchOptions;
        BranchSocket _branchSocket;
        ListView _optionList;

        public override VisualElement CreateInspectorGUI()
        {
            _branchSocket = target as BranchSocket;
            var tree = _visualTree.CloneTree();

            _optionList = tree.Q<ListView>("options-list");
            _optionList.itemsAdded += HandleOptionsChanged;
            _optionList.itemsRemoved += HandleOptionsChanged;

            var refreshButton = tree.Q<Button>("refresh-preview-button");
            refreshButton.clicked += _branchSocket.AddOrUpdatePreviewMesh;

            // EditorApplication.delayCall += BindOptionProbabilityListeners;

            return tree;
        }

        // void BindOptionProbabilityListeners()
        // {
        //     var optionListViewport = _optionList.Q<VisualElement>("unity-content-container");
        //     for (var i = 0; i < optionListViewport.childCount; i++)
        //     {
        //         var child = optionListViewport[i];
        //         var floatField = child.Q<FloatField>();
        //
        //         var fixedOptionIndex = i;
        //         floatField.RegisterCallback<BlurEvent>(evt =>
        //         {
        //             _branchSocket.NormalizeProbabilities(fixedOptionIndex);
        //         });
        //
        //         // floatField.RegisterValueChangedCallback(
        //
        //         Debug.Log($"FloatField:{floatField}");
        //     }
        // }


        void HandleOptionsChanged(IEnumerable<int> i)
        {
            EditorApplication.delayCall += () =>
            {
                _branchSocket.OnBranchOptionChanged();
                serializedObject.Update();
            };
        }
    }
}