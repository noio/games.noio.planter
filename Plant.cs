using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sirenix.OdinInspector;
using Sirenix.Utilities.Editor;
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
        public event Action<Branch> BranchGrown;
        public event Action<Plant> Destroyed;
        const uint MaxBranches = 4;
        const uint MaxDepth = 12;
        public const uint RootAddress = 1;
        static readonly int HighlightColor = Shader.PropertyToID("_HighlightColor");
        static readonly uint MaxAddress = (uint)Mathf.Pow(MaxBranches, MaxDepth + 1);
        static readonly Regex AddressRegex = new Regex(@"D\d+\.S\d+\.A(\d+)");
        static readonly Collider[] ColliderCache = new Collider[2];
        public static bool DebugPlantGrowth;

        #region PUBLIC AND SERIALIZED FIELDS

        [ReadOnly] public Transform RootTransform;
        public PlantDefinition Definition;


        #endregion

        readonly SortedList<uint, Branch> _branches = new SortedList<uint, Branch>();
        readonly HashSet<uint> _branchesToRemove = new HashSet<uint>();
        readonly Queue<(uint Address, int Mask)> _openSockets = new Queue<(uint, int)>();
        readonly Dictionary<uint, BranchTypeCache> _branchTypes = new Dictionary<uint, BranchTypeCache>();
        readonly List<Branch> _animatingBranches = new List<Branch>();
        bool _hasInvisibleOrAnimatingBranches;
        Vector3 _settledPosition;
        Vector3 _settleHitPoint;
        Vector3 _settleHitNormal;
        MeshFilter _mergedMeshFilter;
        MeshRenderer _mergedMeshRenderer;

        #region PROPERTIES

        public int Variant { get; private set; }
        public bool Initialized { get; private set; }

        [TitleGroup("Status")]
        [ShowInInspector]
        [PropertyOrder(1)]
        [ProgressBar(0, nameof(MaxStoredEnergy), 0.62f, 1f, 0.45f)]
        [ShowIf(nameof(Initialized))]
        public int Energy { get; private set; }

        

        /// <summary>
        ///     The number of fruits is subtracted from the energy cap, because
        ///     that energy is stored inside the fruit, if it were.
        /// </summary>
        public int MaxStoredEnergy => Definition.MaxStoredEnergyInt;


        public bool AllSocketsFilled => AnyOpenSocketsLeft == false;
        public bool AllowGrowAnimation { get; set; } = true;
        public bool GrowthBlocked { get; private set; }
        public int BranchCount => _branches.Count;

        /// <summary>
        ///     Is this plant considered to be "Fully Grown".
        /// </summary>
        public bool FullyGrown => AllSocketsFilled || GrowthBlocked;

        [TitleGroup("Status")]
        [ShowInInspector]
        [PropertyOrder(2)]
        [ProgressBar(-10, nameof(MaxGrowAttempts), 0.95f, 0.68f, 0.37f)]
        [ShowIf(nameof(Initialized))]
        public int FailedGrowAttempts { get; private set; }

        
        int MaxGrowAttempts => Definition.MaxGrowAttempts;
        bool AnyOpenSocketsLeft => GrowableBranchTypeMask > 0 && _openSockets.Count > 0;

        /// <summary>
        ///     A mask indicating which branch types
        ///     can currently be grown
        /// </summary>
        int GrowableBranchTypeMask
        {
            get;

            // var bits = Convert.ToString(value, 2).PadLeft(8, '0');
            // Debug.Log($"F{Time.frameCount} Open Socket Bits: {bits}");
            set;
        }

        bool UsingMergedMesh => _mergedMeshRenderer != null && _mergedMeshRenderer.enabled;

        #endregion

        #region MONOBEHAVIOUR METHODS

        /// <summary>
        ///     For some reason Awake doesn't run in ExecuteInEditMode
        ///     So I'm using OnEnable here.
        ///     OnAfterDeserialize sets the _shouldDeserialize flag
        ///     and then this code loads the actual nodes from the serialized data
        /// </summary>
        void OnEnable()
        {

            
        }
        

        #endregion
        

        public void InitWithRootNodeOnly()
        {
            DisableRootPlaceholderRenderer();
            Preprocess();

            Variant = Random.Range(0, 17);

            SetBranches(new Dictionary<uint, Branch>
            {
                {
                    RootAddress, new Branch
                    {
                        GameObject = null,
                        Template = Definition.RootNode,
                        RelRotation = Quaternion.identity
                    }
                }
            }, true);

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

        /// <summary>
        ///     Top up fruit energy, use the rest for
        ///     regular energy.
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="addFruitEnergy">
        ///     Allow this energy to be used
        ///     for FruitEnergy
        /// </param>
        public void AddEnergy(int amount)
        {
            /*
             * Only stock up fruit energy if the maximum number of fruits hasn't been reached.
             * - The maximum amount of stored fruit energy is equal to the remaining number of
             *   fruits that can be grown, times their fruit energy consumption cost.
             *   In other words: if the plant has max fruits, it will store 0 fruit energy.
             * - Use at most half of the added energy for fruits, so that the plant will still
             *   grow regular branches.
             */
            var remainingEnergyUntilFull = Mathf.Max(0, MaxStoredEnergy - Energy);
            
            /*
             * Add the rest to regular energy
             */
            Energy += Mathf.Min(amount, remainingEnergyUntilFull);
        }

        /// <summary>
        ///     Sets energy to a specific amount, ignores the cap.
        /// </summary>
        /// <param name="amount"></param>
        public void SetEnergy(int amount)
        {
            Energy = amount;
        }
        

        void Preprocess()
        {
            PreprocessBranchType(Definition.RootNode);
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
            var rootNode = Definition.RootNode;
            var layers = rootNode.Avoids ^ (1 << rootNode.gameObject.layer);
            if (Physics.SphereCast(rayOrigin, rootNode.Capsule.radius, rayDirection, out var hitInfo, 5,
                layers))
            {
                _settleHitPoint = hitInfo.point;
                _settleHitNormal = hitInfo.normal;

//                    Debug.Log($"Sphere cast hit at {_hitPoint}");
                transform.position = Vector3.Lerp(transform.position, hitInfo.point, lerpAmount);
                transform.rotation = Quaternion.Lerp(transform.rotation,
                    Quaternion.LookRotation(hitInfo.normal, -transform.forward) * Quaternion.Euler(90, 0, 0),
                    lerpAmount);

                return true;
            }

            return false;
        }

        void UpdateGrowAnimation(float deltaTime)
        {
            const int maxAnimatingBranches = 10;
            if (_animatingBranches.Count < maxAnimatingBranches && _hasInvisibleOrAnimatingBranches)
            {
                var allBranchesFullyGrown = true;
                foreach (var pair in _branches)
                {
                    var branch = pair.Value;
                    if (branch.GrowProgress <= 0)
                    {
                        var address = pair.Key;
                        allBranchesFullyGrown = false;
                        if (address == RootAddress || _branches[GetParentAddress(address)].GrowProgress >= 1)
                        {
                            _animatingBranches.Add(branch);
                            
                            if (_animatingBranches.Count >= maxAnimatingBranches)
                            {
                                break;
                            }
                        }
                    }
                }

                // If we reached the end of the iteration above, then there are no more invisible nodes,
                // So the variable is set to false
                if (_animatingBranches.Count == 0 && allBranchesFullyGrown)
                {
                    _hasInvisibleOrAnimatingBranches = false;
                }
            }

            for (var i = _animatingBranches.Count - 1; i >= 0; i--)
            {
                var branch = _animatingBranches[i];
                /*
                 * introduce some random grow time based on the place in the array
                 */
                branch.GrowProgress += deltaTime / (branch.Template.GrowTime * 1 + i * .2f);
                if (branch.GrowProgress >= 1)
                {
                    _animatingBranches.RemoveAt(i);
                }
            }
        }

        void ShowAllBranchesImmediately()
        {
            foreach (var branch in _branches)
            {
                branch.Value.GrowProgress = 1;
            }

            _hasInvisibleOrAnimatingBranches = false;
        }

        static string BranchName(string baseName, uint address, int variant)
        {
            return
                $"{baseName} D{GetBranchDepth(address)}.S{GetSocketIndex(address)}.A{address.ToString()}.V{variant}";
        }

        static uint AddressFromName(string name)
        {
            var match = AddressRegex.Match(name);
            if (match.Success)
            {
//                Debug.Log($"{match.Groups[1]}");
                if (uint.TryParse(match.Groups[1].Value, out var address))
                {
                    return address;
                }
            }

            return 0;
        }

        void PreprocessBranchType(BranchTemplate template)
        {
            // Prevent infinite recursion.
            if (_branchTypes.ContainsKey(template.DatabaseId))
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

            var cache = new BranchTypeCache(template, 1 << _branchTypes.Count);

            _branchTypes.Add(template.DatabaseId, cache);


            foreach (var socket in template.Sockets)
            {
                foreach (var branchOption in socket.BranchOptions)
                {
                    PreprocessBranchType(branchOption);
                }
            }
        }

        #region GROWING

        public class Branch
        {
            #region PUBLIC AND SERIALIZED FIELDS

            public BranchTemplate Template;
            public GameObject GameObject;
            public MeshRenderer Renderer;

            /*
             * Position and rotation of the node relative
             * to the ROOT of the plant
             * (not to the parent node!)
             */
            public Vector3 RelPosition;
            public Quaternion RelRotation;

            #endregion

            float _growProgress;

            #region PROPERTIES

            public float GrowProgress
            {
                get => _growProgress;
                set
                {
                    float scale;
                    if (value >= 1)
                    {
                        _growProgress = 1;
                        scale = 1;
                        Renderer.enabled = true;
                    }
                    else if (value > 0)
                    {
                        _growProgress = value;
                        scale = Mathf.Lerp(0.1f, 1, value);
                        Renderer.enabled = true;
                    }
                    else
                    {
                        _growProgress = 0;
                        scale = 1;
                        Renderer.enabled = false;
                    }

                    Vector3 s;
                    s.x = scale;
                    s.y = scale;
                    s.z = scale;
                    GameObject.transform.localScale = s;

                    var capsule = GameObject.GetComponent<CapsuleCollider>();
                    /*
                     * Inverse scale the capsule to make sure the branch is still
                     * taking up space at its final size.
                     * Maybe not the most efficient but better than
                     * creating a proxy game object just with a collider?
                     */
                    var height = Template.Capsule.height / scale;
                    capsule.radius = Template.Capsule.radius / scale;
                    capsule.height = height;
                    capsule.center = new Vector3(0, 0, capsule.height * .5f);
                }
            }

            #endregion
        }

        class BranchTypeCache
        {
            #region PUBLIC AND SERIALIZED FIELDS

            public readonly BranchTemplate Template;
            public readonly int BitMask;

            #endregion

            public BranchTypeCache(BranchTemplate template, int bitMask)
            {
                Template = template;
                BitMask = bitMask;

                // var mask = Convert.ToString(BitMask, 2).PadLeft(8, '0');
                // Debug.Log($"Create BranchTypeCache for {Template.name}. Mask: {mask}");
            }

            #region PROPERTIES

            public int TotalCount { get; set; }
            public string Name => Template.name;

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
        ///     Checks only for clear space, assuming that the seed already
        ///     collided with a surface that the plant could grown on.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        /// <param name="layerOfCollidedSurface"></param>
        /// <returns></returns>
        public bool CheckNewPlantPlacement(Vector3 position, Quaternion rotation, int layerOfCollidedSurface)
        {
            /*
             * This should only be run on the prefab object
             */
            Assert.IsTrue(gameObject.scene == default, "Don't call on instantiated object");

            var rootNode = Definition.RootNode;
            if (rootNode.NeedsSurface && (rootNode.Surface & (1 << layerOfCollidedSurface)) == 0)
            {
                return false;
            }

            position += rotation * RootTransform.localPosition;
            rotation *= RootTransform.localRotation;
            var clear = CheckIfAreaClear(position, rotation, rootNode, false);
            return clear;
        }

        /// <summary>
        ///     Attempt growing and update the animations
        /// </summary>
        /// <param name="maxGrowAttempts"></param>
        /// <returns></returns>
        public void GrowAndUpdateAnimation(int maxGrowAttempts)
        {
            Assert.IsTrue(Initialized);

            if (_hasInvisibleOrAnimatingBranches)
            {
                if (AllowGrowAnimation)
                {
                    UpdateGrowAnimation(Time.deltaTime);
                }
            }

            // else
            // {
            var attempts = Mathf.Min(MaxGrowAttempts, maxGrowAttempts);

            if (FullyGrown == false && Energy >= Definition.EnergyPerBranchInt)
            {
                Grow(attempts, out _);
            }

            if (_hasInvisibleOrAnimatingBranches == false &&
                FullyGrown &&
                UsingMergedMesh == false)
            {
                // CreateOrUpdateMergedMesh();
            }
        }
        

        /// <summary>
        /// Opposite of <see cref="CreateOrUpdateMergedMesh"/>, switches this plant
        /// back to use individual meshes for each branch.
        /// </summary>
        void SwitchToIndividualBranchMeshes()
        {
            if (UsingMergedMesh)
            {
                _mergedMeshRenderer.enabled = false;
                DestroyImmediate(_mergedMeshFilter.sharedMesh, true);
                _mergedMeshFilter.sharedMesh = null;

                foreach (var kvp in _branches)
                {
                    kvp.Value.Renderer.enabled = true;
                }
            }
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
                    Energy -= Definition.EnergyPerBranchInt;

                    /*
                     * When adding a branch, there might
                     * now be other branches that are growable
                     */
                    SetGrowableBranchTypes();

                    BranchGrown?.Invoke(branch);
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
            return (int) ((((i + (i >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24);
        }


        Branch FindBranchToGrow()
        {
            /*
             * What does this need to do:
             *
             * - Get next open socket from the Queue
             * - Get random (available!) branch type
             * - IF FAIL: RE ENQUEUE OPEN SOCKET
             *
             */
            if (_openSockets.Count == 0)
            {
                FailedGrowAttempts++;
                return null;
            }

            var attemptGrowAtSocket = _openSockets.Dequeue();

            var growableBranchTypes = attemptGrowAtSocket.Mask & GrowableBranchTypeMask;

            /*
             * None of the available branch types fit in this socket.
             * Try again next time.
             */
            if (growableBranchTypes == 0)
            {
                _openSockets.Enqueue(attemptGrowAtSocket);
                FailedGrowAttempts++;
                return null;
            }

            var address = attemptGrowAtSocket.Address;
            var branchType = SelectRandomBranchType(growableBranchTypes);
            var template = branchType.Template;

            var parent = GetParentAndSocketPosition(address, out var socketRelPos, out var parentRelRot);

            // Try a random orientation for the child node
            var xRot = Random.Range(-template.MaxPivotAngle, template.MaxPivotAngle);
            var yRot = Random.Range(-template.MaxPivotAngle, template.MaxPivotAngle);
            var zRot = Random.Range(-template.MaxRollAngle, template.MaxRollAngle);

            var pivot = Quaternion.Euler(xRot, yRot, 0);
            var globalRot = RootTransform.rotation * parentRelRot * pivot;
            var globalPos = RootTransform.TransformPoint(socketRelPos);

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
//                    var localRot = Quaternion.Inverse(parentRot) * globalRot;
//                    var schemaNode = new Schema.PlantNode(template._id, localRot.ToSchemaRotation());

                var branch = new Branch
                {
                    Template = template,
                    RelPosition = socketRelPos,
                    RelRotation = Quaternion.Inverse(RootTransform.rotation) * globalRot
                };

                AddBranch(address, branch);
                AddOpenSocketsFor(address, branch);

                FailedGrowAttempts = 0;
                return branch;
            }

            _openSockets.Enqueue(attemptGrowAtSocket);
            FailedGrowAttempts++;
            return null;
        }

        /// <summary>
        ///     The passed in mask indicates which branch types should
        ///     be selected from. E.g.  0b00011010  means that
        ///     the 2nd, 4th and 5th entries in the _branchTypes list
        ///     are eligible.
        /// </summary>
        /// <param name="combinedMask"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        BranchTypeCache SelectRandomBranchType(int combinedMask)
        {
            /*
             * We select a random number between 0 and
             * the number of set bits, then iterate over the branch
             * types and return the corresponding entry.
             * It's a bit messed up.
             */
            var selectedBranchTypeIndex = Random.Range(0, NumberOfSetBits((uint)combinedMask));
            foreach (var branchType in _branchTypes)
            {
                if ((branchType.Value.BitMask & combinedMask) != 0)
                {
                    if (selectedBranchTypeIndex == 0)
                    {
                        return branchType.Value;
                    }

                    selectedBranchTypeIndex--;
                }
            }

            throw new Exception("Did not find branch type");
        }

        void SetGrowableBranchTypes()
        {
            foreach (var pair in _branchTypes)
            {
                var branchType = pair.Value;
                var template = branchType.Template;
                if (BranchCount >= template.MinTotalOtherBranches &&
                    branchType.TotalCount < template.MaxCount &&
                    (branchType.TotalCount + 1f) / (BranchCount + 1f) <= template.Quota)
                {
                    // Debug.Log($"F{Time.frameCount} CheckGrowable ({branch.Name}): true");
                    if ((GrowableBranchTypeMask & branchType.BitMask) == 0)
                    {
                        Unblock();
                        GrowableBranchTypeMask |= branchType.BitMask;
                    }
                }
                else
                {
                    // Debug.Log($"F{Time.frameCount} CheckGrowable ({branch.Name}): false");
                    GrowableBranchTypeMask &= ~branchType.BitMask;
                }
            }
        }

        void Unblock()
        {
            GrowthBlocked = false;
            FailedGrowAttempts = 0;
            SwitchToIndividualBranchMeshes();
        }

        void AddOpenSocketsFor(uint address, Branch branch, bool checkForExisting = false)
        {
            var depthOfChildren = GetBranchDepth(address) + 1;
            for (var i = 0; i < branch.Template.Sockets.Count; i++)
            {
                var socket = branch.Template.Sockets[i];
                var childAddress = GetBranchAddress(address, i);
                if (checkForExisting == false || _branches.ContainsKey(childAddress) == false)
                {
                    var mask = 0;
                    foreach (var childBranchTemplate in socket.BranchOptions)
                    {
                        // Debug.Log(
                        // $"F{Time.frameCount} Adding sockets for {branch.Template.name} at {childAddress} ({childBranchTemplate.name})");
                        if (childBranchTemplate.DepthMin <= depthOfChildren &&
                            childBranchTemplate.DepthMax >= depthOfChildren)
                        {
                            mask |= _branchTypes[childBranchTemplate.DatabaseId].BitMask;
                        }
                    }

                    if (mask != 0)
                    {
                        _openSockets.Enqueue((childAddress, mask));
                    }
                }
            }
        }

        void ProcessRemovals(bool refundEnergy)
        {
            if (_branchesToRemove.Count > 0)
            {
                if (refundEnergy)
                {
                    Energy += Definition.EnergyPerBranchInt * _branchesToRemove.Count;
                }

                // Delete actual nodes and corresponding gameobjects
                foreach (var deleteAddress in _branchesToRemove)
                {
                    var branch = _branches[deleteAddress];

                    _branches.Remove(deleteAddress);
                    _branchTypes[branch.Template.DatabaseId].TotalCount--;
                    Unblock();

                    if (Application.isPlaying)
                    {
                        Destroy(branch.GameObject);
                    }
                    else
                    {
                        DestroyImmediate(branch.GameObject);
                    }

                    _animatingBranches.Remove(branch);
                }

                _branchesToRemove.Clear();
                FailedGrowAttempts = 0;
                RecalculateOpenSockets();
                SetGrowableBranchTypes();
            }

            /*
             * The entire plant was destroyed.
             */
            if (_branches.Count == 0)
            {
#if UNITY_EDITOR
                if (DebugPlantGrowth)
                {
                    Debug.Log($"F{Time.frameCount} no branches remaining. Destroying {name}");
                }
#endif
                Destroyed?.Invoke(this);
                Destroy(gameObject);
            }

            // var remaining = string.Join(", ", _branches.Keys);
            // Debug.Log($"Remaining branches: {remaining}");
            // var openSockets = string.Join("\n", BranchTypeCaches.Select(cache =>
            // cache.Value.Template.name + ": " + string.Join(", ", cache.Value.OpenSockets)));
            // Debug.Log($"Open Sockets: \n {openSockets}");
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

        Branch GetParentAndSocketPosition(uint address, out Vector3 relPos, out Quaternion relRot)
        {
            if (address != RootAddress)
            {
                var parentAddress = GetParentAddress(address);

                if (_branches.TryGetValue(parentAddress, out var parent) == false)
                {
                    Debug.LogError(
                        $"Tried to find parent for {address} at {parentAddress}, but it does not exist.");
                    relPos = Vector3.zero;
                    relRot = Quaternion.identity;
                    return null;
                }

                var socketIndex = GetSocketIndex(address);
                if (socketIndex >= parent.Template.Sockets.Count)
                {
                    Debug.LogError(
                        $"Branch for nonexisting socket {socketIndex + 1} on {parent.Template.name}");
                    relPos = Vector3.zero;
                    relRot = Quaternion.identity;
                    return parent;
                }

                var socket = parent.Template.Sockets[socketIndex];
                var socketTransform = socket.transform;
                relPos = parent.RelPosition + parent.RelRotation * socketTransform.localPosition;
                relRot = parent.RelRotation * socketTransform.localRotation;
                return parent;
            }

            relPos = Vector3.zero;
            relRot = Quaternion.identity;
            return null;
        }

        #endregion

        #region VISUAL

        public void ResetGrowAnimationProgress()
        {
            foreach (var branch in _branches)
            {
                _hasInvisibleOrAnimatingBranches = true;
                branch.Value.GrowProgress = 0;
            }

            _animatingBranches.Clear();
        }


        #endregion

        #region MODIFICATION

        void RemoveBranch(uint address, bool refundEnergy)
        {
            if (_branches.TryGetValue(address, out var branch))
            {
                RemoveBranch(address, branch, refundEnergy);
            }
        }

        void RemoveBranch(uint address, Branch branch, bool refundEnergy)
        {
            _branchesToRemove.Add(address);
            /*
             * If the branch to remove does not have sockets,
             * it cannot have child branches, so we do not need
             * to check for other branches that should be removed
             * as a consequence, and we can go on directly to
             * ProcessRemovals()
             */
            if (branch.Template.Sockets.Count == 0)
            {
                ProcessRemovals(refundEnergy);
            }
            else
            {
                CheckBranchRemovals(false, refundEnergy);
            }
        }

        public void RemoveBranch(GameObject branchGameObject, bool refundEnergy)
        {
            if (TryFindBranch(branchGameObject, out var address, out var branch))
            {
                RemoveBranch(address, branch, refundEnergy);
            }
        }

        bool TryFindBranch(GameObject branchGameObject, out uint address, out Branch branch)
        {
            foreach (var pair in _branches)
            {
                branch = pair.Value;
                if (branch.GameObject == branchGameObject)
                {
                    address = pair.Key;
                    return true;
                }
            }

            branch = null;
            address = 0;
            return false;
        }
        
        public static uint FastHash(uint a)
        {
            a = a ^ 61 ^ (a >> 16);
            a = a + (a << 3);
            a = a ^ (a >> 4);
            a = a * 0x27d4eb2d;
            a = a ^ (a >> 15);
            return a;
        }


        void AddBranch(uint address, Branch branch)
        {
            var branchVariant = (int)(FastHash((uint)Variant + address) % 1000);

            var go = branch.GameObject;
            if (go == null)
            {
                go = branch.Template.Make(branchVariant);
                go.transform.parent = RootTransform;
                go.name = BranchName(go.name, address, branchVariant);
            }
            else
            {
                Assert.AreEqual(go.transform.parent, RootTransform);
            }

            go.transform.localPosition = branch.RelPosition;
            go.transform.localRotation = branch.RelRotation;

            branch.GameObject = go;
            branch.Renderer = branch.GameObject.GetComponent<MeshRenderer>();

            Assert.IsTrue(address <= MaxAddress,
                $"Address ({address}) for {go.name} is greater than MAX_ADDRESS: {MaxAddress}");

            _branches.Add(address, branch);
            _branchTypes[branch.Template.DatabaseId].TotalCount++;

            branch.GrowProgress = 0;
            _hasInvisibleOrAnimatingBranches = true;
        }

        /// <summary>
        ///     If a node has been added to _branchesToRemove
        ///     This method will check if that branch has any children
        ///     and will then also delete those children
        /// </summary>
        /// <param name="checkOverlap">
        ///     Should this method also check
        ///     all existing branches for overlap with objects (and remove them
        ///     if they do overlap with something)
        /// </param>
        /// <param name="refundEnergy"></param>
        void CheckBranchRemovals(bool checkOverlap, bool refundEnergy)
        {
            // Iterates over nodes, a SortedList, which is automatically breadth-first
            // because of the addressing system
            foreach (var entry in _branches)
            {
                var address = entry.Key;
                var branch = entry.Value;
                if (_branchesToRemove.Contains(address) == false)
                {
                    if (_branchesToRemove.Contains(GetParentAddress(address)))
                    {
                        // Debug.Log($"Mark {address} for removal because parent is being removed");
                        _branchesToRemove.Add(address);
                    }
                    else if (checkOverlap && branch.Template.RemoveIfOverlaps)
                    {
                        var globalPos = RootTransform.TransformPoint(branch.RelPosition);
                        var globalRot = RootTransform.rotation * branch.RelRotation;
                        if (address == RootAddress)
                        {
                            /*
                             * For the root, only check if the area is clear,
                             * don't call `CheckPlacement` which also checks for surfaces to grow on.
                             * Because the root is explicitly positioned when growing the plant,
                             * it might not actually touch a surface.
                             *
                             * Whether or not the plant is 'grounded' at the origin
                             * position is determined in ClearObstructedBranches
                             */
                            if (CheckIfAreaClear(globalPos, globalRot, branch.Template, true))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (CheckPlacement(globalPos, globalRot, branch.Template, true))
                            {
                                continue;
                            }
                        }

#if UNITY_EDITOR
                        if (DebugPlantGrowth)
                        {
                            Debug.Log($"F{Time.frameCount} removing {address} from {name} because overlap");
                        }
#endif
                        /*
                         * If we didn't "Continue" above. That means here we
                         * are removing.
                         */
                        _branchesToRemove.Add(address);
                    }
                }
            }

            ProcessRemovals(refundEnergy);
        }

        void SetBranches(IReadOnlyDictionary<uint, Branch> newBranches, bool reconnectGameObjects)
        {
            Dictionary<uint, GameObject> existingChildren = null;

            if (reconnectGameObjects == false)
            {
                // If not reconnecting children, the gameobject must be empty
                Assert.AreEqual(RootTransform.childCount, 0);
            }
            else
            {
                /*
                 * Build a dictionary of child node gameobjects
                 * by address
                 */
                existingChildren = new Dictionary<uint, GameObject>();
                foreach (Transform child in RootTransform)
                {
//                    Debug.Log($"Found child {child}");
                    existingChildren.Add(AddressFromName(child.name), child.gameObject);
                }
            }

            var toAdd = new List<KeyValuePair<uint, Branch>>();
            foreach (var pair in newBranches)
            {
                var address = pair.Key;
                if (_branches.TryGetValue(address, out var branch) == false)
                {
                    branch = pair.Value;
                    toAdd.Add(pair);
                }

                if (reconnectGameObjects)
                {
                    if (existingChildren.TryGetValue(address, out var go))
                    {
                        existingChildren.Remove(address);
                        branch.GameObject = go;
                    }
                }
            }

            // No need to loop twice (for deletions) if the node counts add up
            var numberOfBranchesToDelete = _branches.Count + toAdd.Count - newBranches.Count;
            if (numberOfBranchesToDelete > 0)
            {
                _branchesToRemove.Clear();
                foreach (var key in _branches.Keys)
                {
                    if (newBranches.ContainsKey(key) == false)
                    {
                        _branchesToRemove.Add(key);
                    }
                }

                ProcessRemovals(false);
            }

            /*
             * ExistingChildren contains the child nodes that will not
             * be reused. So they will be destroyed (below). This should
             * be equal to the amount of nodes that are deleted, otherwise
             * there are some stray GameObjects inside the RootTransform
             */
#if UNITY_EDITOR
            if (existingChildren.Count != numberOfBranchesToDelete)
            {
                WarnAboutStrayChildBranches(existingChildren);
            }
#endif

            foreach (var newBranch in toAdd)
            {
                var address = newBranch.Key;
                var branch = newBranch.Value;
                GetParentAndSocketPosition(address, out var pos, out _);

                // New nodes will not have positions set yet, Set the position cache value here
                // So set it here.
                branch.RelPosition = pos;
                AddBranch(address, branch);
            }

            /*
             * Cleanup all child gameobjects that were for some reason not
             * reconnected.
             */
            if (reconnectGameObjects)
            {
                foreach (var child in existingChildren)
                {
//                    Debug.LogWarning($"Destroying non-reconnected child {child.Key} {child}");
                    DestroyImmediate(child.Value.gameObject);
                }
            }

            /*
             * Need to completely rebuild the OpenSockets Queue.
             */
            RecalculateOpenSockets();
            SetGrowableBranchTypes();
        }

        /// <summary>
        ///     Completely rebuild the queue of open sockets from scratch.
        /// </summary>
        void RecalculateOpenSockets()
        {
            _openSockets.Clear();
            foreach (var branch in _branches)
            {
                AddOpenSocketsFor(branch.Key, branch.Value, true);
            }
        }

        #endregion
        

        #region EDITOR_ACTIONS

#if UNITY_EDITOR
        GUIStyle _richTextMiniLabelStyle;

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

        static void WarnAboutStrayChildBranches(Dictionary<uint, GameObject> existingChildren)
        {
            foreach (var child in existingChildren)
            {
                Debug.LogWarning($"Destroying non-reconnected child {child.Key} {child}");
            }
        }

        [TitleGroup("Actions")]
        [HorizontalGroup("Actions/Buttons")]
        [Button]
        public void Reset()
        {
// ReSharper disable once Unity.NoNullPropagation
            Assert.IsTrue(Definition?.RootNode != null);
            if (Application.isPlaying)
            {
                /*
                 * Just clear branches and restore energy
                 */
                for (var childIndex = 0; childIndex < MaxBranches; childIndex++)
                {
                    var address = GetBranchAddress(RootAddress, childIndex);
                    if (_branches.ContainsKey(address))
                    {
                        _branchesToRemove.Add(address);
                    }
                }

                CheckBranchRemovals(false, false);
            }
            else
            {
                /*
                 * Reset to prefab mode completely
                 */
                foreach (var pair in _branchTypes)
                {
                    pair.Value.Template.Preprocess(true);
                }

// ReSharper disable once Unity.NoNullPropagation
                if (Definition?.RootNode != null)
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

                    meshFilter.sharedMesh = Definition.RootNode.GetComponent<MeshFilter>().sharedMesh;

                    if (RootTransform.TryGetComponent(out MeshRenderer meshRenderer) == false)
                    {
                        meshRenderer = RootTransform.gameObject.AddComponent<MeshRenderer>();
                    }

                    meshRenderer.enabled = true;
                    meshRenderer.sharedMaterials =
                        Definition.RootNode.GetComponent<MeshRenderer>().sharedMaterials;
                }

/*
 * Clear out the nodes
 */
                for (var i = RootTransform.childCount - 1; i >= 0; i--)
                {
                    var child = RootTransform.GetChild(i);
                    DestroyImmediate(child.gameObject);
                }

                _branches.Clear();
                _branchTypes.Clear();
                Initialized = false;
                Unblock();
            }
        }

        [TitleGroup("Actions")]
        [HorizontalGroup("Actions/Buttons")]
        [Button("Grow")]
        [EnableIf(nameof(IsInEditMode))]
        public void ButtonGrow()
        {
            if (Initialized == false)
            {
                InitWithRootNodeOnly();
                Debug.Log($"Initialized <b>{name}</b>");
                /*
                 * Root node is added, but hidden.
                 */
                ShowAllBranchesImmediately();
            }

            GrowInEditor(200);
        }

        /// <summary>
        ///     Grow a plant inside the editor
        /// </summary>
        /// <param name="maxAttempts"></param>
        /// <returns>whether the plant actually grew a node</returns>
        public bool GrowInEditor(int maxAttempts = 20)
        {
            Assert.IsFalse(Application.isPlaying, "Can't use this button in Play mode");
            Assert.IsTrue(Initialized);

            if (GrowthBlocked)
            {
                return false;
            }

            if (Grow(maxAttempts, out _))
            {
                ShowAllBranchesImmediately();

                /*
                 * Max out energy to keep the plant growin in the editor
                 */
                Energy = Definition.MaxStoredEnergyInt;
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
                    InitWithRootNodeOnly();
                }

                return false;
            }

            return true;
        }


        [BoxGroup("Status/Status", false)]
        [PropertyOrder(0)]
        [ShowInInspector]
        [GUIColor(nameof(StatusColor))]
        string Status => Initialized
            ? AllSocketsFilled ? "Fully Grown" :
            GrowthBlocked ? "Blocked" : "Growing"
            : "Prefab";

        Color StatusColor => Initialized
            ? AllSocketsFilled ? new Color(0.38f, 1f, 0.36f) :
            GrowthBlocked ? new Color(1f, 0.39f, 0.37f) : new Color(0.73f, 1f, 0.74f)
            : new Color(0.55f, 0.66f, 1f);

        [TitleGroup("Status")]
        [PropertyOrder(3)]
        [ShowIf(nameof(Initialized))]
        [OnInspectorGUI]
        void DrawStatus()
        {
            var gray = Color.gray;

            // var green = EditorColors.Green.Hex();

            using (new EditorGUI.DisabledScope(true))
            {

                EditorGUILayout.IntField("Total Branches", _branches.Count);
                EditorGUILayout.IntField("Open Sockets", _openSockets.Count);
            }

            if (this != null)
            {
                foreach (var pair in _branchTypes)
                {
                    var branchType = pair.Value;
                    GUIHelper.PushColor((branchType.BitMask & GrowableBranchTypeMask) > 0
                        ? new Color(0.76f, 1f, 0.78f)
                        : Color.white);
                    SirenixEditorGUI.BeginBox();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var label = branchType.Template.name;

                        GUILayout.Label(label, EditorStyles.miniLabel,
                            GUILayout.Width(EditorGUIUtility.labelWidth));
                        GUILayout.Label(
                            $"{branchType.TotalCount} <color={gray}>/ {branchType.Template.MaxCount}</color>",
                            RichTextMiniLabelStyle);
                    }

                    SirenixEditorGUI.EndBox();
                    GUIHelper.PopColor();
                }
            }
        }

#endif

        #endregion // EDITOR
    }
}