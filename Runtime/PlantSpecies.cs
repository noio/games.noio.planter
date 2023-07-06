// using Sirenix.OdinInspector;

using UnityEditor;
using UnityEngine;

namespace games.noio.planter
{
    [CreateAssetMenu(menuName = "Noio/Planter/Plant Species")]
    public class PlantSpecies : ScriptableObject
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] BranchTemplate _rootBranch;
        [SerializeField] int _maxTotalBranches = 50;
        public BranchTemplate RootBranch => _rootBranch;

        #endregion

        #region PROPERTIES

        public int MaxTotalBranches => _maxTotalBranches;

        #endregion

#if UNITY_EDITOR
#endif
        public static PlantSpecies Create()
        {
            var species = CreateInstance<PlantSpecies>();

            var defaultBranch =
                AssetDatabase.LoadAssetAtPath<BranchTemplate>(
                    AssetDatabase.GUIDToAssetPath("6e3e661e25d7e4b1db21aa6ad06af88d"));

            species._rootBranch = defaultBranch;

            var path = "Assets/New Plant Species.asset";
            AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CreateAsset(species, path);
            AssetDatabase.SaveAssets();
            return species;
        }
    }
}