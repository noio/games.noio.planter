// (C)2023 @noio_games
// Thomas van den Berg

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;

namespace games.noio.planter
{
    [SelectionBase]
    [ExecuteAlways]
    public class Plant : MonoBehaviour
    {
        public event Action BranchAdded;
        const int MaxFailedGrowAttempts = 5000;
        const int GrowAttemptsPerFrame = 200;
        const int BranchesPerFrame = 20;
        static readonly Collider[] ColliderCache = new Collider[4];

        #region PUBLIC AND SERIALIZED FIELDS

        [Tooltip("Restart simulation when the GameObject is moved")]
        [SerializeField]
        bool _restartWhenMoved;

        [Tooltip("Keep colliders on individual branches after simulation is over")]
        [SerializeField]
        bool _keepColliders;

        [Tooltip("Keep 'Branch' components on individual branches after simulation is over")]
        [SerializeField]
        bool _keepBranchComponents;

        [SerializeField] Branch _rootBranch;
        [SerializeField] PlantSpecies _species;
        [SerializeField] int _seed;
        [SerializeField] Random.State _randomState;
        [SerializeField] PlantState _state;
        [SerializeField] Vector3 _grownAtPosition;
        [SerializeField] Quaternion _grownAtRotation;
        [SerializeField] int _growSucceeded;
        [SerializeField] int _growFailed;
        [SerializeField] float _difficulty;
        [SerializeField] UnityEvent _growStarted;
        [SerializeField] UnityEvent _growComplete;

        #endregion

        List<Branch> _branches;
        Queue<Branch> _branchesWithOpenSockets;
        List<BranchType> _branchTypes;
        List<(BranchType branchType, float weight)> _growableBranchTypes;
        int _nextSocketIndex;

        #region PROPERTIES

        /// <summary>
        ///     Number of failed grow attempts since the last successful grow attempt
        ///     (the last time a branch was added)
        /// </summary>
        public int FailedAttemptsSinceBranchAdded { get; private set; }

        public IReadOnlyList<BranchType> BranchTypes => _branchTypes;

        #endregion

        #region MONOBEHAVIOUR METHODS

        void Update()
        {
            switch (_state)
            {
                case PlantState.Growing:
                {
                    PrepareForGrowing();

                    for (var i = 0; i < BranchesPerFrame; i++)
                    {
                        Grow();
                    }

                    if (_state == PlantState.Growing)
                    {
                        ForceQueueUpdate();
                    }

                    break;
                }
                case PlantState.Done:
                    break;
                case PlantState.MissingData:
                    break;
                default:
                    _state = PlantState.Done;
                    break;
            }
        }

        #endregion

        public void Restart()
        {
            ResetPlant();
            ForceQueueUpdate();
            _state = PlantState.Growing;
        }

        public bool CheckIfMovedAndReset()
        {
            if (_restartWhenMoved &&
                _state != PlantState.MissingData &&
                (Vector3.Distance(transform.localPosition, _grownAtPosition) > .01f ||
                 Quaternion.Angle(transform.localRotation, _grownAtRotation) > 1))
            {
                ResetPlant();
                Undo.RecordObject(this, "Start Growing");
                _state = PlantState.Growing;
                _grownAtPosition = transform.localPosition;
                _grownAtRotation = transform.localRotation;

                return true;
            }

            return false;
        }

        static void ForceQueueUpdate()
        {
#if UNITY_EDITOR
            if (Application.isPlaying == false)
            {
                EditorApplication.delayCall += EditorApplication.QueuePlayerLoopUpdate;
            }
#endif
        }

        void ResetPlant()
        {
            CheckSetup();
            if (_rootBranch == null || _rootBranch.Template != _species.RootBranch)
            {
                /*
                 * If the RootBranch is not set, actually clear out ALL
                 * children of this GameObject
                 */
                ClearChildren(transform);
            }
            else
            {
                /*
                 * Clear out the nodes
                 */
                ClearChildren(_rootBranch.transform);
            }

            /*
             * This will clear cache if it's not valid, but otherwise
             * it will preserve the root branch.
             * We want to preserve the root branch because dragging in inspector
             * is annoying otherwise:
             * if the collider is constantly recreated while dragging,
             * the raycast that Unity uses for "move on surface" will hit the
             * re-created collider. (But unity is smart enough to ignore existing children
             * of the dragged object)
             */
            TryReconstructCacheFromHierarchy();

            PreprocessBranchType(_species.RootBranch);

            CreateRootBranch();

            _branchesWithOpenSockets.Enqueue(_branches[0]);
            _nextSocketIndex = 0;

            UpdateGrowableBranchTypes();

            Random.InitState(_seed);
            _randomState = Random.state;

            FailedAttemptsSinceBranchAdded = 0;
            _growSucceeded = 0;
            _growFailed = 0;
            _difficulty = 0;

            /*
             * Block to prevent plant from growing immediately
             */
            _state = PlantState.Done;
        }

        static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                DestroyImmediate(child.gameObject);
            }
        }

        void PrepareForGrowing()
        {
            CheckSetup();

            if (IsCacheValid() == false)
            {
                /*
                 * Try to reconstruct cache from Game Object Hierarchy
                 */
                TryReconstructCacheFromHierarchy();
            }

            /*
             * Reconstructing from gameobject hierarchy didn't work
             * (or the GO is just empty)
             */
            if (IsCacheValid() == false)
            {
                ResetPlant(); // also clears cache
            }

            _state = PlantState.Growing;
        }

        static Quaternion RotateAroundZ(float zRad)
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

        bool TryReconstructCacheFromHierarchy()
        {
            ClearCache();

            PreprocessBranchType(_species.RootBranch);

            foreach (var branch in GetComponentsInChildren<Branch>())
            {
                var branchType = BranchTypes.FirstOrDefault(bt => bt.Template == branch.Template);
                if (branchType == null)
                {
                    /*
                     * Clear cache to make sure it's invalid and calling
                     * function will proceed with rebuilding from scratch
                     */
                    ClearCache();
                    return false;
                }

                branchType.TotalCount++;

                _branches.Add(branch);
                if (branch.HasOpenSockets())
                {
                    _branchesWithOpenSockets.Enqueue(branch);
                }
            }

            UpdateGrowableBranchTypes();
            return true;
        }

        void ClearCache()
        {
            _branchTypes?.Clear();
            _branchTypes ??= new List<BranchType>();

            _branches?.Clear();
            _branches ??= new List<Branch>();

            _branchesWithOpenSockets?.Clear();
            _branchesWithOpenSockets ??= new Queue<Branch>();

            _growableBranchTypes?.Clear();
            _growableBranchTypes ??= new List<(BranchType branchType, float weight)>();
        }

        bool IsCacheValid()
        {
            return _branchTypes != null && _branchTypes.Count > 0 &&
                   _branches != null && _branches.Count > 0;
        }

        /// <summary>
        ///     Perform one pass of 'settling' the plant on underlying objects (Moving it closer)
        /// </summary>
        /// <returns>Whether the plant is settled</returns>
        bool SnapToGround(int steps = 10, float lerpAmount = .2f)
        {
            for (var i = 0; i < steps; i++)
            {
                var rayOrigin = transform.TransformPoint(new Vector3(0, 2, 0));
                var dir = new Vector3(Random.Range(-.5f, .5f), -1, Random.Range(-.5f, .5f));
                var rayDirection = transform.TransformDirection(dir);
                Debug.DrawRay(rayOrigin, rayDirection, Color.magenta, 10);
                var rootNode = _species.RootBranch;
                var layers = rootNode.ObstacleLayers ^ (1 << rootNode.gameObject.layer);
                if (Physics.SphereCast(rayOrigin, 2, rayDirection, out var hitInfo, 5,
                        layers))
                {
                    Debug.DrawLine(hitInfo.point, hitInfo.point + Vector3.right, Color.cyan, 20);
                    if (Vector3.Distance(transform.position, hitInfo.point) > .03f)
                    {
                        transform.position = Vector3.Lerp(transform.position, hitInfo.point, lerpAmount);
                        transform.rotation = Quaternion.Lerp(transform.rotation,
                            Quaternion.LookRotation(hitInfo.normal, -transform.forward) *
                            Quaternion.Euler(90, 0, 0),
                            lerpAmount);
                    }
                    else
                    {
                        /*
                         * Plant was close to ray hit
                         */
                        return true;
                    }
                }
                else
                {
                    /*
                     * Ray didn't hit anything, we're floating so don't settle
                     */
                    return true;
                }
            }

            return false;
        }

        void PreprocessBranchType(BranchTemplate template)
        {
            /*
             * Skip templates that we already added
             */
            if (BranchTypes.Any(bt => bt.Template == template))
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

            template.FindSockets();

            var branchType = new BranchType(template);

            _branchTypes.Add(branchType);

            foreach (var socket in template.Sockets)
            {
                foreach (var branchOption in socket.BranchOptions)
                {
                    PreprocessBranchType(branchOption.Template);
                }
            }
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
        /// <returns></returns>
        bool Grow()
        {
            var attempts = GrowAttemptsPerFrame;
            while (attempts-- > 0)
            {
                var branch = FindBranchToGrow();
                if (branch != null)
                {
                    return true;
                }
            }

            if (FailedAttemptsSinceBranchAdded > MaxFailedGrowAttempts)
            {
                OnGrowComplete();
            }

            return false;
        }

        Branch FindBranchToGrow()
        {
            if (_branches.Count >= _species.MaxTotalBranches)
            {
                OnGrowComplete();
                return null;
            }

            if (_branchesWithOpenSockets.Count == 0)
            {
                OnGrowComplete();
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
            foreach (var bt in BranchTypes)
            {
                if (bt.Growable && openSocket.IsBranchOption(bt.Template, out var weight))
                {
                    _growableBranchTypes.Add((bt, weight));
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
                OnGrowFailed();
                return null;
            }

            // var address = attemptGrowAtSocket.Address;
            // var totalWeight = _growableBranchTypes.Sum(bt => bt.;);
            // _growableBranchTypes;
            var pair = Utils.Utils.PickWeighted(_growableBranchTypes, tuple => tuple.weight);
            var branchType = pair.branchType;
            var template = branchType.Template;

            // var parent = _branches[GetParentAddress(address)];
            GetSocketPositionAndRotation(parent, _nextSocketIndex,
                out var socketLocalPos, out var socketLocalRot);

            // var parent = GetParentAndSocketPosition(address, out var socketRelPos, out var parentRelRot);

            /*
             * These are actually the only calls to random in the entire plant algorithm!
             * Neat!
             * (We restore and save state here, in case some other code uses random
             * in between calls to Update().
             */
            Random.state = _randomState;

            // Try a random orientation for the child node
            var xRot = Random.Range(-template.MaxPivotAngle, template.MaxPivotAngle);
            var yRot = Random.Range(-template.MaxPivotAngle, template.MaxPivotAngle);
            var zRot = Random.Range(-template.MaxRollAngle, template.MaxRollAngle);
            _randomState = Random.state;

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

            if (template.GrowUpwards < 0)
            {
                var down = Quaternion.LookRotation(Vector3.down, globalRot * Vector3.forward);
                globalRot = Quaternion.SlerpUnclamped(globalRot, down, -template.GrowUpwards);
            }
            else if (template.GrowUpwards > 0)
            {
                var up = Quaternion.LookRotation(Vector3.up, globalRot * Vector3.back);
                globalRot = Quaternion.SlerpUnclamped(globalRot, up, template.GrowUpwards);
            }

            if (template.FaceUpwards)
            {
                globalRot = Quaternion.LookRotation(globalRot * Vector3.forward, Vector3.up);
            }

            var roll = RotateAroundZ(zRot * Mathf.Deg2Rad);
            globalRot *= roll;

            if (CheckPlacement(globalPos, globalRot, template, parent.gameObject))
            {
                var branch = AddBranch(parent, _nextSocketIndex, branchType,
                    socketLocalPos, Quaternion.Inverse(parent.transform.rotation) * globalRot);

                /*
                 * When successfully adding a branch we call SyncTransforms because
                 * otherwise calls CheckCapsule will not hit the newly created colliders
                 */
                Physics.SyncTransforms();

                if (branch.HasOpenSockets())
                {
                    _branchesWithOpenSockets.Enqueue(branch);
                }

                if (parent.HasOpenSockets() == false)
                {
                    /*
                     * This parent had all sockets filled, remove from
                     * 'open sockets' list.
                     */
                    _nextSocketIndex = 0;
                    _branchesWithOpenSockets.Dequeue();
                }

                UpdateGrowableBranchTypes();

                OnGrowSuccess();
                BranchAdded?.Invoke();
                return branch;
            }

            _nextSocketIndex++;
            OnGrowFailed();
            return null;
        }

        void OnGrowComplete()
        {
            Undo.RecordObject(this, "Complete Growth");
            if (_keepColliders == false)
            {
                /*
                 * Do this one frame later in case we created some colliders in this
                 * frame still.
                 */
                EditorApplication.delayCall += RemoveUnwantedComponents;
            }

            _state = PlantState.Done;
            _growComplete?.Invoke();
        }

        /// <summary>
        ///     Remove "CapsuleCollider" and "Branch" components
        ///     from all branches.
        /// </summary>
        void RemoveUnwantedComponents()
        {
            foreach (var branch in _branches)
            {
                if (_keepColliders == false && branch.TryGetComponent(out CapsuleCollider capsule))
                {
                    DestroyImmediate(capsule);
                }

                if (_keepBranchComponents == false)
                {
                    DestroyImmediate(branch);
                }
            }

            if (_keepBranchComponents == false)
            {
                _branches.Clear();
            }
        }

        void OnGrowSuccess()
        {
            FailedAttemptsSinceBranchAdded = 0;
            _growSucceeded++;
            var f = Mathf.Log10(MaxFailedGrowAttempts);
            _difficulty =
                Mathf.Clamp01(-Mathf.Log10((float)_growSucceeded / (_growSucceeded + _growFailed)) / f);
        }

        void OnGrowFailed()
        {
            FailedAttemptsSinceBranchAdded++;
            _growFailed++;
            var f = Mathf.Log10(MaxFailedGrowAttempts);
            _difficulty =
                Mathf.Clamp01(-Mathf.Log10((float)_growSucceeded / (_growSucceeded + _growFailed)) / f);
        }

        void UpdateGrowableBranchTypes()
        {
            var anyGrowable = false;
            foreach (var branchType in BranchTypes)
            {
                var template = branchType.Template;
                if (_branches.Count >= template.MinTotalOtherBranches &&
                    branchType.TotalCount < template.MaxCount &&
                    (branchType.TotalCount + 1f) / (_branches.Count + 1f) <= template.QuotaPercent / 100f)
                {
                    anyGrowable = true;
                    branchType.Growable = true;
                }
                else
                {
                    branchType.Growable = false;
                }
            }

            if (anyGrowable == false)
            {
                OnGrowComplete();
            }
        }

        bool CheckPlacement(
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

            var occupied = template.ObstacleLayers;

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

        bool CheckIfTouchesSurface(Vector3 globalPos, Quaternion globalRot, BranchTemplate template)
        {
            var radius = template.Capsule.radius;
            var height = template.Capsule.height;
            var dir = globalRot * Vector3.forward;

            var offset = globalRot * Vector3.down * radius;
            var start = globalPos + offset + dir * (0.5f * height);
            var end = globalPos + offset + dir * (height - radius);

            radius *= template.SurfaceDistance;

            var count =
                Physics.OverlapCapsuleNonAlloc(start, end, radius, ColliderCache, template.SurfaceLayers);
            for (var i = 0; i < count; i++)
            {
                /*
                 * Among the found colliders, we need to check whether they belong to the current plant
                 * in order to avoid plants from using themselves as a surface. Although it would be more
                 * efficient to create a dedicated layer called 'Plants' for all plants and exclude it
                 * from the SurfaceLayers, I don't want to impose a specific layer setup on users of
                 * this package. I want to avoid setting layers on the sample plants that could
                 * potentially clash with existing layers in a project.
                 */
                var cldr = ColliderCache[i];
                if (cldr.TryGetComponent(out Branch branch))
                {
                    var owner = branch.GetComponentInParent<Plant>();
                    if (owner == this)
                    {
                        continue;
                    }
                }

                return true;
            }

            return false;
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

        /// <summary>
        ///     Checks if a Species is set, and if a RootNode is
        ///     created & set. Will create a rootnode if none is set.
        /// </summary>
        void CheckSetup()
        {
            if (_species == null)
            {
                _state = PlantState.MissingData;
                Debug.LogError($"Create and set a {nameof(PlantSpecies)} on Plant \"{name}\"");
                return;
            }

            if (_species.RootBranch == null)
            {
                _state = PlantState.MissingData;
                Debug.LogError(
                    $"Create and set a Root Branch ({nameof(BranchTemplate)}) on Species \"{_species}\"");
            }
        }

        void CreateRootBranch()
        {
            if (_species?.RootBranch != null && _rootBranch == null)
            {
                _rootBranch = AddBranch(null, 0, BranchTypes[0], Vector3.zero, Quaternion.Euler(-90, 0, 0));
            }
        }

        Branch AddBranch(
            Branch     parent,
            int        socketIndex,
            BranchType branchType,
            Vector3    localPosition,
            Quaternion localRotation)
        {
            var branch = branchType.Template.CreateBranch();
            var branchTransform = branch.transform;

            if (parent != null)
            {
                parent.Children[socketIndex] = branch;
                branch.transform.SetParent(parent.transform, false);
                branch.Depth = parent.Depth + 1;
            }
            else
            {
                branchTransform.SetParent(transform, false);
            }

            branchTransform.SetLocalPositionAndRotation(localPosition, localRotation);

            branch.name = $"D{branch.Depth} {branchType.Template.name}";
            branch.Template = branchType.Template;
            Undo.RegisterCreatedObjectUndo(branch.gameObject, $"Added branch for {name}");

            _branches.Add(branch);
            branchType.TotalCount++;
            return branch;
        }

        public class BranchType
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
    }
}

public enum PlantState
{
    MissingData,
    Growing,
    Done
}