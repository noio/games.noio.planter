using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
#endif

namespace games.noio.planter
{
    public class AssetPickerAttribute : PropertyAttribute
    {
    }

#if UNITY_EDITOR

    [CustomPropertyDrawer(typeof(AssetPickerAttribute))]
    public class AssetPickerDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row
                }
            };

            var button = new Button
            {
                style =
                {
                    flexDirection = FlexDirection.Row
                }
            };
            button.clicked += () => { ShowAssetPickerDropdown(property); };
            
            var label = new Label("▼")
            {
                style =
                {
                    fontSize = 8,
                    unityTextAlign = TextAnchor.MiddleCenter
                }
            };

            var propertyField = new PropertyField(property)
            {
                style =
                {
                    flexGrow = 1
                }
            };
            
            container.Add(propertyField);
            button.Add(label);
            container.Add(button);
            
            return container;
        }

        void ShowAssetPickerDropdown(SerializedProperty targetProperty)
        {
            var type = fieldInfo.FieldType;
            var assets = AssetDatabase.FindAssets("t:GameObject")
                                      .Select(AssetDatabase.GUIDToAssetPath)
                                      .Select(path => AssetDatabase.LoadAssetAtPath(path, type))
                                      .Where(asset => asset != null)
                                      .ToList();

            var menu = new GenericMenu();
            foreach (var asset in assets)
            {
                var assetPath = AssetDatabase.GetAssetPath(asset);
                var folderName = Path.GetFileName(Path.GetDirectoryName(assetPath));
                menu.AddItem(new GUIContent(folderName + "/" + asset.name), false, () =>
                {
                    targetProperty.objectReferenceValue = asset;
                    targetProperty.serializedObject.ApplyModifiedProperties();
                });
            }

            menu.ShowAsContext();
        }
    }

#endif
}