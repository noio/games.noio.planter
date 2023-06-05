using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;

// using Sirenix.OdinInspector;
// using Sirenix.Utilities.Editor;

namespace games.noio.planter
{
    [ExecuteAlways]
    public class BranchTemplate : MonoBehaviour
    {
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
        [Range(0,100)]
        float _quotaPercent = 100;

        [Tooltip("How many other branches (of any type) should the plant have, before " +
                 "this type of branch can grow")]
        [SerializeField]
        int _minTotalOtherBranches;

        [Tooltip("This branch will not grow through colliders on these layers")]
        [SerializeField]
        LayerMask _obstacleLayers = 1;

        [Tooltip("If any layer set: the branch will only grow if it can stick to surfaces on these layers")]
        [SerializeField] LayerMask _surfaceLayers = 1;
        [Tooltip("Maximum distance between the branch and the surface defined above")]
        [SerializeField] float _surfaceDistance = 1;
        
        [Tooltip("Branches are randomly rotated (relative to their socket rotation). By how much?")]
        [Range(0, 180)] [SerializeField] float _maxPivotAngle = 30;
        [Tooltip("Branches are randomly rolled (around the Z-axis). By how much?")]
        [Range(0, 180)] [SerializeField] float _maxRollAngle = 30;
        [Tooltip("After random rotation, should the branches be tilted towards the sky?")]
        [Range(-1, 1)] [SerializeField] float _growUpwards;
        [Tooltip("After selecting a random direction for the branch, roll it (around the Z axis) to make " +
                 "its up-vector face upwards.")]
        [SerializeField] bool _faceUpwards;
        
        [SerializeField] Mesh[] _meshVariants;

        #endregion

        Renderer _renderer;
        CapsuleCollider _capsuleCollider;
        bool _preprocessed;

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
                if (_capsuleCollider == null)
                {
                    _capsuleCollider = GetComponent<CapsuleCollider>();
                }

                return _capsuleCollider;
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

        public Mesh GetMeshVariant(int variant)
        {
            return _meshVariants.Length == 0
                ? GetComponent<MeshFilter>().sharedMesh
                : _meshVariants[variant % _meshVariants.Length];
        }

        public Mesh GetRandomMeshVariant()
        {
            if (_meshVariants.Length == 0)
            {
                return GetComponent<MeshFilter>().sharedMesh;
            }

            var idx = Random.Range(0, _meshVariants.Length);
            return _meshVariants[idx];
        }

        public Branch CreateBranch()
        {
            Assert.IsTrue(_preprocessed, $"{name} has not been Preprocessed");

            _renderer = _renderer ? _renderer : GetComponent<Renderer>();

            var created = new GameObject { name = name, layer = gameObject.layer };

            var branch = created.AddComponent<Branch>();

            var meshFilter = created.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GetRandomMeshVariant();

            var newRenderer = created.AddComponent<MeshRenderer>();
            newRenderer.sharedMaterials = _renderer.sharedMaterials;
            newRenderer.shadowCastingMode = _renderer.shadowCastingMode;

            var newCapsuleCollider = created.AddComponent<CapsuleCollider>();
            newCapsuleCollider.center = Capsule.center;
            newCapsuleCollider.direction = Capsule.direction;
            newCapsuleCollider.height = Capsule.height;
            newCapsuleCollider.radius = Capsule.radius;
            newCapsuleCollider.direction = Capsule.direction;
            newCapsuleCollider.sharedMaterial = Capsule.sharedMaterial;

            return branch;
        }

        public void Preprocess(bool force = false)
        {
            /*
             * Only Prefabs should be preprocessed.
             */
            Assert.IsTrue(gameObject.scene == default, "BranchTemplate should not be instantiated.");

            if (_preprocessed && !force)
            {
                return;
            }

            _preprocessed = true;

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

        static float MaxQuotaPercentage(float value, GUIContent label)
        {
            using (new GUILayout.HorizontalScope())
            {
                var newValue = EditorGUILayout.Slider(label, value * 100, 0, 100) / 100;
                GUILayout.Label("%", GUILayout.Width(16));
                return newValue;
            }
        }

        public bool ValidateCapsuleCollider => Capsule.direction == 2 &&
                                               Mathf.Abs(Capsule.height - Capsule.center.z * 2) < 0.0001f &&
                                               ((Vector2)Capsule.center).sqrMagnitude < 0.0001f;

        public void SetSocketsVisible(bool visible)
        {
            foreach (Transform child in transform)
            {
                child.gameObject.SetActive(visible);
            }
        }

        // [OnInspectorGUI]
        // void BeforeSocketsGUI()
        // {
        //     if (AnySocketVisible())
        //     {
        //         EditorGUILayout.HelpBox("Hide sockets before applying prefab", MessageType.Warning);
        //     }
        // }

        // [PropertyOrder(-1)]
        // [TitleGroup("Branching", "Which branches will grow out of this branch, and where")]

        // [HorizontalGroup("Branching/Sockets")]
        // [EnableIf(nameof(HasSockets))]
        // [Button(ButtonSizes.Large, Name = "@" + nameof(ToggleSocketsButtonName))]
        // [GUIColor(nameof(ToggleSocketsColor))]
        void ToggleSockets()
        {
            var visi = AnySocketVisible() == false;
            SetSocketsVisible(visi);
            _preprocessed = false;
        }

        string ToggleSocketsButtonName =>
            AnySocketVisible() ? "Hide Sockets before Applying Prefab" : "Show Sockets";

        Color ToggleSocketsColor => AnySocketVisible() ? new Color(1f, 0.65f, 0.53f) : Color.white;

        // [HorizontalGroup("Branching/Sockets")]
        // [OnInspectorGUI]
        void DrawBranchInfo()
        {
            if (Selection.gameObjects.Length == 1)
            {
                foreach (Transform child in transform)
                {
                    if (child.TryGetComponent(out BranchSocket socket))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (socket.BranchOptions != null && socket.BranchOptions.Count > 0)
                            {
                                GUILayout.Label(socket.name, GUILayout.Width(80));
                                var branches = string.Join(", ", socket.BranchOptions.Select(o => o.name));

                                // GUILayout.Label(branches, SirenixGUIStyles.BoldLabel);
                            }
                        }
                    }
                }
            }
        }

        // [HorizontalGroup("Branching/Sockets", Width = 100)]
        // [VerticalGroup("Branching/Sockets/Buttons")]
        // [Button(ButtonSizes.Large, Name = "Add Socket")]
        void CreateSocket()
        {
            SetSocketsVisible(true);
            var go = new GameObject("Socket");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.forward;
            var socket = go.AddComponent<BranchSocket>();

            // socket.BranchOptions = new List<BranchTemplate> { this };
            socket.OnBranchOptionsChanged();
        }

        // [ShowIf(nameof(AnySocketVisible))]
        // [VerticalGroup("Branching/Sockets/Buttons")]
        // [Button(Name = "Refresh")]
        void RefreshSocketsPreviewMesh()
        {
            Undo.RecordObject(gameObject, "Refresh Sockets");
            foreach (var socket in GetComponentsInChildren<BranchSocket>(true))
            {
                socket.RefreshSocketPreviewMesh();
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

        // [TitleGroup("Actions")]
        // [GUIColor(1, .56f, .49f)]
        // [HideIf(nameof(ValidateCapsuleCollider))]
        // [Button]
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