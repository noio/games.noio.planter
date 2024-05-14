using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using Random = UnityEngine.Random;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace games.noio.planter
{
    [RequireComponent(typeof(CapsuleCollider))]
    public class BranchTemplate : MonoBehaviour
    {
        public const int MaxSockets = 4;

        #region PUBLIC AND SERIALIZED FIELDS

        [Tooltip("Where in the plant is this branch allowed to occur?")]
        [SerializeField]
        int _depthMin;

        [Tooltip("Where in the plant is this branch allowed to occur?")]
        [SerializeField]
        int _depthMax = 12;

        [SerializeField] int _maxCount = 500;

        [Tooltip("How much of the plant can be made up of this type of branch")]
        [SerializeField]
        [Range(0, 100)]
        float _quotaPercent = 100;

        [Tooltip("How many other branches (of any type) should the plant have, before " +
                 "this type of branch can grow")]
        [SerializeField]
        int _minTotalOtherBranches;

        [Tooltip("This branch will not grow through colliders on these layers")]
        [SerializeField]
        LayerMask _obstacleLayers = 1;

        [Tooltip("If any layer set: the branch will only grow if it can stick to surfaces on these layers")]
        [SerializeField]
        LayerMask _surfaceLayers = 1;

        [Tooltip("Maximum distance between the branch and the surface defined above")]
        [SerializeField]
        float _surfaceDistance = 1;

        [Tooltip("Branches are randomly rotated (relative to their socket rotation). By how much?")]
        [Range(0, 180)]
        [SerializeField]
        float _maxPivotAngle = 30;

        [Tooltip("Branches are randomly rolled (around the Z-axis). By how much?")]
        [Range(0, 180)]
        [SerializeField]
        float _maxRollAngle = 30;

        [Tooltip("After random rotation, should the branches be tilted towards the sky?")]
        [Range(-1, 1)]
        [SerializeField]
        float _growUpwards;

        [Tooltip("After selecting a random direction for the branch, roll it (around the Z axis) to make " +
                 "its up-vector face upwards.")]
        [SerializeField]
        bool _faceUpwards;

        [SerializeField] BranchMeshVariant[] _meshVariants;

        #endregion

        Renderer _renderer;
        CapsuleCollider _capsule;

        #region PROPERTIES

        [Tooltip("How much of the plant can be made up of this type of branch")]
        public float QuotaPercent => _quotaPercent;

        [Tooltip("How many other branches (of any type) should the plant have, before" +
                 "this type of branch can grow")]
        public int MinTotalOtherBranches => _minTotalOtherBranches;

        [Tooltip("The plant will not grow through colliders on these layers")]
        public LayerMask ObstacleLayers => _obstacleLayers;

        public bool NeedsSurface => _surfaceLayers != 0;
        public LayerMask SurfaceLayers => _surfaceLayers;

        // [TitleGroup("Shape")]
        // [EnableIf(nameof(NeedsSurface))]
        public float SurfaceDistance => _surfaceDistance;

        // [BoxGroup("Shape/Orientation")]
        public bool FaceUpwards => _faceUpwards;
        public float MaxPivotAngle => _maxPivotAngle;
        public float MaxRollAngle => _maxRollAngle;

        // [HorizontalGroup("Shape/Orientation/Angles")]
        public float GrowUpwards => _growUpwards;

        // [TitleGroup("Shape", "Parameters that define where & how a plant grows")]
        public int DepthMin => _depthMin;

        // [TitleGroup("Shape")]
        public int DepthMax => _depthMax;

        // [TitleGroup("Shape")]
        public int MaxCount => _maxCount;
        public List<BranchSocket> Sockets { get; private set; }

        public CapsuleCollider Capsule
        {
            get
            {
                if (_capsule == null)
                {
                    _capsule = GetComponent<CapsuleCollider>();
                }

                return _capsule;
            }
        }

        #endregion

        #region MONOBEHAVIOUR METHODS

        void Awake()
        {
            /*
             * This should never awake in a game.
             * The prefab is only used to build
             * another gameobject.
             */
            Assert.IsTrue(Application.isEditor);
        }

        #endregion

        public Mesh GetRandomMeshVariant()
        {
            if (_meshVariants.Length > 0)
            {
                var picked = Utils.Utils.PickWeighted(_meshVariants, v => v.ProbabilityPercent);
                if (picked != null && picked.Mesh != null)
                {
                    return picked.Mesh;
                }
            }

            /*
             * Fallback: (could still return null if no mesh is set)
             */
            return GetComponent<MeshFilter>().sharedMesh;
        }
        
        public void NormalizeMeshVariantProbabilities()
        {
            switch (_meshVariants.Length)
            {
                case 0:
                    return;
                case 1:
                    _meshVariants[0].ProbabilityPercent = 100;
                    return;
            }

            var factor = _meshVariants.Sum(bo => bo.ProbabilityPercent) / 100f;
            foreach (var bso in _meshVariants)
            {
                if (factor > 0)
                {
                    bso.ProbabilityPercent /= factor;
                }
                else
                {
                    bso.ProbabilityPercent = 100f / _meshVariants.Length;
                }
            }
        }

        public Branch CreateBranch()
        {
            _renderer = _renderer ? _renderer : GetComponent<Renderer>();

            var gameOb = new GameObject { name = name, layer = gameObject.layer };
            gameOb.isStatic = true;

            var branch = gameOb.AddComponent<Branch>();

            var meshFilter = gameOb.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GetRandomMeshVariant();

            var newRenderer = gameOb.AddComponent<MeshRenderer>();
            newRenderer.sharedMaterials = _renderer.sharedMaterials;
            newRenderer.shadowCastingMode = _renderer.shadowCastingMode;

            var newCapsuleCollider = gameOb.AddComponent<CapsuleCollider>();
            newCapsuleCollider.center = Capsule.center;
            newCapsuleCollider.direction = Capsule.direction;
            newCapsuleCollider.height = Capsule.height;
            newCapsuleCollider.radius = Capsule.radius;
            newCapsuleCollider.direction = Capsule.direction;
            newCapsuleCollider.sharedMaterial = Capsule.sharedMaterial;

            return branch;
        }

        public void FindSockets()
        {
            Sockets = new List<BranchSocket>();
            foreach (Transform child in transform)
            {
                if (child.TryGetComponent(out BranchSocket socket))
                {
                    Sockets.Add(socket);
                }
            }
        }

        #region EDITOR

#if UNITY_EDITOR

        public BranchSocket CreateSocket()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                throw new Exception("Can only add branch with Prefab open.");
            }

            Assert.IsTrue(Sockets.Count < MaxSockets);

            var go = new GameObject($"Socket [{Sockets.Count}]");
            go.transform.SetParent(transform, false);
            var l = Capsule.height;

            switch (Sockets.Count)
            {
                case 0:
                    go.transform.localPosition = Vector3.forward;
                    break;
                case 1:
                    go.transform.localPosition = Vector3.forward;
                    go.transform.localEulerAngles = new Vector3(0, 45);
                    break;
                case 2:
                    go.transform.localPosition = Vector3.forward;
                    go.transform.localEulerAngles = new Vector3(0, -45);
                    break;
                default:
                    go.transform.localPosition = new Vector3(0, 0, 0.5f);
                    go.transform.localEulerAngles = new Vector3(-45, 0);
                    break;
            }

            var socket = go.AddComponent<BranchSocket>();
            Sockets.Add(socket);

            var path = stage.assetPath;
            var templatePrefab = AssetDatabase.LoadAssetAtPath<BranchTemplate>(path);

            Debug.Log($"Template Prefab: {templatePrefab} {AssetDatabase.GetAssetPath(templatePrefab)}");
            Assert.IsNotNull(templatePrefab);

            socket.AddBranchOption(templatePrefab);
            socket.OnBranchOptionChanged();
            return socket;
        }

        void RefreshSocketsPreviewMesh()
        {
            Undo.RecordObject(gameObject, "Refresh Sockets");
            foreach (var socket in GetComponentsInChildren<BranchSocket>(true))
            {
                socket.AddOrUpdatePreviewMesh();
            }
        }

        bool HasSockets()
        {
            return transform.childCount > 0;
        }

        bool AnySocketVisible()
        {
            foreach (Transform child in transform)
            {
                if (child.gameObject.activeSelf)
                {
                    return true;
                }
            }

            return false;
        }

        void FixCapsuleColliderPosition()
        {
            if (Capsule != null)
            {
                Capsule.direction = 2;
                var center = new Vector3(0, 0, Capsule.height / 2);
                Capsule.center = center;
            }
        }

#endif

        #endregion
    }
}