using UnityEditor;
using UnityEngine;

namespace games.noio.planter
{
    [CustomPropertyDrawer(typeof(BranchMeshVariant))]
    public class BranchMeshVariantDrawer : PropertyDrawer
    {
        #region MONOBEHAVIOUR METHODS

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var probabilityRect = new Rect(position.x, position.y, 40f,
                EditorGUIUtility.singleLineHeight);
            var percentLabelRect = new Rect(probabilityRect.xMax + 5, position.y, 20f,
                EditorGUIUtility.singleLineHeight);
            var meshRect = new Rect(percentLabelRect.xMax, position.y,
                position.width - percentLabelRect.xMax - 10,
                EditorGUIUtility.singleLineHeight);

            var probabilityProp = property.FindPropertyRelative("_probabilityPercent");
            var meshProp = property.FindPropertyRelative("_mesh");

            EditorGUI.PropertyField(probabilityRect, probabilityProp, GUIContent.none);
            EditorGUI.LabelField(percentLabelRect, "%");
            EditorGUI.PropertyField(meshRect, meshProp, GUIContent.none);

            EditorGUI.EndProperty();
        }

        #endregion

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        }
    }
}