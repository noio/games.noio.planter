using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace games.noio.planter
{
    public class PlantDefinition : ScriptableObject
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [TitleGroup("Placement")]
        [AssetSelector(FlattenTreeView = false, Paths = "Assets/Content/Plants")]
        public BranchTemplate RootNode;

        [Range(0, 90)] [HideIf(nameof(UsingOdin))] public float MinPlantTilt;
        [Range(0, 180)] [HideIf(nameof(UsingOdin))] public float MaxPlantTilt = 180;
        [TitleGroup("Visuals")] public Color ColorA;
        public Color ColorB;

        [TitleGroup("Growth")]
        [PropertyRange(0, nameof(MaxStoredEnergyInt))]
        [Tooltip("How much energy is consumed by growing a single branch")]
        public int EnergyPerBranchInt = 100;

        #endregion

        #region PROPERTIES

        [TitleGroup("Placement", Order = 0)]
        [ShowInInspector]
        [MinMaxSlider(0, 180, true)]
        public Vector2 TiltRange
        {
            get => new Vector2(MinPlantTilt, MaxPlantTilt);
            set
            {
                MinPlantTilt = value.x;
                MaxPlantTilt = value.y;
            }
        }

        /// <summary>
        ///     The [HideIf] attribute that calls this
        ///     is only used by odin.
        /// </summary>
        bool UsingOdin => true;

        public int MaxStoredEnergyInt => 1000;
        public int MaxGrowAttempts = 5000;

        #endregion

#if UNITY_EDITOR



#endif
    }
}