using System;
using System.Collections.Generic;
using SmoothMoves;
using UnityEngine;

// Combatant Forge: a forged combatant's bones can turn around another point than their own origin (the Forge's
// "Rotation pivot" on a slot, e.g. eyebrows drawn higher than the rig's). Tools/CombatantForge writes
// Resources/ForgeBonePivots/<class item name>.json; CharacterAssetController.SetForgeFeatures hands it to Apply.
// Each frame, after the animation, a bone moves by (rest rotation·scale - current rotation·scale)·pivot, so the
// pivot stays where it is in the rest pose (Idle's first frame) while the bone follows its parent and animated
// position. The Forge's preview does the same (app.js pose).
public static class ForgeBonePivots
{
	[Serializable]
	public class Entry
	{
		public string bone;
		public float x;
		public float y;
		public float restAngle;
		public float restSx = 1f;
		public float restSy = 1f;
	}

	[Serializable]
	public class Data
	{
		public Entry[] bones;
	}

	private static readonly Dictionary<string, Data> s_Cache = new Dictionary<string, Data>();

	private static Data Load(string name)
	{
		Data data;
		if (s_Cache.TryGetValue(name, out data))
		{
			return data;
		}
		var text = Resources.Load<TextAsset>("ForgeBonePivots/" + name);
		if (text != null)
		{
			try
			{
				data = JsonUtility.FromJson<Data>(text.text);
			}
			catch (Exception ex)
			{
				DebugLog.Error("Forge bone pivots " + name + " failed to load: " + ex);
			}
		}
		s_Cache[name] = data;
		return data;
	}

	// assetName: the class item (Forge_<id> or Forge_<id>_<Variant>); null or another item clears them
	public static void Apply(BoneAnimation animation, string assetName)
	{
		if (animation == null)
		{
			return;
		}
		Data data = null;
		if (!string.IsNullOrEmpty(assetName))
		{
			data = Load(assetName);
			var cut = assetName.LastIndexOf('_');
			if (data == null && cut > 0 && cut + 1 < assetName.Length && char.IsUpper(assetName[cut + 1]))
			{
				data = Load(assetName.Substring(0, cut));  // a variant uses its base combatant's pivots
			}
		}
		var driver = animation.GetComponent<ForgeBonePivotDriver>();
		if (data == null || data.bones == null || data.bones.Length == 0)
		{
			if (driver != null)
			{
				driver.Set(null);
			}
			return;
		}
		if (driver == null)
		{
			driver = animation.gameObject.AddComponent<ForgeBonePivotDriver>();
		}
		var list = new List<ForgeBonePivotDriver.Pivot>();
		foreach (var e in data.bones)
		{
			var t = animation.GetBoneTransform(e.bone);
			if (t == null)
			{
				continue;
			}
			var a = e.restAngle * Mathf.Deg2Rad;
			list.Add(new ForgeBonePivotDriver.Pivot
			{
				bone = t,
				pivot = new Vector2(e.x, e.y),
				rest = new Vector4(Mathf.Cos(a) * e.restSx, Mathf.Sin(a) * e.restSx, -Mathf.Sin(a) * e.restSy, Mathf.Cos(a) * e.restSy)
			});
		}
		driver.Set(list);
	}
}

public class ForgeBonePivotDriver : MonoBehaviour
{
	public class Pivot
	{
		public Transform bone;
		public Vector2 pivot;
		// the rest pose's rotation·scale, column-major 2x2 (a, b, c, d): x' = a·x + c·y, y' = b·x + d·y
		public Vector4 rest;
		public Vector3 basePosition;
		public Vector3 lastSet;
		public bool started;
	}

	private List<Pivot> m_Pivots;

	public void Set(List<Pivot> pivots)
	{
		Restore();
		m_Pivots = pivots;
		enabled = pivots != null && pivots.Count > 0;
	}

	private void Restore()
	{
		if (m_Pivots == null)
		{
			return;
		}
		foreach (var p in m_Pivots)
		{
			if (p.bone != null && p.started && p.bone.localPosition == p.lastSet)
			{
				p.bone.localPosition = p.basePosition;
			}
		}
	}

	private void LateUpdate()
	{
		if (m_Pivots == null)
		{
			return;
		}
		foreach (var p in m_Pivots)
		{
			var t = p.bone;
			if (t == null)
			{
				continue;
			}
			// the animation rewrote the position this frame, or it still holds what we set (an unanimated position)
			if (!p.started || t.localPosition != p.lastSet)
			{
				p.basePosition = t.localPosition;
				p.started = true;
			}
			var angle = t.localEulerAngles.z * Mathf.Deg2Rad;
			var s = t.localScale;
			float c = Mathf.Cos(angle), sn = Mathf.Sin(angle);
			float a = c * s.x, b = sn * s.x, cc = -sn * s.y, d = c * s.y;
			var dx = (p.rest.x - a) * p.pivot.x + (p.rest.z - cc) * p.pivot.y;
			var dy = (p.rest.y - b) * p.pivot.x + (p.rest.w - d) * p.pivot.y;
			var set = new Vector3(p.basePosition.x + dx, p.basePosition.y + dy, p.basePosition.z);
			t.localPosition = set;
			p.lastSet = t.localPosition;
		}
	}

	private void OnDisable()
	{
		Restore();
	}
}
