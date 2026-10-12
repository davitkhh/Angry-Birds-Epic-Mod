using System.Collections.Generic;
using UnityEngine;

// Put on a world map foreground visual (a prop drawn in front of the birds). With m_BirdsJumpInFront on, birds jumping
// along a path that overlaps this visual are drawn in front of it while they're in the air.
public class WorldMapJumpForeground : MonoBehaviour
{
	public bool m_BirdsJumpInFront = true;

	private static readonly List<WorldMapJumpForeground> s_Active = new List<WorldMapJumpForeground>();

	public static List<WorldMapJumpForeground> Active
	{
		get
		{
			return s_Active;
		}
	}

	private void OnEnable()
	{
		s_Active.Add(this);
	}

	private void OnDisable()
	{
		s_Active.Remove(this);
	}

	// The closest z (most negative) of this visual's renderers that overlap area on screen, or +infinity.
	public float GetFrontZ(Bounds area)
	{
		var frontZ = float.PositiveInfinity;
		foreach (var renderer in GetComponentsInChildren<Renderer>())
		{
			var bounds = renderer.bounds;
			if (renderer.enabled && bounds.min.x <= area.max.x && bounds.max.x >= area.min.x && bounds.min.y <= area.max.y && bounds.max.y >= area.min.y)
			{
				frontZ = Mathf.Min(frontZ, bounds.min.z);
			}
		}
		return frontZ;
	}
}
