using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;

// using Sirenix.OdinInspector;

// ReSharper disable Unity.NoNullPropagation
namespace games.noio.planter
{
    public class BranchSocket : MonoBehaviour
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] List<BranchTemplate> _branchOptions;
        [SerializeField] List<BranchOption> _branchOptions2;

        #endregion

        BranchTemplate _parentTemplate;

        #region PROPERTIES

        BranchTemplate ParentTemplate => _parentTemplate
            ? _parentTemplate
            : _parentTemplate = GetComponentInParent<BranchTemplate>();

        // public IReadOnlyList<BranchTemplate> BranchOptions => _branchOptions;
        public IReadOnlyList<BranchOption> BranchOptions2 => _branchOptions2;

        #endregion

        [Serializable]
        public class BranchOption
        {
            #region PUBLIC AND SERIALIZED FIELDS

            [SerializeField] BranchTemplate _template;
            [SerializeField] float _probabilityPercent;

            #endregion

            #region PROPERTIES

            public BranchTemplate Template
            {
                get => _template;
                set => _template = value;
            }

            public float ProbabilityPercent
            {
                get => _probabilityPercent;
                set => _probabilityPercent = value;
            }

            #endregion
        }

#if UNITY_EDITOR

        public void OnBranchOptionsChanged()
        {
            AddPreviewMesh();
        }

        public void AddPreviewMesh()
        {
            if (_branchOptions2?.Count > 0)
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

                var firstOption = _branchOptions2[0].Template;

                meshFilter.sharedMesh = firstOption.GetMeshVariant(0);
                meshRenderer.sharedMaterials = firstOption.GetComponent<MeshRenderer>().sharedMaterials;
            }
        }

        void AddBranchOption2()
        {
            var namePrefix = ParentTemplate != null ? ParentTemplate.name.Split(' ', '-', '_')[0] : "NONE";

            var allBranches = AssetDatabase.FindAssets("t:GameObject")
                                           .Select(AssetDatabase.GUIDToAssetPath)
                                           .Select(AssetDatabase.LoadAssetAtPath<BranchTemplate>)
                                           .Where(bt => bt != null)
                                           .Where(d => BranchOptions2.Any(bo => bo.Template == d) == false)
                                           .ToList();
            
            var branchesOfSamePlant = allBranches
                                      .Where(d => d.name.StartsWith(namePrefix))
                                      .ToList();

            var menu = new GenericMenu();
            foreach (var branch in branchesOfSamePlant)
            {
                menu.AddItem(new GUIContent(branch.name), false, () =>
                {
                    Undo.RecordObject(this, "Add Branch Option");
                    _branchOptions.Insert(0, branch);
                });
            }

            foreach (var branch in allBranches)
            {
                menu.AddItem(new GUIContent("Other/" + branch.name), false, () =>
                {
                    Undo.RecordObject(this, "Add Branch Option");
                    _branchOptions.Insert(0, branch);
                });
            }

            menu.ShowAsContext();
        }

        public void AddBranchOption(BranchTemplate branch, float probabilityPercent = 100)
        {
            Assert.IsTrue(branch.gameObject.scene == default, "Need prefab object ref of a BranchTemplate");

            if (_branchOptions2 == null)
            {
                _branchOptions2 = new List<BranchOption>();
            }

            _branchOptions2.Add(new BranchOption
            {
                Template = branch,
                ProbabilityPercent = probabilityPercent
            });

            NormalizeProbabilities();

            EditorUtility.SetDirty(this);
        }

        void NormalizeProbabilities()
        {
            var factor = _branchOptions2.Sum(bo => bo.ProbabilityPercent) / 100f;
            foreach (var bo in _branchOptions2)
            {
                if (factor > 0)
                {
                    bo.ProbabilityPercent /= factor;
                }
                else
                {
                    bo.ProbabilityPercent = 100f / _branchOptions2.Count;
                }
            }
        }
#endif
        public bool IsBranchOption(BranchTemplate template)
        {
            return _branchOptions2.Any(o => o.Template == template);
        }
    }
}