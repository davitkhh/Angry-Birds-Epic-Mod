using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Combatant Forge effect extras, carried in a VisualEffect's EffectTextIdent as "forge:{json}" (Tools/CombatantForge,
// publish._effect_entry):
// - SkillFX_Generic_Text with its own text, colour, outline, size, rotation and arc (the text bent along a circle),
//   instead of a localised word (BattleMgrBase.InstantiateEffectOnCharacter calls ApplyText);
// - "Forge_AfterImages": fading copies of the combatant's rig for a while (AfterImageTrail), no prefab.
// Slow motion needs nothing here: it is the game's own VisualEffectTargetCombatant.SlowMotion.
public static class ForgeEffectExtras
{
	public const string Prefix = "forge:";

	public const string AfterImagesPrefab = "Forge_AfterImages";

	// Forge spawn timings beyond VisualEffectSpawnTiming's: when a slow motion starts, and a projectile's events
	public const int TimingSlowMotion = 10;
	public const int TimingProjectileLaunch = 11;
	public const int TimingProjectileHit = 12;
	public const int TimingProjectileBounce = 13;
	public const int TimingProjectileEnd = 14;
	// when a slow motion is over (back to normal speed), and when the skill's attack or support clip ends
	public const int TimingSlowMotionEnd = 15;
	public const int TimingAnimationEnd = 16;

	// Forge target: where the projectile event happened (BattleMgrBase.SpawnEffectAt)
	public const int TargetProjectile = 20;

	[Serializable]
	public class Settings
	{
		public string text = "";
		public string color = "ffffff";
		public string outline = "";
		public float size = 1f;
		public float rotation;
		public float arc;
		public float duration = 0.6f;
		public float interval = 0.06f;
		public float life = 0.35f;
		public float opacity = 0.6f;
		public string tint = "ffffff";
		public bool behind = true;
		public float spread;
		public float driftX;
		public float driftY;
		public float grow = 1f;
		// rotation: an extra turn (degrees), and what it turns with: "anchor" (the bone it sits on, the game's way),
		// "upright" (never turns) or "bone" (follows the named bone of the combatant)
		public float turn;
		// moved this far (rig units; x toward the enemy) from where it would be
		public float offsetX;
		public float offsetY;
		// a slow motion's ease back to normal speed (seconds; its InterpolationTime is the ease in); < 0: the same
		public float easeOut = -1f;
		public string turnWith = "anchor";
		public string bone = "";
		// sits on this bone of the combatant (instead of its anchor), at this point in the bone's own space
		public string attach = "";
		// the game's own word (a loca key) an effect shows while it carries these settings
		public string word = "";
		public float attachX;
		public float attachY;

		[NonSerialized]
		public bool isText;
	}

	public static Settings Parse(VisualEffect effect)
	{
		if (effect == null || string.IsNullOrEmpty(effect.EffectTextIdent) || !effect.EffectTextIdent.StartsWith(Prefix))
		{
			return null;
		}
		try
		{
			var json = effect.EffectTextIdent.Substring(Prefix.Length);
			var s = JsonUtility.FromJson<Settings>(json);
			s.isText = json.Contains("\"text\"");
			return s;
		}
		catch (Exception ex)
		{
			DebugLog.Error("Forge effect settings: " + ex);
			return null;
		}
	}

	// The effect's extra turn and what it turns with (see Settings)
	public static void ApplyTurn(GameObject spawned, CharacterControllerBattleGroundBase character, Settings s)
	{
		if (spawned == null || s == null)
		{
			return;
		}
		if (Mathf.Abs(s.offsetX) > 0.001f || Mathf.Abs(s.offsetY) > 0.001f)
		{
			var unit = character != null && character.m_AssetController != null ? Mathf.Abs(character.m_AssetController.transform.lossyScale.y) : 1f;
			var forward = character != null && character.GetModel() != null && character.GetModel().CombatantFaction == ABH.Shared.Generic.Faction.Pigs ? -1f : 1f;
			spawned.transform.position += new Vector3(s.offsetX * forward, s.offsetY, 0f) * unit;
		}
		if (s.turnWith == "upright" || s.turnWith == "bone")
		{
			Transform target = null;
			if (s.turnWith == "bone" && character != null && character.m_AssetController != null && !string.IsNullOrEmpty(s.bone))
			{
				var bones = character.m_AssetController.GetComponentInChildren<SmoothMoves.BoneAnimation>();
				target = bones != null ? bones.GetBoneTransform(s.bone) : null;
			}
			var turn = spawned.AddComponent<ForgeTurn>();
			turn.Target = target;
			turn.Offset = s.turn;
			turn.LateUpdate();
		}
		else if (Mathf.Abs(s.turn) > 0.01f)
		{
			spawned.transform.rotation = spawned.transform.rotation * Quaternion.Euler(0f, 0f, s.turn);
		}
	}

	// The bone an effect sits on instead of its anchor (Settings.attach), or null
	public static Transform AttachBone(CharacterControllerBattleGroundBase character, Settings s)
	{
		if (s == null || string.IsNullOrEmpty(s.attach) || character == null || character.m_AssetController == null)
		{
			return null;
		}
		var bones = character.m_AssetController.GetComponentInChildren<SmoothMoves.BoneAnimation>();
		return bones != null ? bones.GetBoneTransform(s.attach) : null;
	}

	// Puts an effect on its point of the bone (parented: it moves with it; else just where it is now)
	public static void PlaceOnBone(GameObject spawned, Transform bone, Settings s, bool parented)
	{
		if (spawned == null || bone == null || s == null)
		{
			return;
		}
		var point = new Vector3(s.attachX, s.attachY, 0f);
		if (parented && spawned.transform.parent == bone)
		{
			spawned.transform.localPosition = point;
		}
		else
		{
			var at = bone.TransformPoint(point);
			spawned.transform.position = new Vector3(at.x, at.y, spawned.transform.position.z);
		}
	}

	public static bool IsAfterImages(VisualEffect effect)
	{
		return effect != null && effect.PrefabName == AfterImagesPrefab;
	}

	private static Color Hex(string hex, Color fallback)
	{
		Color c;
		return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out c) ? c : fallback;
	}

	public static void ApplyText(GameObject spawned, Settings s)
	{
		var label = spawned == null ? null : spawned.GetComponentInChildren<UILabel>(true);
		if (label == null)
		{
			return;
		}
		var color = Hex(s.color, Color.white);
		label.color = new Color(color.r, color.g, color.b, label.color.a);
		if (!string.IsNullOrEmpty(s.outline))
		{
			label.effectStyle = UILabel.Effect.Outline8;
			label.effectColor = Hex(s.outline, Color.black);
		}
		var t = label.transform;
		// words read left to right even on a mirrored (pig side) effect
		var flip = t.lossyScale.x < 0f ? -1f : 1f;
		t.localScale = new Vector3(t.localScale.x * s.size * flip, t.localScale.y * s.size, t.localScale.z);
		t.localRotation = Quaternion.Euler(0f, 0f, s.rotation * flip);
		var text = s.text ?? string.Empty;
		if (Mathf.Abs(s.arc) < 0.5f || text.Length < 2)
		{
			label.text = text;
			return;
		}
		// along an arc: one label per letter, the first keeping the animated colour for the rest (ForgeArcText)
		label.text = string.Empty;
		var letters = new List<UILabel>();
		var widths = new List<float>();
		foreach (var ch in text)
		{
			var copy = UnityEngine.Object.Instantiate(label.gameObject, t);
			foreach (Transform child in copy.transform)
			{
				UnityEngine.Object.Destroy(child.gameObject);
			}
			copy.transform.localScale = Vector3.one;
			copy.transform.localRotation = Quaternion.identity;
			var l = copy.GetComponent<UILabel>();
			l.text = ch.ToString();
			var w = l.printedSize.x;
			widths.Add(ch == ' ' || w <= 0f ? label.fontSize * 0.35f : w);
			letters.Add(l);
		}
		float total = 0f;
		foreach (var w in widths)
		{
			total += w;
		}
		var radius = total / (s.arc * Mathf.Deg2Rad);
		var x = 0f;
		for (var i = 0; i < letters.Count; i++)
		{
			var phi = (x + widths[i] / 2f - total / 2f) / radius;
			letters[i].transform.localPosition = new Vector3(radius * Mathf.Sin(phi), radius * Mathf.Cos(phi) - radius, 0f);
			letters[i].transform.localRotation = Quaternion.Euler(0f, 0f, -phi * Mathf.Rad2Deg);
			x += widths[i];
		}
		var sync = label.gameObject.AddComponent<ForgeArcText>();
		sync.Source = label;
		sync.Letters = letters;
	}

	// Starts fading copies of the character for the effect's duration (unscaled time, like AfterImageTrail).
	public static void AfterImages(MonoBehaviour host, CharacterControllerBattleGroundBase character, Transform parent, Settings s)
	{
		if (host == null || character == null || character.m_AssetController == null)
		{
			return;
		}
		s = s ?? new Settings();
		var trail = AfterImageTrail.Begin(character.m_AssetController.transform, parent);
		trail.Interval = Mathf.Max(0.02f, s.interval);
		trail.FadeTime = Mathf.Max(0.02f, s.life);
		trail.StartAlpha = Mathf.Clamp01(s.opacity);
		trail.Tint = Hex(s.tint, Color.white);
		trail.DepthBehind = s.behind ? 1f : -1f;
		// in the rig's units, forward being toward the enemy side (pigs face left)
		var unit = Mathf.Abs(character.m_AssetController.transform.lossyScale.y);
		var forward = character.GetModel() != null && character.GetModel().CombatantFaction == ABH.Shared.Generic.Faction.Pigs ? -1f : 1f;
		trail.Spread = Mathf.Max(0f, s.spread) * unit;
		trail.Drift = new Vector2(s.driftX * forward, s.driftY) * unit;
		trail.Grow = s.grow <= 0f ? 1f : s.grow;
		host.StartCoroutine(EndAfter(trail, s.duration));
	}

	private static IEnumerator EndAfter(AfterImageTrail trail, float seconds)
	{
		yield return new WaitForSecondsRealtime(Mathf.Max(0f, seconds));
		if (trail != null)
		{
			trail.End();
		}
	}
}

// Keeps an effect turned with another bone (or upright) plus an offset, wherever it is parented.
public class ForgeTurn : MonoBehaviour
{
	public Transform Target;

	public float Offset;

	public void LateUpdate()
	{
		var z = (Target != null ? Target.eulerAngles.z : 0f) + Offset;
		transform.rotation = Quaternion.Euler(0f, 0f, z);
	}
}

// The letters of arced text take the colour (and its animated fade) of the label they were made from.
public class ForgeArcText : MonoBehaviour
{
	public UILabel Source;

	public List<UILabel> Letters;

	private void LateUpdate()
	{
		if (Source == null || Letters == null)
		{
			return;
		}
		foreach (var l in Letters)
		{
			if (l != null)
			{
				l.color = Source.color;
			}
		}
	}
}
