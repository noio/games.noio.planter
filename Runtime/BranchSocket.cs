using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// ReSharper disable Unity.NoNullPropagation
namespace games.noio.planter
{
    public class BranchSocket : MonoBehaviour
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] List<BranchSocketOption> _branchOptions;

        #endregion

        #region PROPERTIES

        public IReadOnlyList<BranchSocketOption> BranchOptions => _branchOptions;

        #endregion

        public bool ContainsBranchOption(BranchTemplate template, out float weight)
        {
            var option = _branchOptions.FirstOrDefault(o => o.Template == template);
            if (option != null)
            {
                weight = option.ProbabilityPercent;
                return true;
            }
            else
            {
                weight = 0;
                return false;
            }
        }

#if UNITY_EDITOR

        public void OnBranchOptionChanged()
        {
            InitializeEmptyBranchOptions();
            NormalizeProbabilities();
            AddOrUpdatePreviewMesh();
        }

        public void OnBranchOptionRemoved(int removeIndex)
        {
        }

        public void AddOrUpdatePreviewMesh()
        {
            if (_branchOptions == null)
            {
                return;
            }

            foreach (var option in _branchOptions)
            {
                if (option != null && option.Template != null)
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

                    meshFilter.sharedMesh = option.Template.GetRandomMeshVariant();
                    meshRenderer.sharedMaterials =
                        option.Template.GetComponent<MeshRenderer>().sharedMaterials;
                    
                    if (meshFilter.sharedMesh != null)
                    {
                        /*
                         * We successfully set a mesh, so we're done.
                         */
                        EditorUtility.SetDirty(gameObject);
                        return;
                    }
                }
            }
        }

        void InitializeEmptyBranchOptions()
        {
            foreach (var option in _branchOptions)
            {
                if (option.Template != null)
                {
                    continue;
                }

                var templateInstance = GetComponentInParent<BranchTemplate>();
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                string templateAssetPath;
                if (stage != null && stage.prefabContentsRoot.transform == transform.root)
                {
                    templateAssetPath = stage.assetPath;
                }
                else
                {
                    templateAssetPath =
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(templateInstance);
                }

                if (string.IsNullOrEmpty(templateAssetPath))
                {
                    Debug.LogError($"Could not find Prefab Asset for {name}");
                }

                var templateAsset = AssetDatabase.LoadAssetAtPath<BranchTemplate>(templateAssetPath);

                // Debug.Log($"Path: {templateAssetPath} Asset: {templateAsset}");
                option.Template = templateAsset;
                option.ProbabilityPercent = 100;
            }
        }

        public void AddBranchOption(BranchTemplate branch, float probabilityPercent = 100)
        {
            Assert.IsTrue(branch.gameObject.scene == default, "Need prefab object ref of a BranchTemplate");

            if (_branchOptions == null)
            {
                _branchOptions = new List<BranchSocketOption>();
            }

            _branchOptions.Add(new BranchSocketOption
            {
                Template = branch,
                ProbabilityPercent = probabilityPercent
            });

            NormalizeProbabilities();
            AddOrUpdatePreviewMesh();

            EditorUtility.SetDirty(this);
        }

        void NormalizeProbabilities()
        {
            switch (_branchOptions.Count)
            {
                case 0:
                    return;
                case 1:
                    _branchOptions[0].ProbabilityPercent = 100;
                    return;
            }

            var factor = _branchOptions.Sum(bo => bo.ProbabilityPercent) / 100f;
            foreach (var bso in _branchOptions)
            {
                if (factor > 0)
                {
                    bso.ProbabilityPercent /= factor;
                }
                else
                {
                    bso.ProbabilityPercent = 100f / _branchOptions.Count;
                }
            }
        }

        public void NormalizeProbabilities(int keepFixed)
        {
            switch (_branchOptions.Count)
            {
                case 0:
                    return;
                case 1:
                    _branchOptions[0].ProbabilityPercent = 100;
                    return;
            }

            /*
             * Sum of other probabilities
             */
            var fixedOption = _branchOptions[keepFixed];
            fixedOption.ProbabilityPercent = Mathf.Clamp(fixedOption.ProbabilityPercent, 0, 100);
            var otherSum = _branchOptions.Where(option => option != fixedOption)
                                         .Sum(option => option.ProbabilityPercent);

            var remainingProbability = (100f - fixedOption.ProbabilityPercent);
            var factor = otherSum / remainingProbability;

            foreach (var option in _branchOptions)
            {
                if (option == fixedOption)
                {
                    continue;
                }

                if (factor > 0)
                {
                    option.ProbabilityPercent /= factor;
                }
                else
                {
                    option.ProbabilityPercent = remainingProbability / _branchOptions.Count;
                }
            }
        }
#endif
    }
}