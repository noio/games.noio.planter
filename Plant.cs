﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
// using Sirenix.OdinInspector;
// using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

// ReSharper disable Unity.InefficientPropertyAccess
namespace games.noio.planter
{
    /// <summary>
    ///     The PlantRuntimeComponent is attached to all plants
    ///     that are show in runtime or in the editor. It is basically
    ///     the MonoBehaviour that represents all plants, because
    ///     {PlantCore} is a pure (non-serializable) data representation
    /// </summary>
    [ExecuteInEditMode]
    public class Plant : MonoBehaviour
    {
        const uint MaxBranches = 4;
        const uint MaxDepth = 12;
        public const uint RootAddress = 1;
        static readonly int HighlightColor = Shader.PropertyToID("_HighlightColor");
        static readonly uint MaxAddress = (uint)Mathf.Pow(MaxBranches, MaxDepth + 1);
        static readonly Regex AddressRegex = new Regex(@"D\d+\.S\d+\.A(\d+)");
        static readonly Collider[] ColliderCache = new Collider[2];
        public static bool DebugPlantGrowth;

        #region PUBLIC AND SERIALIZED FIELDS

        // [ReadOnly] 
        public Transform RootTransform;
        [FormerlySerializedAs("Definition")] [SerializeField] PlantDefinition _definition;

        #endregion

        List<Branch> _branches = new();

        // Queue<(uint Address, int Mask)> _openSockets = new();
        Queue<Branch> _branchesWithOpenSockets = new();
        List<BranchType> _branchTypes = new();
        List<BranchType> _growableBranchTypes;
        int _nextSocketIndex;
        Vector3 _settledPosition;

        #region PROPERTIES

        public int Variant { get; private set; }
        public bool Initialized { get; private set; }

        // [TitleGroup("Status")]
        // [ShowInInspector]
        // [PropertyOrder(1)]
        // [ProgressBar(0, nameof(MaxStoredEnergy), 0.62f, 1f, 0.45f)]
        // [ShowIf(nameof(Initialized))]
        public int Energy { get; private set; }

        /// <summary>
        ///     The number of fruits is subtracted from the energy cap, because
        ///     that energy is stored inside the fruit, if it were.
        /// </summary>
        public int MaxStoredEnergy => _definition.MaxStoredEnergyInt;

        public bool AllSocketsFilled => AnyOpenSocketsLeft == false;
        public bool AllowGrowAnimation { get; set; } = true;
        public bool GrowthBlocked { get; private set; }
        public int BranchCount => _branches.Count;

        /// <summary>
        ///     Is this plant considered to be "Fully Grown".
        /// </summary>
        public bool FullyGrown => AllSocketsFilled || GrowthBlocked;

        // [TitleGroup("Status")]
        // [ShowInInspector]
        // [PropertyOrder(2)]
        // [ProgressBar(-10, nameof(MaxGrowAttempts), 0.95f, 0.68f, 0.37f)]
        // [ShowIf(nameof(Initialized))]
        public int FailedGrowAttempts { get; private set; }

        int MaxGrowAttempts => _definition.MaxGrowAttempts;

        bool AnyOpenSocketsLeft => (_branchTypes?.Any(bt => bt.Growable) ?? false) &&
                                   _branchesWithOpenSockets?.Count > 0;

        /// <summary>
        ///     A mask indicating which branch types
        ///     can currently be grown
        /// </summary>

        // int GrowableBranchTypeMask { get; set; }

        #endregion

        #region MONOBEHAVIOUR METHODS

        #endregion

        public void ClearAndInitialize()
        {
            DisableRootPlaceholderRenderer();

            _branchTypes = new List<BranchType>();
            _branches = new List<Branch>();

            // _openSockets = new Queue<(uint Address, int Mask)>();
            _branchesWithOpenSockets = new Queue<Branch>();
            _growableBranchTypes = new List<BranchType>();

            // _branchTypes.Clear();
            PreprocessBranchType(_definition.RootNode);

            Variant = Random.Range(0, 17);

            /*
             * Clear out the nodes
             */
            for (var i = RootTransform.childCount - 1; i >= 0; i--)
            {
                var child = RootTransform.GetChild(i);
                DestroyImmediate(child.gameObject);
            }

            AddBranch(new Branch
            {
                Depth = 0,
                GameObject = null,
                BranchType = _branchTypes[0], // root branch type is always first
                RelRotation = Quaternion.identity
            });

            /*
             * Need to completely rebuild the OpenSockets Queue.
             */
            _branchesWithOpenSockets.Enqueue(_branches[0]);
            SetGrowableBranchTypes();

            // SetBranches(new Dictionary<uint, Branch>
            // {
            //     {
            //         RootAddress, new Branch
            //         {
            //             GameObject = null,
            //             Template = Definition.RootNode,
            //             RelRotation = Quaternion.identity
            //         }
            //     }
            // }, true);

            Initialized = true;
            Unblock();
        }

        /// <summary>
        ///     Perform a number of settling steps
        ///     Settling is most essential on meshColliders, where the collision
        ///     with the seed tends to be way off.
        /// </summary>
        /// <param name="steps"></param>
        /// <param name="lerpAmount"></param>
        public void Settle(int steps, float lerpAmount)
        {
            while (steps-- > 0)
            {
                SettleStep(lerpAmount);
            }
        }

        void DisableRootPlaceholderRenderer()
        {
            if (RootTransform.TryGetComponent<MeshRenderer>(out var meshRenderer))
            {
                /*
                 * The MeshRenderer on the RootTransform is only for previewing in
                 * the Editor.
                 * In playmode, just get rid of it.
                 */
                if (Application.isPlaying)
                {
                    Destroy(meshRenderer);
                }
                else
                {
                    meshRenderer.enabled = false;
                }
            }
        }

        /// <summary>
        ///     Perform one pass of 'settling' the plant on underlying objects (Moving it closer)
        /// </summary>
        /// <returns></returns>
        bool SettleStep(float lerpAmount = .2f)
        {
            var rayOrigin = transform.TransformPoint(new Vector3(0, 2, 0));
            var rayDirection = transform.TransformDirection(Vector3.down);
            var rootNode = _definition.RootNode;
            var layers = rootNode.Avoids ^ (1 << rootNode.gameObject.layer);
            if (Physics.SphereCast(rayOrigin, rootNode.Capsule.radius, rayDirection, out var hitInfo, 5,
                    layers))
            {
                transform.position = Vector3.Lerp(transform.position, hitInfo.point, lerpAmount);
                transform.rotation = Quaternion.Lerp(transform.rotation,
                    Quaternion.LookRotation(hitInfo.normal, -transform.forward) * Quaternion.Euler(90, 0, 0),
                    lerpAmount);

                return true;
            }

            return false;
        }

        void PreprocessBranchType(BranchTemplate template)
        {
            /*
             * Skip templates that we already added
             */
            if (_branchTypes.Any(bt => bt.Template == template))
            {
                return;
            }

            // Debug.Log($"Added branch type to {name}: {template.name}");

            if (_branchTypes.Count >= 32)
            {
                /*
                 * This has something to do with the method of tracking open sockets
                 * (in an int bitmask). Maybe those are actually 64 bit but i'm
                 * too lazy to make sure right now.
                 */
                throw new Exception("Plants with more than 32 types of branches are not supported.");
            }

            template.Preprocess();

            var branchType = new BranchType(template, 1 << _branchTypes.Count);

            _branchTypes.Add(branchType);

            foreach (var socket in template.Sockets)
            {
                foreach (var branchOption in socket.BranchOptions)
                {
                    PreprocessBranchType(branchOption);
                }
            }
        }

        #region GROWING

        class Branch
        {
            #region PUBLIC AND SERIALIZED FIELDS

            public BranchType BranchType;
            public GameObject GameObject;
            public Branch Parent;
            public Branch[] Children = new Branch[4];

            /*
             * Position and rotation of the node relative
             * to the RootTransform of the plant
             * (the transform of the RootTransform GameObject)
             * (not to the parent node!)
             */
            public Vector3 RelPosition;
            public Quaternion RelRotation;
            public int Depth { get; set; }

            #endregion

            public IEnumerator<int> OpenSockets()
            {
                for (int i = 0; i < 4; i++)
                {
                    if (Children[i] == null)
                    {
                        yield return i;
                    }
                }
            }
        }

        class BranchType
        {
            #region PUBLIC AND SERIALIZED FIELDS

            public readonly BranchTemplate Template;
            public readonly int BitMask;

            #endregion

            public BranchType(BranchTemplate template, int bitMask)
            {
                Template = template;
                BitMask = bitMask;

                // var mask = Convert.ToString(BitMask, 2).PadLeft(8, '0');
                // Debug.Log($"Create BranchTypeCache for {Template.name}. Mask: {mask}");
            }

            #region PROPERTIES

            public int TotalCount { get; set; }
            public bool Growable { get; set; }

            #endregion
        }

        public static uint GetParentAddress(uint address)
        {
            return address / MaxBranches;
        }

        static uint GetBranchAddress(uint parentAddress, int childIndex)
        {
            Assert.IsTrue(childIndex < MaxBranches);
            return parentAddress * MaxBranches + (uint)childIndex;
        }

        public static int GetSocketIndex(uint address)
        {
            return (int)(address % MaxBranches);
        }

        static int GetBranchDepth(uint address)
        {
            return Mathf.RoundToInt(Mathf.Log(address) / 1.3862943611f); // Log(4)
        }

        /// <summary>
        ///     Returns the distance along the axis of the capsule towards the
        ///     centers of the 'start' and 'end' as used by Physics.CheckCapsule
        /// </summary>
        /// <param name="radius"></param>
        /// <param name="height"></param>
        /// <param name="endDist">The center of the sphere at the end of the capsule.</param>
        /// <returns>The center of the sphere at the start of the capsule</returns>
        static float CheckCapsuleDistances(float radius, float height, out float endDist)
        {
            var startDist = Mathf.Min(height - radius, 3 * radius);
            endDist = Mathf.Max(startDist, height - radius);
            return startDist;
        }

        /// <summary>
        ///     Try for a number of attempts to find a node to grow
        /// </summary>
        /// <param name="attempts"></param>
        /// <param name="branch"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        bool Grow(int attempts, out Branch branch)
        {
            while (attempts-- > 0)
            {
                branch = FindBranchToGrow();
                if (branch != null)
                {
                    Energy -= _definition.EnergyPerBranchInt;

                    /*
                     * When adding a branch, there might
                     * now be other branches that are growable
                     */
                    SetGrowableBranchTypes();

                    return true;
                }
            }

            if (FailedGrowAttempts > MaxGrowAttempts)
            {
                GrowthBlocked = true;
            }

            branch = null;
            return false;
        }

        public static Quaternion RotateAroundZ(float zRad)
        {
            // float rollOver2 = 0;
            var halfAngle = zRad * 0.5f;
            var sinAngle = Mathf.Sin(halfAngle);
            var cosAngle = Mathf.Cos(halfAngle);

            // float yawOver2 = 0;

            return new Quaternion(
                0,
                0,
                sinAngle,
                cosAngle
            );
        }

        public static int NumberOfSetBits(uint i)
        {
            // Java: use int, and use >>> instead of >>
            // C or C++: use uint32_t
            i = i - ((i >> 1) & 0x55555555);
            i = (i & 0x33333333) + ((i >> 2) & 0x33333333);
            return (int)((((i + (i >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24);
        }

        Branch FindBranchToGrow()
        {
            if (_branchesWithOpenSockets.Count == 0)
            {
                FailedGrowAttempts++;
                return null;
            }

            var parent = _branchesWithOpenSockets.Peek();
            BranchSocket openSocket = null;
            while (openSocket == null)
            {
                if (_nextSocketIndex < parent.BranchType.Template.Sockets.Count)
                {
                    if (parent.Children[_nextSocketIndex] == null)
                    {
                        openSocket = parent.BranchType.Template.Sockets[_nextSocketIndex];
                    }
                    else
                    {
                        _nextSocketIndex++;
                    }
                }
                else
                {
                    /*
                     * We've gone through all sockets on this branch, put it at the
                     * end of the queue. It could still have open sockets though,
                     * since it's only removed from the queue once it no longer has open
                     * sockets (when a branch is grown from the last open socket).
                     */
                    _nextSocketIndex = 0;
                    _branchesWithOpenSockets.Dequeue();
                    _branchesWithOpenSockets.Enqueue(parent);
                    parent = _branchesWithOpenSockets.Peek();
                }
            }
            
            _growableBranchTypes.Clear();
            foreach (var bt in _branchTypes)
            {
                if (bt.Growable && openSocket.BranchOptions.Contains(bt.Template))
                {
                    _growableBranchTypes.Add(bt);
                }
            }

            /*
             * None of the available branch types fit in this socket,
             * that can happen depending on the composition of other branches in the plant
             * (if the selected socket has a "ratio" set)
             * Try again next time.
             * It's fine if _nextSocketIndex goes out of bounds,
             * that's what we check for at the beginning of the method
             */
            if (_growableBranchTypes.Count == 0)
            {
                _nextSocketIndex++;
                FailedGrowAttempts++;
                return null;
            }

            // var address = attemptGrowAtSocket.Address;
            var branchType = _growableBranchTypes[Random.Range(0, _growableBranchTypes.Count)];
            var template = branchType.Template;

            // var parent = _branches[GetParentAddress(address)];
            GetSocketPositionAndRotation(parent, _nextSocketIndex, out var socketPos, out var socketRot);

            // var parent = GetParentAndSocketPosition(address, out var socketRelPos, out var parentRelRot);

            // Try a random orientation for the child node
            var xRot = Random.Range(-template.MaxPivotAngle, template.MaxPivotAngle);
            var yRot = Random.Range(-template.MaxPivotAngle, template.MaxPivotAngle);
            var zRot = Random.Range(-template.MaxRollAngle, template.MaxRollAngle);

            var pivot = Quaternion.Euler(xRot, yRot, 0);
            var globalRot = RootTransform.rotation * socketRot * pivot;
            var globalPos = RootTransform.TransformPoint(socketPos);

            if (template.VerticalBias < 0)
            {
                var down = Quaternion.LookRotation(Vector3.down, globalRot * Vector3.forward);
                globalRot = Quaternion.SlerpUnclamped(globalRot, down, -template.VerticalBias);
            }
            else if (template.VerticalBias > 0)
            {
                var up = Quaternion.LookRotation(Vector3.up, globalRot * Vector3.back);
                globalRot = Quaternion.SlerpUnclamped(globalRot, up, template.VerticalBias);
            }

            if (template.MakeHorizontal)
            {
                globalRot = Quaternion.LookRotation(globalRot * Vector3.forward, Vector3.up);
            }

            var roll = RotateAroundZ(zRot * Mathf.Deg2Rad);
            globalRot *= roll;

            if (CheckPlacement(globalPos, globalRot, template, false, parent.GameObject))
            {
                var branch = new Branch
                {
                    Depth = parent.Depth + 1,
                    Parent = parent,
                    BranchType = branchType,
                    RelPosition = socketPos,
                    RelRotation = Quaternion.Inverse(RootTransform.rotation) * globalRot
                };

                parent.Children[_nextSocketIndex] = branch;
                if (HasOpenSockets(parent) == false)
                {
                    /*
                     * This parent had all sockets filled, remove from
                     * 'open sockets' list.
                     */
                    _nextSocketIndex = 0;
                    _branchesWithOpenSockets.Dequeue();
                }
                
                AddBranch(branch);
                if (HasOpenSockets(branch))
                {
                    _branchesWithOpenSockets.Enqueue(branch);
                }

                FailedGrowAttempts = 0;
                return branch;
            }
            _nextSocketIndex++;
            FailedGrowAttempts++;
            return null;
        }

        void SetGrowableBranchTypes()
        {
            foreach (var branchType in _branchTypes)
            {
                var template = branchType.Template;
                if (BranchCount >= template.MinTotalOtherBranches &&
                    branchType.TotalCount < template.MaxCount &&
                    (branchType.TotalCount + 1f) / (BranchCount + 1f) <= template.Quota)
                {
                    if (branchType.Growable == false)
                    {
                        branchType.Growable = true;
                        Unblock();
                    }
                }
                else
                {
                    branchType.Growable = false;
                }
            }
        }

        void Unblock()
        {
            GrowthBlocked = false;
            FailedGrowAttempts = 0;
        }

        bool HasOpenSockets(Branch newBranch)
        {
            var depthOfChildren = newBranch.Depth + 1;
            for (var i = 0; i < newBranch.BranchType.Template.Sockets.Count; i++)
            {
                if (newBranch.Children[i] == null)
                {
                    var socket = newBranch.BranchType.Template.Sockets[i];
                    foreach (var childBranchTemplate in socket.BranchOptions)
                    {
                        if (depthOfChildren >= childBranchTemplate.DepthMin &&
                            depthOfChildren <= childBranchTemplate.DepthMax)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        static bool CheckPlacement(
            Vector3 globalPos,  Quaternion globalRot, BranchTemplate template,
            bool    isExisting, GameObject ignoredParent = null)
        {
            if (CheckIfAreaClear(globalPos, globalRot, template, isExisting, ignoredParent) == false)
            {
                return false;
            }

            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (template.NeedsSurface && CheckIfTouchesSurface(globalPos, globalRot, template) == false)
            {
                return false;
            }

            return true;
        }

        static bool CheckIfAreaClear(
            Vector3 globalPos,  Quaternion globalRot, BranchTemplate template,
            bool    isExisting, GameObject ignoredParent = null)
        {
            var radius = template.Capsule.radius;
            var height = template.Capsule.height;

            var startDist = CheckCapsuleDistances(radius, height, out var endDist);

            var dir = globalRot * Vector3.forward;
            var start = globalPos + dir * startDist;
            var end = globalPos + dir * endDist;

#if UNITY_EDITOR

            if (DebugPlantGrowth || Application.isPlaying == false)
            {
                // Debug.Log($"F{Time.frameCount} Check if area clear.");
                // DebugUtils.DrawDebugCapsule(start, end, radius, new Color(0.66f, 1f, 0.56f, .2f), 1);
            }
#endif

            var occupied = template.Avoids;
            if (isExisting)
            {
                /*
                 * If checking placement of an existing plant it should not be
                 * bothered by its own presence. In other words:
                 * normally plants don't grow inside other plants, but
                 * if a branch is already placed, it should not be
                 * bothered by its own existence.
                 *
                 * The way this is implemented is per layer: so it will not
                 * be bothered by *anything* on its own layer.
                 */
                // occupied &= ~(1 << template.gameObject.layer);
                /*
                 * The above is flexible, but it breaks when the plant is on different
                 * layers (Fruit and Plant). So for now, those two layers
                 * are just hardcoded.
                 *
                 * The problem is that there is ANOTHER behavior that relies on this one.
                 * The fact that branches can overlap with their PARENT, without being
                 * removed. When checking for a NEW branch, this works through the
                 * 'ignoredParent' parameter; when checking EXISTING branches,
                 * it is the code below that provides 'immunity' from parent overlap.
                 * So if the branch and parent are on different layers, that is why
                 * the above breaks (And I had to hardcode Plants & Fruit layers here)
                 */
                // occupied &= ~((1 << Layers.Plants) | (1 << Layers.Fruit));
            }

            if (ignoredParent == null)
            {
                var isOccupied = Physics.CheckCapsule(start, end, radius, occupied);

#if UNITY_EDITOR
                if (DebugPlantGrowth && isOccupied)
                {
                    Debug.Log($"{template} failed to grow because space is occupied");
                }
#endif

                return isOccupied == false;
            }

            /*
             * If we need to check for an ignored parent, find a single collider.
             */
            var count = Physics.OverlapCapsuleNonAlloc(start, end, radius, ColliderCache, occupied);
            for (var i = 0; i < count; i++)
            {
                if (ColliderCache[i].gameObject != ignoredParent)
                {
                    // Debug.Log($"F{Time.frameCount} Overlap with {ColliderCache[i].gameObject.name}");
                    return false;
                }
            }

            return true;
        }

        static bool CheckIfTouchesSurface(Vector3 globalPos, Quaternion globalRot, BranchTemplate template)
        {
            return GetTouchingSurface(globalPos, globalRot, template) != null;
        }

        static Collider GetTouchingSurface(Vector3 globalPos, Quaternion globalRot, BranchTemplate template)
        {
            var radius = template.Capsule.radius;
            var height = template.Capsule.height;
            var dir = globalRot * Vector3.forward;

            // ReSharper disable once Unity.InefficientMultiplicationOrder
            var offset = globalRot * Vector3.down * radius;
            var start = globalPos + offset + dir * (0.5f * height);
            var end = globalPos + offset + dir * (height - radius);

            radius *= template.SurfaceDistance;

#if UNITY_EDITOR
            if (DebugPlantGrowth || Application.isPlaying == false)
            {
                // DebugUtils.DrawDebugCapsule(start, end, radius, new Color(0.38f, 0.48f, 0.91f, .2f), 2);
            }
#endif

            var count = Physics.OverlapCapsuleNonAlloc(start, end, radius, ColliderCache, template.Surface);

            return count > 0 ? ColliderCache[0] : null;
        }

        void GetSocketPositionAndRotation(Branch parent,
            int                                  socketIndex,
            out Vector3                          socketPos,
            out Quaternion                       socketRot)
        {
            if (socketIndex >= parent.BranchType.Template.Sockets.Count)
            {
                Debug.LogError(
                    $"Branch for nonexisting socket {socketIndex + 1} on {parent.BranchType.Template.name}");
                socketPos = Vector3.zero;
                socketRot = Quaternion.identity;
                return;
            }

            var socket = parent.BranchType.Template.Sockets[socketIndex];
            var socketTransform = socket.transform;
            socketPos = parent.RelPosition + parent.RelRotation * socketTransform.localPosition;
            socketRot = parent.RelRotation * socketTransform.localRotation;
        }

        #endregion

        #region MODIFICATION

        public static uint FastHash(uint a)
        {
            a = a ^ 61 ^ (a >> 16);
            a = a + (a << 3);
            a = a ^ (a >> 4);
            a = a * 0x27d4eb2d;
            a = a ^ (a >> 15);
            return a;
        }

        void AddBranch(Branch branch)
        {
            var branchVariant = Variant + _branches.Count;

            var go = branch.GameObject;
            if (go == null)
            {
                go = branch.BranchType.Template.CreateInstance(branchVariant);
                go.transform.parent = RootTransform;
            }
            else
            {
                Assert.AreEqual(go.transform.parent, RootTransform);
            }

            go.transform.localPosition = branch.RelPosition;
            go.transform.localRotation = branch.RelRotation;

            branch.GameObject = go;

            _branches.Add(branch);
            branch.BranchType.TotalCount++;
        }

        #endregion

        #region EDITOR_ACTIONS

#if UNITY_EDITOR
        GUIStyle _richTextMiniLabelStyle;

        public Plant(bool allowGrowAnimation)
        {
            AllowGrowAnimation = allowGrowAnimation;
        }

        GUIStyle RichTextMiniLabelStyle
        {
            get
            {
                if (_richTextMiniLabelStyle == null)
                {
                    _richTextMiniLabelStyle = EditorStyles.miniLabel;
                    _richTextMiniLabelStyle.richText = true;
                }

                return _richTextMiniLabelStyle;
            }
        }

        // [TitleGroup("Actions")]
        // [HorizontalGroup("Actions/Buttons")]
        // [Button]
        public void Reset()
        {
// ReSharper disable once Unity.NoNullPropagation
            Assert.IsTrue(_definition?.RootNode != null);
            /*
             * Reset to prefab mode completely
             */
            if (_branchTypes != null)
            {
                foreach (var pair in _branchTypes)
                {
                    pair.Template.Preprocess(true);
                }
            }

// ReSharper disable once Unity.NoNullPropagation
            if (_definition?.RootNode != null)
            {
                if (RootTransform == null)
                {
                    RootTransform = new GameObject("Root Transform").transform;
                    RootTransform.SetParent(transform, false);
                }

                // name = Definition.RootNode.name.Split(' ')[0];

                if (RootTransform.TryGetComponent(out MeshFilter meshFilter) == false)
                {
                    meshFilter = RootTransform.gameObject.AddComponent<MeshFilter>();
                }

                meshFilter.sharedMesh = _definition.RootNode.GetComponent<MeshFilter>().sharedMesh;

                if (RootTransform.TryGetComponent(out MeshRenderer meshRenderer) == false)
                {
                    meshRenderer = RootTransform.gameObject.AddComponent<MeshRenderer>();
                }

                meshRenderer.enabled = true;
                meshRenderer.sharedMaterials =
                    _definition.RootNode.GetComponent<MeshRenderer>().sharedMaterials;
            }

            /*
             * Clear out the nodes
             */
            for (var i = RootTransform.childCount - 1; i >= 0; i--)
            {
                var child = RootTransform.GetChild(i);
                DestroyImmediate(child.gameObject);
            }

            _branches?.Clear();
            _branchTypes?.Clear();
            Initialized = false;
            Unblock();
        }

        
        public void Grow()
        {
            if (Initialized == false)
            {
                ClearAndInitialize();
                Debug.Log($"Initialized <b>{name}</b>");
            }

            for (int i = 0; i < 10; i++)
            {
                GrowInEditor(200);
            }
        }

        /// <summary>
        ///     Grow a plant inside the editor
        /// </summary>
        /// <param name="maxAttempts"></param>
        /// <returns>whether the plant actually grew a node</returns>
        bool GrowInEditor(int maxAttempts = 20)
        {
            Assert.IsFalse(Application.isPlaying, "Can't use this button in Play mode");
            Assert.IsTrue(Initialized);

            if (GrowthBlocked)
            {
                return false;
            }

            if (Grow(maxAttempts, out _))
            {
                /*
                 * Max out energy to keep the plant growin in the editor
                 */
                Energy = _definition.MaxStoredEnergyInt;
                return true;
            }

            return false;
        }

        bool IsInEditMode => Application.isPlaying == false;

        public bool CheckIfSettled()
        {
            /*
             * This is only to be used in the editor.
             */
            Assert.IsFalse(Application.isPlaying);
            var pos = transform.position;

            if ((pos - _settledPosition).sqrMagnitude > 0.0001f)
            {
                /*
                 * Set the Settled Position regardless of whether
                 * the plant moves in this call. That means if it doesn't
                 * move, the next call to CheckIfSettled will return True;
                 */
                _settledPosition = pos;
                /*
                 * If the plant was moved, reset all grown nodes.
                 */
                if (SettleStep())
                {
                    ClearAndInitialize();
                }

                return false;
            }

            return true;
        }

        // [BoxGroup("Status/Status", false)]
        // [PropertyOrder(0)]
        // [ShowInInspector]
        // [GUIColor(nameof(StatusColor))]
        string Status =>
            Initialized
                ? AllSocketsFilled ? "Fully Grown" :
                GrowthBlocked ? "Blocked" : "Growing"
                : "Prefab";

        Color StatusColor => Initialized
            ? AllSocketsFilled ? new Color(0.38f, 1f, 0.36f) :
            GrowthBlocked ? new Color(1f, 0.39f, 0.37f) : new Color(0.73f, 1f, 0.74f)
            : new Color(0.55f, 0.66f, 1f);

        // [TitleGroup("Status")]
        // [PropertyOrder(3)]
        // [ShowIf(nameof(Initialized))]
        // [OnInspectorGUI]
        // void DrawStatus()
        // {
        //     var gray = Color.gray;
        //
        //     // var green = EditorColors.Green.Hex();
        //
        //     using (new EditorGUI.DisabledScope(true))
        //     {
        //         EditorGUILayout.IntField("Total Branches", _branches?.Count ?? 0);
        //         EditorGUILayout.IntField("Open Sockets", _branchesWithOpenSockets?.Count ?? 0);
        //     }
        //
        //     if (this != null)
        //     {
        //         foreach (var branchType in _branchTypes ?? Enumerable.Empty<BranchType>())
        //         {
        //             GUIHelper.PushColor(branchType.Growable
        //                 ? new Color(0.76f, 1f, 0.78f)
        //                 : Color.white);
        //             SirenixEditorGUI.BeginBox();
        //             using (new EditorGUILayout.HorizontalScope())
        //             {
        //                 var label = branchType.Template.name;
        //
        //                 GUILayout.Label(label, EditorStyles.miniLabel,
        //                     GUILayout.Width(EditorGUIUtility.labelWidth));
        //                 GUILayout.Label(
        //                     $"{branchType.TotalCount} <color={gray}>/ {branchType.Template.MaxCount}</color>",
        //                     RichTextMiniLabelStyle);
        //             }
        //
        //             SirenixEditorGUI.EndBox();
        //             GUIHelper.PopColor();
        //         }
        //     }
        // }
#endif

        #endregion // EDITOR
    }
}