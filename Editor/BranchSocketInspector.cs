using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
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
        VisualElement _branchOptions;
        BranchSocket _branchSocket;

        #endregion

        public override VisualElement CreateInspectorGUI()
        {
            _branchSocket = target as BranchSocket;
            var tree = _visualTree.CloneTree();

            var addButton = tree.Q<Button>("add-button");
            addButton.clicked += AddBranchOption;

            _branchOptions = tree.Q<VisualElement>("branch-options");
            RefreshBranchOptions();

            return tree;
        }

        void RefreshBranchOptions()
        {
            var branchOptionsProp = serializedObject.FindProperty("_branchOptions2");
            _branchOptions.Clear();
            for (int i = 0; i < branchOptionsProp.arraySize; i++)
            {
                var optionProp = branchOptionsProp.GetArrayElementAtIndex(i);
                var element = _branchOptionElement.CloneTree();
                element.BindProperty(optionProp);
                _branchOptions.Add(element);
            }
        }

        void AddBranchOption()
        {
            // var namePrefix = ParentTemplate != null ? ParentTemplate.name.Split(' ', '-', '_')[0] : "NONE";

            var allBranches = AssetDatabase.FindAssets("t:GameObject")
                                           .Select(AssetDatabase.GUIDToAssetPath)
                                           .Select(AssetDatabase.LoadAssetAtPath<BranchTemplate>)
                                           .Where(bt => bt != null)
                                           .Where(bt => _branchSocket.IsBranchOption(bt) == false)
                                           .OrderBy(bt => bt.name)
                                           .ToList();

            // var branchesOfSamePlant = allBranches
            // .Where(d => d.name.StartsWith(namePrefix))
            // .Where(d => BranchOptions.Contains(d) == false)
            // .ToList();

            var menu = new GenericMenu();

            // foreach (var branch in branchesOfSamePlant)
            // {
            // menu.AddItem(new GUIContent(branch.name), false, () =>
            // {
            // Undo.RecordObject(this, "Add Branch Option");
            // _branchOptions.Insert(0, branch);
            // });
            // }

            foreach (var branch in allBranches)
            {
                menu.AddItem(new GUIContent(branch.name), false, () =>
                {
                    Undo.RecordObject(this, "Add Branch Option");
                    _branchSocket.AddBranchOption(branch, 100f / (_branchSocket.BranchOptions2.Count + 1));
                    RefreshBranchOptions();
                    EditorUtility.SetDirty(_branchSocket);
                    serializedObject.Update();

                    // _branchOptions.Insert(0, branch);
                });
            }

            menu.ShowAsContext();
        }
    }
}