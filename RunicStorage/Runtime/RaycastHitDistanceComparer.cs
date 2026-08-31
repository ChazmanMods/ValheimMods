using System.Collections.Generic;
using UnityEngine;

namespace RunicStorage.Runtime;

internal sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
{
	internal static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();
	public int Compare(RaycastHit left, RaycastHit right) => left.distance.CompareTo(right.distance);
}
