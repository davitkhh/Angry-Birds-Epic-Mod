using System;
using UnityEngine;

// How a bird picks its target in battle (the rage chili is not affected):
//   Drag      - the battle ring follows the finger (the original behaviour).
//   Slingshot - the ring moves opposite the finger, mirrored through the press point (pull back to aim, like a
//               slingshot). Where the screen leaves less room for the finger than for the ring in that direction
//               (pulling left of the birds to aim at the pigs), the ring travels further than the finger so the
//               finger reaching the screen edge puts the ring on the opposite edge. A dotted line in the ring's
//               colour runs from the bird to the real finger (CharacterControlHUD.SetAimLine).
// Locked tutorial steps always use Drag, because the guide hand demonstrates dragging onto the target.
//
// The toggle is a button Install() clones into the world-map options strip (next to the sound button); it loops
// through the styles. The choice is a device setting (player prefs key "targeting_style").
public static class TargetingStyle
{
	public enum Style
	{
		Drag = 0,
		Slingshot = 1
	}

	private const string PrefsKey = "targeting_style";

	private const string ButtonName = "2b_TargetingStyleButton";

	private const int IconSize = 66;

	private static readonly string[] IconTextures = { "UI/TargetingStyle_Drag", "UI/TargetingStyle_Slingshot" };

	private static int s_cached = -1;

	public static Style Current
	{
		get
		{
			if (s_cached < 0)
			{
				var prefs = DIContainerInfrastructure.GetPlayerPrefsService();
				s_cached = prefs != null ? prefs.GetInt(PrefsKey, 0) : 0;
				if (!Enum.IsDefined(typeof(Style), s_cached))
				{
					s_cached = 0;
				}
			}
			return (Style)s_cached;
		}
		set
		{
			s_cached = (int)value;
			var prefs = DIContainerInfrastructure.GetPlayerPrefsService();
			if (prefs != null)
			{
				prefs.SetInt(PrefsKey, s_cached);
			}
		}
	}

	public static bool UsesSlingshot
	{
		get
		{
			return Current == Style.Slingshot && !(DIContainerInfrastructure.TutorialMgr != null && DIContainerInfrastructure.TutorialMgr.IsCurrentlyLocked);
		}
	}

	public static Style Next(Style style)
	{
		var count = Enum.GetValues(typeof(Style)).Length;
		return (Style)(((int)style + 1) % count);
	}

	// Screen position of the ring for a finger at mouse, the drag having started at press (on the bird).
	public static Vector2 SlingshotAim(Vector2 press, Vector2 mouse)
	{
		var delta = mouse - press;
		if (delta.sqrMagnitude < 0.0001f)
		{
			return press;
		}
		var dir = delta.normalized;
		var fingerRoom = DistanceToScreenEdge(press, dir);
		var ringRoom = DistanceToScreenEdge(press, -dir);
		var gain = fingerRoom > 1f ? Mathf.Max(1f, ringRoom / fingerRoom) : 1f;
		var aim = press - delta * gain;
		return new Vector2(Mathf.Clamp(aim.x, 0f, Screen.width - 1f), Mathf.Clamp(aim.y, 0f, Screen.height - 1f));
	}

	private static float DistanceToScreenEdge(Vector2 from, Vector2 dir)
	{
		var t = float.MaxValue;
		if (dir.x > 0.0001f)
		{
			t = Mathf.Min(t, (Screen.width - from.x) / dir.x);
		}
		else if (dir.x < -0.0001f)
		{
			t = Mathf.Min(t, -from.x / dir.x);
		}
		if (dir.y > 0.0001f)
		{
			t = Mathf.Min(t, (Screen.height - from.y) / dir.y);
		}
		else if (dir.y < -0.0001f)
		{
			t = Mathf.Min(t, -from.y / dir.y);
		}
		return Mathf.Max(0f, t);
	}

	// Clones the sound button into the options strip's grid (sorted by name: right after the sound button) and widens
	// the strip's background by one cell.
	public static void Install(OptionsMgr options)
	{
		if (options == null || options.m_SoundButton == null || options.m_ButtonGrid == null)
		{
			return;
		}
		try
		{
			var grid = options.m_ButtonGrid.transform;
			if (grid.Find(ButtonName) != null)
			{
				return;
			}
			var template = options.m_SoundButton.transform.parent;
			var clone = UnityEngine.Object.Instantiate(template.gameObject);
			clone.name = ButtonName;
			clone.transform.parent = grid;
			clone.transform.localScale = template.localScale;
			clone.transform.localRotation = template.localRotation;
			clone.transform.localPosition = template.localPosition;
			UITexture icon = null;
			foreach (var sprite in clone.GetComponentsInChildren<UISprite>(true))
			{
				if (sprite.name != "Icon")
				{
					continue;
				}
				sprite.enabled = false;
				var go = new GameObject("TargetingStyleIcon");
				go.layer = sprite.gameObject.layer;
				go.transform.parent = sprite.transform.parent;
				go.transform.localPosition = sprite.transform.localPosition;
				go.transform.localRotation = Quaternion.identity;
				go.transform.localScale = Vector3.one;
				icon = go.AddComponent<UITexture>();
				icon.width = IconSize;
				icon.height = IconSize;
				icon.depth = sprite.depth;
				break;
			}
			ShowIcon(icon);
			var trigger = clone.GetComponentInChildren<UIInputTrigger>(true);
			if (trigger != null)
			{
				trigger.Clicked += () =>
				{
					Current = Next(Current);
					ShowIcon(icon);
				};
			}
			if (options.m_Background != null)
			{
				var body = options.m_Background.GetComponent<UISprite>();
				if (body != null)
				{
					body.width += Mathf.RoundToInt(options.m_ButtonGrid.cellWidth);
				}
			}
			options.m_ButtonGrid.Reposition();
		}
		catch (Exception ex)
		{
			DebugLog.Error("[TargetingStyle] Install failed: " + ex);
		}
	}

	private static void ShowIcon(UITexture icon)
	{
		if (icon == null)
		{
			return;
		}
		icon.mainTexture = Resources.Load<Texture2D>(IconTextures[(int)Current]);
	}
}
