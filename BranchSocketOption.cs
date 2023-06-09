using System;
using UnityEngine;

namespace games.noio.planter
{
    [Serializable]
    public class BranchSocketOption
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] BranchTemplate _template;
        [SerializeField] float _probabilityPercent;

        #endregion

        #region PROPERTIES

        public BranchTemplate Template
        {
            get => _template;
            set => _template = value;
        }

        public float ProbabilityPercent
        {
            get => _probabilityPercent;
            set => _probabilityPercent = value;
        }

        #endregion
    }
}