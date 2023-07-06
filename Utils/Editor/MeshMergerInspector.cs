using games.noio.planter.Combiner;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Packages.games.noio.planter.Combiner.Editor
{
    [CustomEditor(typeof(MeshMerger))]
    public class MeshMergerInspector : UnityEditor.Editor
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] VisualTreeAsset _visualTree;

        #endregion

        public override VisualElement CreateInspectorGUI()
        {
            var merger = target as MeshMerger;

            var tree = _visualTree.CloneTree();

            var mergeButton = tree.Q<Button>("merge-button");
            mergeButton.clicked += () => merger.MergeChildren();

            return tree;
        }
    }
}