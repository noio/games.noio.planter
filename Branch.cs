using UnityEngine;

namespace games.noio.planter
{
    public class Branch : MonoBehaviour
    {
        #region PUBLIC AND SERIALIZED FIELDS

        [SerializeField] int _depth = 0;
        [SerializeField] BranchTemplate _template;
        [SerializeField] Branch[] _children = new Branch[4];

        #endregion

        #region PROPERTIES

        public int Depth
        {
            get => _depth;
            set => _depth = value;
        }

        public BranchTemplate Template
        {
            get => _template;
            set => _template = value;
        }

        public Branch[] Children => _children;

        #endregion
    }
}