using UnityEditor;
using UnityEditor.UIElements;
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
            return tree;
        }
    }
}