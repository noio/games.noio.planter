using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

// ReSharper disable Unity.NoNullPropagation
namespace games.noio.planter
{
    public class BranchSocket : MonoBehaviour
    {
        #region PUBLIC FIELDS

#if UNITY_EDITOR
        [OnValueChanged(nameof(OnBranchOptionsChanged))]
        [ListDrawerSettings(CustomAddFunction = nameof(AddBranchOption), Expanded = true)]
#endif
        public List<BranchTemplate> BranchOptions;

        #endregion

        BranchTemplate _parentTemplate;

        #region PROPERTIES

        BranchTemplate ParentTemplate => _parentTemplate ? _parentTemplate : _parentTemplate = GetComponentInParent<BranchTemplate>();

        #endregion

#if UNITY_EDITOR

        public void OnBranchOptionsChanged()
        {
            ParentTemplate?.SetSocketsVisible(true);
            RefreshSocketPreviewMesh();
        }

        public void RefreshSocketPreviewMesh()
        {
            if (BranchOptions.Count > 0)
            {
                Undo.RecordObject(gameObject, "Change Socket Mesh");

                if (TryGetComponent<MeshFilter>(out var meshFilter) == false)
                {
                    meshFilter = gameObject.AddComponent<MeshFilter>();
                }

                if (TryGetComponent<MeshRenderer>(out var meshRenderer) == false)
                {
                    meshRenderer = gameObject.AddComponent<MeshRenderer>();
                }

                var firstOption = BranchOptions[0];
    
                meshFilter.sharedMesh = firstOption.GetMeshVariant(0);
                meshRenderer.sharedMaterials = firstOption.GetComponent<MeshRenderer>().sharedMaterials;
            }
        }

        void AddBranchOption()
        {
            var namePrefix = ParentTemplate != null ? ParentTemplate.name.Split(' ', '-', '_')[0] : "NONE";

            var allBranches = AssetDatabase.FindAssets("t:GameObject")
                                           .Select(AssetDatabase.GUIDToAssetPath)
                                           .Select(AssetDatabase.LoadAssetAtPath<BranchTemplate>)
                                           .ToList();
            var branchesOfSamePlant = allBranches
                                      .Where(d => d.name.StartsWith(namePrefix))
                                      .Where(d => BranchOptions.Contains(d) == false)
                                      .ToList();


            var menu = new GenericMenu();
            foreach (var branch in branchesOfSamePlant)
            {
                menu.AddItem(new GUIContent(branch.name), false, () =>
                {
                    Undo.RecordObject(this, "Add Branch Option");
                    BranchOptions.Insert(0, branch);
                });
            }


            foreach (var branch in allBranches)
            {
                menu.AddItem(new GUIContent("Other/" + branch.name), false, () =>
                {
                    Undo.RecordObject(this, "Add Branch Option");
                    BranchOptions.Insert(0, branch);
                });
            }

            menu.ShowAsContext();
        }
#endif
    }
}