using System;
using UnityEngine;

namespace games.noio.planter
{
    [Serializable]
    public class BranchMeshVariant
    {
        [SerializeField] Mesh _mesh;
        [SerializeField] float _probabilityPercent = 100;

        public Mesh Mesh
        {
            get => _mesh;
            set => _mesh = value;
        }

        public float ProbabilityPercent
        {
            get => _probabilityPercent;
            set => _probabilityPercent = value;
        }

    }
}