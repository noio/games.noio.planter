using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace games.noio.planter
{
    [ExecuteInEditMode]
    public class Plant : MonoBehaviour
    {
        static readonly Collider[] ColliderCache = new Collider[2];

        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] Transform _rootTransform;
        [SerializeField] PlantSpecies _species;

        #endregion

        List<Branch> _branches;
        Queue<Branch> _branchesWithOpenSockets;
        List<BranchType> _branchTypes;
        List<BranchType> _growableBranchTypes;
        int _nextSocketIndex;
        Vector3 _settledPosition;

        #region PROPERTIES

        public int Variant { get; private set; }
        public bool Initialized { get; private set; }
        public bool AllSocketsFilled => AnyOpenSocketsLeft == false;
        public bool GrowthBlocked { get; private set; }
        public int BranchCount => _branches.Count;

        /// <summary>
        ///     Is this plant considered to be "Fully Grown".
        /// </summary>
        public bool FullyGrown => AllSocketsFilled || GrowthBlocked;

        public int FailedGrowAttempts { get; private set; }

        bool AnyOpenSocketsLeft => (_branchTypes?.Any(bt => bt.Growable) ?? false) &&
                                   _branchesWithOpenSockets?.Count > 0;

        #endregion

        /// <summary>
        /// Always called before growing to see if the caches are there,
        /// if not, rebuild them from the game object hierarchy.
        /// Also checks if a rootnode and a species is set of course
        /// </summary>
        public void CheckRebuildCache()
        {
        }

        public void ClearAndInitialize()
        {
            DisableRootPlaceholderRenderer();

            _branchTypes = new List<BranchType>();
            _branches = new List<Branch>();

            _branchesWithOpenSockets = new Queue<Branch>();
            _growableBranchTypes = new List<BranchType>();

            PreprocessBranchType(_species.RootBranch);

            Variant = Random.Range(0, 17);

            /*
             * Clear out the nodes
             */
            for (var i = _rootTransform.childCount - 1; i >= 0; i--)
            {
                var child = _rootTransform.GetChild(i);
                DestroyImmediate(child.gameObject);
            }

            AddBranch(null, 0, _branchTypes[0], Vector3.zero, Quaternion.identity);

            /*
             * Need to completely rebuild the OpenSockets Queue.
             */
            _branchesWithOpenSockets.Enqueue(_branches[0]);
            SetGrowableBranchTypes();

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
            if (_rootTransform.TryGetComponent<MeshRenderer>(out var meshRenderer))
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
            var rootNode = _species.RootBranch;
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

            var branchType = new BranchType(template);

            _branchTypes.Add(branchType);

            foreach (var socket in template.Sockets)
            {
                foreach (var branchOption in socket.BranchOptions)
                {
                    PreprocessBranchType(branchOption);
                }
            }
        }

        class BranchType
        {
            #region PUBLIC AND SERIALIZED FIELDS

            public readonly BranchTemplate Template;

            #endregion

            public BranchType(BranchTemplate template)
            {
                Template = template;
            }

            #region PROPERTIES

            public int TotalCount { get; set; }
            public bool Growable { get; set; }

            #endregion
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

        const int MaxGrowAttempts = 5000;

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
                if (_nextSocketIndex < parent.Template.Sockets.Count)
                {
                    if (parent.Children[_nextSocketIndex] == null)
                    {
                        openSocket = parent.Template.Sockets[_nextSocketIndex];
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
            GetSocketPositionAndRotation(parent, _nextSocketIndex,
                out var socketLocalPos, out var socketLocalRot);

            // var parent = GetParentAndSocketPosition(address, out var socketRelPos, out var parentRelRot);

            // Try a random orientation for the child node
            var xRot = Random.Range(-template.MaxPivotAngle, template.MaxPivotAngle);
            var yRot = Random.Range(-template.MaxPivotAngle, template.MaxPivotAngle);
            var zRot = Random.Range(-template.MaxRollAngle, template.MaxRollAngle);

            /*
             * We do all rotation operations on a global rotation here,
             * because we want to manipulate it in world space.
             * yaw/pivot/roll are local, but the VerticalBias represents
             * gravity which is a world space transform.
             * When we create a branch we transform it back to a local
             * rotation (relative to the parent socket)
             */
            var pivot = Quaternion.Euler(xRot, yRot, 0);
            var globalRot = parent.transform.rotation * socketLocalRot * pivot;
            var globalPos = parent.transform.TransformPoint(socketLocalPos);

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

            if (CheckPlacement(globalPos, globalRot, template, parent.gameObject))
            {
                var branch = AddBranch(parent, _nextSocketIndex, branchType,
                    socketLocalPos, Quaternion.Inverse(parent.transform.rotation) * globalRot);

                if (HasOpenSockets(branch))
                {
                    _branchesWithOpenSockets.Enqueue(branch);
                }

                if (HasOpenSockets(parent) == false)
                {
                    /*
                     * This parent had all sockets filled, remove from
                     * 'open sockets' list.
                     */
                    _nextSocketIndex = 0;
                    _branchesWithOpenSockets.Dequeue();
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
            for (var i = 0; i < newBranch.Template.Sockets.Count; i++)
            {
                if (newBranch.Children[i] == null)
                {
                    var socket = newBranch.Template.Sockets[i];
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
            Vector3 globalPos, Quaternion globalRot, BranchTemplate template, GameObject ignoredParent = null)
        {
            if (CheckIfAreaClear(globalPos, globalRot, template, ignoredParent) == false)
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
            Vector3 globalPos, Quaternion globalRot, BranchTemplate template, GameObject ignoredParent = null)
        {
            var radius = template.Capsule.radius;
            var height = template.Capsule.height;

            var startDist = CheckCapsuleDistances(radius, height, out var endDist);

            var dir = globalRot * Vector3.forward;
            var start = globalPos + dir * startDist;
            var end = globalPos + dir * endDist;

            var occupied = template.Avoids;

            if (ignoredParent == null)
            {
                var isOccupied = Physics.CheckCapsule(start, end, radius, occupied);
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

            var count = Physics.OverlapCapsuleNonAlloc(start, end, radius, ColliderCache, template.Surface);

            return count > 0 ? ColliderCache[0] : null;
        }

        void GetSocketPositionAndRotation(
            Branch         parent,
            int            socketIndex,
            out Vector3    socketLocalPos,
            out Quaternion socketLocalRot)
        {
            if (socketIndex >= parent.Template.Sockets.Count)
            {
                Debug.LogError(
                    $"Branch for nonexisting socket {socketIndex + 1} on {parent.Template.name}");
                socketLocalPos = Vector3.zero;
                socketLocalRot = Quaternion.identity;
                return;
            }

            var socket = parent.Template.Sockets[socketIndex];
            var socketTransform = socket.transform;
            socketLocalPos = socketTransform.localPosition;
            socketLocalRot = socketTransform.localRotation;
        }

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

        Branch AddBranch(
            Branch     parent,
            int        socketIndex,
            BranchType branchType,
            Vector3    localPosition,
            Quaternion localRotation)
        {
            var branchVariant = Variant + _branches.Count;

            var branch = branchType.Template.CreateBranch(branchVariant);
            var branchTransform = branch.transform;

            if (parent != null)
            {
                parent.Children[socketIndex] = branch;
                branch.transform.SetParent(parent.transform, false);
                branch.Depth = parent.Depth + 1;
            }
            else
            {
                branchTransform.SetParent(_rootTransform, false);
                ;
            }

            branchTransform.SetLocalPositionAndRotation(localPosition, localRotation);

            branch.Template = branchType.Template;

            _branches.Add(branch);
            branchType.TotalCount++;
            return branch;
        }

        #endregion


        /// <summary>
        /// Checks if a Species is set, and if a RootNode is
        /// created & set. Will create a rootnode if none is set.
        /// </summary>
        void CheckSetup()
        {
            if (_species == null)
            {
                throw new Exception($"Create and set a {nameof(PlantSpecies)} on Plant \"{name}\"");
            }

            if (_species.RootBranch == null)
            {
                throw new Exception(
                    $"Create and set a RootNode ({nameof(BranchTemplate)}) on Species \"{_species}\"");
            }
        }

        public void Reset()
        {
            CheckSetup();

            if (_rootTransform != null)
            {
                /*
                 * Clear out the nodes
                 */
                for (var i = _rootTransform.childCount - 1; i >= 0; i--)
                {
                    var child = _rootTransform.GetChild(i);
                    DestroyImmediate(child.gameObject);
                }
            }
            else
            {
                /*
                 * If the RootTransform is not set, actually clear out ALL
                 * children of this GameObject
                 */
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    var child = transform.GetChild(i);
                    DestroyImmediate(child.gameObject);
                }
            }
            
            CreateRootTransform();

            _branches?.Clear();
            _branchTypes?.Clear();
            
            Initialized = false;
            Unblock();
        }

        void BuildCaches()
        {
            if (_branchTypes != null)
            {
                foreach (var pair in _branchTypes)
                {
                    pair.Template.Preprocess(true);
                }
            }
        }

        void CreateRootTransform()
        {
            if (_species?.RootBranch != null)
            {
                if (_rootTransform == null)
                {
                    _rootTransform = new GameObject("Root Transform").transform;
                    _rootTransform.SetParent(transform, false);
                    _rootTransform.rotation = Quaternion.Euler(-90, 0, 0);
                }

                // name = Definition.RootNode.name.Split(' ')[0];

                if (_rootTransform.TryGetComponent(out MeshFilter meshFilter) == false)
                {
                    meshFilter = _rootTransform.gameObject.AddComponent<MeshFilter>();
                }

                meshFilter.sharedMesh = _species.RootBranch.GetMeshVariant(0);

                if (_rootTransform.TryGetComponent(out MeshRenderer meshRenderer) == false)
                {
                    meshRenderer = _rootTransform.gameObject.AddComponent<MeshRenderer>();
                }

                meshRenderer.enabled = true;
                meshRenderer.sharedMaterials =
                    _species.RootBranch.GetComponent<MeshRenderer>().sharedMaterials;
            }
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

            return Grow(maxAttempts, out _);
        }

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
    }
}