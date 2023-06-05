// using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace games.noio.planter
{
    [CreateAssetMenu(menuName="Noio/Planter/Plant Species")]
    public class PlantSpecies : ScriptableObject
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] BranchTemplate _rootBranch;

        [SerializeField] int _maxTotalBranches = 250;
        // [TitleGroup("Placement")]
        // [AssetSelector(FlattenTreeView = false, Paths = "Assets/Content/Plants")]
        public BranchTemplate RootBranch => _rootBranch;


        #endregion

        #region PROPERTIES

        // [TitleGroup("Placement", Order = 0)]
        // [ShowInInspector]
        // [MinMaxSlider(0, 180, true)]
        public int MaxTotalBranches => _maxTotalBranches;

        #endregion

#if UNITY_EDITOR



#endif
    }
}