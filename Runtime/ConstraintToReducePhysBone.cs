#nullable enable
using System;
using UnityEngine;

namespace Anatawa12.Modifier4Avatar
{
    [AddComponentMenu("Avatar Modifier/M4A Constraint To Reduce PhysBone")]
    public class ConstraintToReducePhysBone : MonoBehaviour
    {
        // The list of child transform of this GameObjects that will be used as PhysBone chains.
        // The transform chains that are not listed here will become ignored by PhysBone,
        // and constraints will handle them instead.
        public Transform?[] pbChains = Array.Empty<Transform?>();
        public bool solveInLocalSpace = true;
    }
}
