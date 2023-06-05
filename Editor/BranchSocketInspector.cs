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

        #endregion


        public override VisualElement CreateInspectorGUI()
        {

            var tree = _visualTree.CloneTree();
            var branchList = tree.Q<ListView>("branch-list");
            branchList.makeItem = _branchOptionElement.CloneTree;
            branchList.reorderable = false;
            
            var addButton = tree.Q<Button>("add-button");
            addButton.clicked += AddBranchOption;

            return tree;
        }
        
        void AddBranchOption()
        {
            // var namePrefix = ParentTemplate != null ? ParentTemplate.name.Split(' ', '-', '_')[0] : "NONE";

            var allBranches = AssetDatabase.FindAssets("t:GameObject")
                                           .Select(AssetDatabase.GUIDToAssetPath)
                                           .Select(AssetDatabase.LoadAssetAtPath<BranchTemplate>)
                                           .Where(bt => bt != null)
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
                menu.AddItem(new GUIContent("Other/" + branch.name), false, () =>
                {
                    Undo.RecordObject(this, "Add Branch Option");
                    // _branchOptions.Insert(0, branch);
                });
            }

            menu.ShowAsContext();
        }
    }
}