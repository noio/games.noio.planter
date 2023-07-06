using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace games.noio.planter.Combiner
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class MeshMerger : MonoBehaviour
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] AfterMergeAction _afterMerge;

        #endregion

        /// <summary>
        ///     Combine Different meshes (from different renderers)
        ///     Into a single mesh with many submeshes
        /// </summary>
        /// <param name="renderers">List of renderers whose meshes you want to combine</param>
        /// <param name="refTransform">
        ///     Transformation that is applied to all children (in addition to their
        ///     own localToWorld matrix). If you're merging relative to a specific parent, pass the
        ///     INVERSE of that parent's transform here. (so the worldToLocalMatrix)
        /// </param>
        /// <param name="allMaterials">A list of the materials that should be applied to each submesh</param>
        /// <param name="mergeSubMeshes">
        ///     Should the submeshes be merged into a single submesh,
        ///     regardless of materials
        /// </param>
        /// <returns></returns>
        public static Mesh CombineMeshes(
            IEnumerable<MeshRenderer> renderers,
            Matrix4x4                 refTransform,
            out List<Material>        allMaterials,
            bool                      mergeSubMeshes = false)
        {
            var mesh = new Mesh();
            CombineMeshes(mesh, renderers, refTransform, out allMaterials, mergeSubMeshes);
            return mesh;
        }

        public static Mesh CombineMeshes(
            Mesh                      mesh,
            IEnumerable<MeshRenderer> renderers,
            Matrix4x4                 refTransform,
            out List<Material>        allMaterials,
            bool                      mergeSubMeshes = false)
        {
            /*
             * When merging submeshes, the material list is nonsense, since
             * there won't be multiple submeshes to apply the material to.
             * In that case, just choose what material you want to apply
             * to the single merged mesh. (i.e. set the material on the
             * MeshRenderer that you'll use to render the returned mesh)
             */
            allMaterials = mergeSubMeshes ? null : new List<Material>();
            var combineInstances = new List<CombineInstance>();
            var vertCount = 0;

            foreach (var rndr in renderers)
            {
                if (rndr.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                {
                    var packInputMesh = mf.sharedMesh;
                    var sharedMaterials = rndr.sharedMaterials;
                    var localMatrix = refTransform * rndr.transform.localToWorldMatrix;

                    for (var i = 0; i < sharedMaterials.Length; i++)
                    {
                        vertCount += packInputMesh.vertexCount;
                        var mat = sharedMaterials[i];
                        allMaterials?.Add(mat);

                        /*
                         * Mimic unity behavior:
                         *
                         * If there are more materials assigned to the Mesh Renderer than there are
                         * submeshes in the mesh, the first submesh is rendered with each of the
                         * remaining materials, one on top of the next.
                         */
                        combineInstances.Add(new CombineInstance
                        {
                            mesh = packInputMesh,
                            subMeshIndex = i < packInputMesh.subMeshCount ? i : 0,
                            transform = localMatrix
                        });
                    }
                }
            }

            /*
             * Now we have combineInstances and a list of materials
             */
            if (vertCount > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.CombineMeshes(combineInstances.ToArray(), mergeSubMeshes, true);
            return mesh;
        }

        /// <summary>
        ///     Combines submeshes with the same material in Mesh.
        ///     Yielding one submesh per material.
        ///     Returns the new list of materials
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="materials"></param>
        /// <returns>
        ///     List of materials used on each of the submeshes
        ///     (corresponding to renderer.sharedMaterials).
        ///     This list is MODIFIED to contain the unique materials
        ///     AFTER merging.
        /// </returns>
        public static void MergeSubmeshes(Mesh mesh, List<Material> materials)
        {
            /*
             * Create a mapping from each material
             * to an index in a list of unique materials.
             * The unique materials also define the Submeshes that
             * will be created after this method runs
             *
             * Use the input material list to store the unique materials,
             * (in the first part of the list, as we go through it, then
             * truncate the list)
             *
             *  unique materials | junk | input materials
             *   U U U U U U U U J J J J M M M M M M M M
             *
             */
            var submeshCount = mesh.subMeshCount;
            Assert.AreEqual(submeshCount, materials.Count,
                $"Submesh Count: {submeshCount} and Material Count: {materials.Count} must match.");

            var newMaterialMappping = new int[materials.Count];
            var firstOpenEntry = 0;

            for (var index = 0; index < materials.Count; index++)
            {
                var material = materials[index];

                /*
                 * Search only the 'unique materials' part
                 */
                var firstOccurrence = materials.IndexOf(material, 0, firstOpenEntry);
                /*
                 * Found an existing entry
                 */
                if (firstOccurrence > -1)
                {
                    newMaterialMappping[index] = firstOccurrence;
                }
                else
                {
                    materials[firstOpenEntry] = material;
                    newMaterialMappping[index] = firstOpenEntry;
                    firstOpenEntry++;
                }
            }

            materials.RemoveRange(firstOpenEntry, materials.Count - firstOpenEntry);

            var newSubmeshes = new List<List<int>>();

            /*
             * Combine triangles for submeshes with the same materials into
             * one list.
             */
            for (var oldSubmeshIndex = 0; oldSubmeshIndex < submeshCount; oldSubmeshIndex++)
            {
                var newSubmeshIndex = newMaterialMappping[oldSubmeshIndex];
                if (newSubmeshIndex >= newSubmeshes.Count)
                {
                    newSubmeshes.Add(new List<int>());
                }

                newSubmeshes[newSubmeshIndex].AddRange(mesh.GetTriangles(oldSubmeshIndex));
            }

            mesh.subMeshCount = newSubmeshes.Count;

            for (var newSubmeshIndex = 0; newSubmeshIndex < newSubmeshes.Count; newSubmeshIndex++)
            {
                var submesh = newSubmeshes[newSubmeshIndex];
                mesh.SetTriangles(submesh, newSubmeshIndex);
            }
        }

        [ContextMenu("Combine Child Renderers")]
        public void MergeChildren()
        {
            var renderers = GetComponentsInChildren<MeshRenderer>(includeInactive: true)
                            .Where(mr => mr.gameObject != gameObject)
                            .ToList();

            /*
             * - First combine the different meshes into a single mesh with submeshes
             * - Then merge submeshes that use the same material
             */
            var mesh = CombineMeshes(renderers, transform.worldToLocalMatrix, out var combinedMaterials);
            mesh.name = name;
            MergeSubmeshes(mesh, combinedMaterials);

            var meshRenderer = gameObject.GetComponent<MeshRenderer>();
            var meshFilter = gameObject.GetComponent<MeshFilter>();

            meshRenderer.sharedMaterials = combinedMaterials.ToArray();
            meshFilter.mesh = mesh;

            switch (_afterMerge)
            {
                case AfterMergeAction.DisableRenderers:
                    foreach (var r in renderers)
                    {
                        r.enabled = false;
                    }

                    break;
                case AfterMergeAction.RemoveChildren:
                    foreach (var r in renderers)
                    {
                        DestroyImmediate(r.gameObject);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public enum AfterMergeAction
    {
        DisableRenderers,
        RemoveChildren
    }
}