using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Serialization;

namespace games.noio.planter
{
    [ExecuteAlways]
    public class BranchTemplate : MonoBehaviour
    {
        #region PUBLIC FIELDS

        [FormerlySerializedAs("NodeDepth")]
        [TitleGroup("Shape", "Parameters that define where & how a plant grows")]
        public int DepthMin;

        public int DepthMax;

        [TitleGroup("Shape")]
        public int MaxCount = 100;

#if UNITY_EDITOR
        [CustomValueDrawer(nameof(MaxQuotaPercentage))]
#endif
        [TitleGroup("Shape")]
        [Tooltip("How much of the plant can be made up of this type of branch")]
        public float Quota = 1;

        [FormerlySerializedAs("MinTotalOtherBranches2")]
        [TitleGroup("Shape")]
        [Tooltip("How many ohter branches (of any type) should the plant have, before" +
                 "this type of branch can grow")]
        public int MinTotalOtherBranches;


        [TitleGroup("Shape")]
        [Tooltip("How much of the plant's FruitEnergy does this branch consume when growing." +
                 "This is equal to the energy that a fruit gives when collected")]
        [GUIColor(nameof(FruitColor))]
        public int FruitEnergyCostInt;


        [TitleGroup("Shape")]
        [HorizontalGroup("Shape/Avoid")]
        [Tooltip("The plant will not grow through colliders on these layers")]
        public LayerMask Avoids;

        [TitleGroup("Shape")]
        [HorizontalGroup("Shape/Avoid", Width = 50)]
        [ToggleLeft]
        [Tooltip("Remove branches if they overlap objects on the avoided layers")]
        public bool RemoveIfOverlaps = true;

        [TitleGroup("Shape")]
        public bool NeedsSurface;

        [TitleGroup("Shape")]
        [EnableIf(nameof(NeedsSurface))]
        public LayerMask Surface;

        [TitleGroup("Shape")]
        [EnableIf(nameof(NeedsSurface))]
        [Range(.2f, 1)]
        public float SurfaceDistance = 1;

        [BoxGroup("Shape/Orientation")]
        public bool MakeHorizontal;

        [Range(0, 180)]
        [HorizontalGroup("Shape/Orientation/Angles")]
        public float MaxPivotAngle;

        [Range(0, 180)]
        [HorizontalGroup("Shape/Orientation/Angles")]
        public float MaxRollAngle;

        [Range(-1, 1)]
        [HorizontalGroup("Shape/Orientation/Angles")]
        public float VerticalBias;

        [Title("Visuals")]
        public float GrowTime = 0.75f;

        public Mesh[] MeshVariants;


        #endregion

        [SerializeField]
        [ReadOnly]
        [PropertyOrder(-1)]
        uint _databaseId;

        Renderer _renderer;

        CapsuleCollider _capsuleCollider;

        bool _preprocessed;

        #region PROPERTIES

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

        public uint DatabaseId
        {
            get => _databaseId;
            set => _databaseId = value;
        }

        public bool IsFruit { get; private set; }

        Color FruitColor() => FruitEnergyCostInt > 0 ? new Color(1f, 0.69f, 0.98f) : Color.white;

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

        public GameObject Make(int variant)
        {
            Assert.IsTrue(_preprocessed, $"{name} has not been Preprocessed");

            _renderer = _renderer ? _renderer : GetComponent<Renderer>();

            GameObject created;
#if UNITY_EDITOR
            if (Application.isPlaying == false)
            {
                // The object is already an instance, so materials etc. are already correct
                // this call to Apply below is only to set a  _variation_
                var instance = PrefabUtility.InstantiatePrefab(gameObject);
                created = (GameObject) instance;
                if (MeshVariants.Length > 0)
                {
                    var meshFilter = created.GetComponent<MeshFilter>();
                    meshFilter.sharedMesh = MeshVariants[variant % MeshVariants.Length];
                }
            }

            else
#endif
            {
                created = new GameObject {name = name, layer = gameObject.layer};

                if (MeshVariants.Length > 0)
                {
                    var meshFilter = created.GetComponent<MeshFilter>();
                    meshFilter.sharedMesh = MeshVariants[variant % MeshVariants.Length];
                }

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

            }

            return created;
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

            IsFruit = FruitEnergyCostInt > 0;
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

        public bool IsCapsuleColliderCentered => Mathf.Abs(Capsule.height - Capsule.center.z * 2) < 0.0001f &&
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

        [PropertyOrder(-1)]
        [TitleGroup("Branching", "Which branches will grow out of this branch, and where")]
        // [HorizontalGroup("Branching/Sockets")]
        [EnableIf(nameof(HasSockets))]
        [Button(ButtonSizes.Large, Name = "@" + nameof(ToggleSocketsButtonName))]
        [GUIColor(nameof(ToggleSocketsColor))]
        void ToggleSockets()
        {
            var visi = AnySocketVisible() == false;
            SetSocketsVisible(visi);
            _preprocessed = false;
        }

        string ToggleSocketsButtonName => AnySocketVisible() ? "Hide Sockets before Applying Prefab" : "Show Sockets";

        Color ToggleSocketsColor => AnySocketVisible() ? new Color(1f, 0.65f, 0.53f) : Color.white;

        [HorizontalGroup("Branching/Sockets")]
        [OnInspectorGUI]
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
                                GUILayout.Label(branches, SirenixGUIStyles.BoldLabel);
                            }
                        }
                    }
                }
            }
        }

        [HorizontalGroup("Branching/Sockets", Width = 100)]
        [VerticalGroup("Branching/Sockets/Buttons")]
        [Button(ButtonSizes.Large, Name = "Add Socket")]
        void CreateSocket()
        {
            SetSocketsVisible(true);
            var go = new GameObject("Socket");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.forward;
            var socket = go.AddComponent<BranchSocket>();
            socket.BranchOptions = new List<BranchTemplate> {this};
            socket.OnBranchOptionsChanged();
        }

        [ShowIf(nameof(AnySocketVisible))]
        [VerticalGroup("Branching/Sockets/Buttons")]
        [Button(Name = "Refresh")]
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

        [TitleGroup("Actions")]
        [GUIColor(1, .56f, .49f)]
        [HideIf(nameof(IsCapsuleColliderCentered))]
        [Button]
        void FixCapsuleColliderPosition()
        {
            if (Capsule != null)
            {
                var center = new Vector3(0, 0, Capsule.height / 2);
                Capsule.center = center;
            }
        }


#endif

        #endregion
    }
}