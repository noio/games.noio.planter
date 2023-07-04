using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace games.noio.planter.Editor
{
    [CustomPropertyDrawer(typeof(BranchSocketOption))]
    public class BranchSocketOptionDrawer : PropertyDrawer
    {
        const string BranchOptionVisualTreeAssetGUID = "ab0519dfb0b874d94afabc5a6e16f090";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var visualTreeAssetPath = AssetDatabase.GUIDToAssetPath(BranchOptionVisualTreeAssetGUID);
            var visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(visualTreeAssetPath);
            var tree = visualTreeAsset.CloneTree();
            tree.BindProperty(property);

            // var templateProperty = property.FindPropertyRelative("_template");
            // var selectButton = tree.Q<Button>("select-button");
            // selectButton.clicked += () => SelectTemplate(templateProperty);
            return tree;
        }

        void SelectTemplate(SerializedProperty property)
        {
            var allBranches = AssetDatabase.FindAssets("t:GameObject")
                                           .Select(AssetDatabase.GUIDToAssetPath)
                                           .Select(AssetDatabase.LoadAssetAtPath<BranchTemplate>)
                                           .Where(bt => bt != null)
                                           .OrderBy(bt => bt.name)
                                           .ToList();

            var menu = new GenericMenu();

            foreach (var branch in allBranches)
            {
                menu.AddItem(new GUIContent(branch.name), false, () =>
                {
                    property.objectReferenceValue = branch;
                    property.serializedObject.ApplyModifiedProperties();
                });
            }

            menu.ShowAsContext();
        }
    }
}