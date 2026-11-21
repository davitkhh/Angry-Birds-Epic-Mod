using System;
using UnityEngine;

// Which pieces of equipment the birds wear on the world map (the main hand, the headgear, the off hand).
//
// The strip button (a dotted outline of Red; Install() clones it into the world-map options strip next to the
// targeting style button) opens a popup above itself with one button per piece - lit yellow when that piece is shown,
// darkened when it is hidden - and a select-all button on the right. Select-all turns everything on when most pieces
// are off (green tick) and everything off when most are on (red cross).
//
// The choice is a device setting (player prefs key "worldmap_gear", a bit mask of Part); by default only the headgear
// is shown, which is how the world map always drew the birds. Only the world map's own birds follow it
// (CharacterControllerWorldMap.m_UsesWorldMapGear, set in WorldMapStateMgr.LoadBirdsIntoScene); see
// CharacterAssetController.SetWorldMapToggledGear.
//
// Icons: Resources/UI/WorldMapGear_*.png, made by ArtSource/WorldMapGearIcons/make_icons.py.
public class WorldMapGear : MonoBehaviour
{
	[Flags]
	public enum Part
	{
		MainHand = 1,
		Headgear = 2,
		OffHand = 4
	}

	public const int AllParts = (int)(Part.MainHand | Part.Headgear | Part.OffHand);

	private const string PrefsKey = "worldmap_gear";

	private const int DefaultMask = (int)Part.Headgear;

	private const string ButtonName = "2c_WorldMapGearButton";

	private const string PopupName = "WorldMapGearPopup";

	private const string SceneName = "Menu_WorldMap";

	private const int IconSize = 66;

	private const int MarkSize = 44;

	// Popup layout (UI units): the strip's 84 buttons on a strip-style body, a tail pointing down at the strip button.
	private const float Cell = 84f;

	private const float BodyPadding = 12f;

	private const float TailSize = 32f;

	private const float GapAboveButton = 4f;

	// Off buttons: the yellow button darkened to the sketch's olive.
	private static readonly Color OffTint = new Color(0.4f, 0.4f, 0.4f, 1f);

	private static readonly Part[] PopupParts = { Part.MainHand, Part.Headgear, Part.OffHand };

	private static readonly string[] PartIcons = { "UI/WorldMapGear_MainHand", "UI/WorldMapGear_Headgear", "UI/WorldMapGear_OffHand" };

	private static int s_cached = -1;

	public static int Mask
	{
		get
		{
			if (s_cached < 0)
			{
				var prefs = DIContainerInfrastructure.GetPlayerPrefsService();
				s_cached = prefs != null ? prefs.GetInt(PrefsKey, DefaultMask) : DefaultMask;
				s_cached &= AllParts;
			}
			return s_cached;
		}
		set
		{
			s_cached = value & AllParts;
			var prefs = DIContainerInfrastructure.GetPlayerPrefsService();
			if (prefs != null)
			{
				prefs.SetInt(PrefsKey, s_cached);
			}
			RefreshBirds();
		}
	}

	public static bool Shows(Part part)
	{
		return (Mask & (int)part) != 0;
	}

	public static void Toggle(Part part)
	{
		Mask ^= (int)part;
	}

	// Most pieces on: select-all turns everything off (red cross); otherwise it turns everything on (green tick).
	public static bool MostAreOn
	{
		get
		{
			var on = 0;
			foreach (var part in PopupParts)
			{
				if (Shows(part))
				{
					on++;
				}
			}
			return on * 2 > PopupParts.Length;
		}
	}

	public static void ToggleAll()
	{
		Mask = MostAreOn ? 0 : AllParts;
	}

	// Whether this rig is one of the world map's own birds (the only ones the toggles apply to).
	public static bool AppliesTo(Component rig)
	{
		if (rig == null)
		{
			return false;
		}
		var view = rig.GetComponentInParent<CharacterControllerWorldMap>();
		return view != null && view.m_UsesWorldMapGear;
	}

	public static void RefreshBirds()
	{
		foreach (var view in FindObjectsOfType<CharacterControllerWorldMap>())
		{
			if (view.m_UsesWorldMapGear && view.m_AssetController != null)
			{
				view.m_AssetController.RefreshEquipment();
			}
		}
	}

	// --- options strip button + popup ---------------------------------------------------------------------------------

	private OptionsMgr m_Options;

	private Transform m_Opener;

	private Transform m_Body;

	private UIWidget m_BodyWidget;

	private Transform m_Tail;

	private UISprite[] m_PartBodies;

	private UITexture m_AllMark;

	private bool m_Open;

	// Everything the popup shows; it slides inside the popup's clipped panel (down behind the strip button to close).
	private Transform m_Content;

	private UIPanel m_Panel;

	private float m_SlideTime;

	private bool m_Sliding;

	// The strip's own slide, in UI units (AnimationClip/Panel_Open and Panel_Close: x -750 <-> 0 in 4 frames at
	// 24 fps). The popup moves along the same curves vertically at the same speed: it only covers the last part of the
	// way (its own height), so opening starts where the curve is that far out and closing stops there.
	private static readonly AnimationCurve OpenCurve = new AnimationCurve(
		new Keyframe(0f, -750f, 6613.7114f, 6613.7114f),
		new Keyframe(0.16666667f, 0f, 0f, 0f),
		new Keyframe(0.25f, 0f, 0f, 0f));

	private static readonly AnimationCurve CloseCurve = new AnimationCurve(
		new Keyframe(0f, 0f, 0f, 0f),
		new Keyframe(0.16666666f, -750f, -6764.9077f, -6764.9077f));

	// Clones the sound button into the strip's grid (sorted by name: after the targeting style button), widens the
	// strip by one cell and builds the (hidden) popup. World map only: the battle pause menu has the same strip.
	public static void Install(OptionsMgr options)
	{
		if (options == null || options.m_SoundButton == null || options.m_ButtonGrid == null || options.gameObject.scene.name != SceneName)
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
			var opener = CloneButton(template, grid, ButtonName, "UI/WorldMapGear_Bird");
			if (options.m_Background != null)
			{
				var body = options.m_Background.GetComponent<UISprite>();
				if (body != null)
				{
					body.width += Mathf.RoundToInt(options.m_ButtonGrid.cellWidth);
				}
			}
			options.m_ButtonGrid.Reposition();
			WidenStripClip(options, opener.transform);

			var stripPanel = UIPanel.Find(grid);
			var popupRoot = new GameObject(PopupName);
			popupRoot.layer = grid.gameObject.layer;
			// outside the strip's panel, which clips everything to the strip's height
			popupRoot.transform.parent = stripPanel != null && stripPanel.transform.parent != null ? stripPanel.transform.parent : grid.parent;
			popupRoot.transform.localPosition = Vector3.zero;
			popupRoot.transform.localRotation = Quaternion.identity;
			popupRoot.transform.localScale = Vector3.one;
			var panel = popupRoot.AddComponent<UIPanel>();
			panel.depth = (stripPanel != null ? stripPanel.depth : 0) + 5;
			var popup = popupRoot.AddComponent<WorldMapGear>();
			popup.m_Options = options;
			popup.m_Opener = opener.transform;
			popup.m_Panel = panel;
			var content = new GameObject("Content");
			content.layer = popupRoot.layer;
			content.transform.parent = popupRoot.transform;
			content.transform.localPosition = Vector3.zero;
			content.transform.localRotation = Quaternion.identity;
			content.transform.localScale = Vector3.one;
			popup.m_Content = content.transform;
			popup.Build(template, options.m_Background != null ? options.m_Background.GetComponent<UISprite>() : null);
			popup.SetOpen(false, true);

			var trigger = opener.GetComponentInChildren<UIInputTrigger>(true);
			if (trigger != null)
			{
				trigger.Clicked += () => popup.SetOpen(!popup.m_Open);
			}
		}
		catch (Exception ex)
		{
			DebugLog.Error("[WorldMapGear] Install failed: " + ex);
		}
	}

	private static GameObject CloneButton(Transform template, Transform parent, string name, string iconTexture)
	{
		var clone = Instantiate(template.gameObject, parent);
		clone.name = name;
		clone.transform.localScale = template.localScale;
		clone.transform.localRotation = template.localRotation;
		clone.transform.localPosition = template.localPosition;
		foreach (var sprite in clone.GetComponentsInChildren<UISprite>(true))
		{
			if (sprite.name != "Icon")
			{
				continue;
			}
			sprite.enabled = false;
			var icon = AddTexture(sprite.transform.parent, "WorldMapGearIcon", sprite.transform.localPosition, IconSize, IconSize, sprite.depth);
			icon.mainTexture = Resources.Load<Texture2D>(iconTexture);
			break;
		}
		return clone;
	}

	private static UITexture AddTexture(Transform parent, string name, Vector3 localPosition, int width, int height, int depth)
	{
		var go = new GameObject(name);
		go.layer = parent.gameObject.layer;
		go.transform.parent = parent;
		go.transform.localPosition = localPosition;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		var texture = go.AddComponent<UITexture>();
		texture.width = width;
		texture.height = height;
		texture.depth = depth;
		return texture;
	}

	// The strip's panel clips to a fixed width; with the extra buttons the last one could fall outside it.
	private static void WidenStripClip(OptionsMgr options, Transform lastButton)
	{
		var panel = UIPanel.Find(options.m_ButtonGrid.transform);
		if (panel == null || panel.clipping == UIDrawCall.Clipping.None)
		{
			return;
		}
		var right = 0f;
		foreach (Transform child in options.m_ButtonGrid.transform)
		{
			if (child.gameObject.activeSelf)
			{
				right = Mathf.Max(right, panel.transform.InverseTransformPoint(child.position).x + Cell / 2f + 8f);
			}
		}
		var region = panel.baseClipRegion;
		var clipRight = region.x + panel.clipOffset.x + region.z / 2f;
		if (right > clipRight)
		{
			var extra = right - clipRight;
			panel.baseClipRegion = new Vector4(region.x + extra / 2f, region.y, region.z + extra, region.w);
		}
	}

	private void Build(Transform template, UISprite stripBody)
	{
		var width = PopupParts.Length + 1;
		// body: a copy of the strip's background (same sprite and tint)
		if (stripBody != null)
		{
			var body = Instantiate(stripBody.gameObject, m_Content);
			body.name = "Body";
			foreach (Transform child in body.transform)
			{
				Destroy(child.gameObject);
			}
			var sprite = body.GetComponent<UISprite>();
			sprite.pivot = UIWidget.Pivot.Center;
			sprite.width = Mathf.RoundToInt(width * Cell + 2f * BodyPadding);
			body.transform.localPosition = Vector3.zero;
			body.transform.localScale = Vector3.one;
			m_Body = body.transform;
			m_BodyWidget = sprite;
			var tail = AddTexture(m_Content, "Tail", Vector3.zero, Mathf.RoundToInt(TailSize), Mathf.RoundToInt(TailSize), sprite.depth);
			tail.mainTexture = Resources.Load<Texture2D>("UI/WorldMapGear_Tail");
			tail.color = sprite.color;
			m_Tail = tail.transform;
		}
		m_PartBodies = new UISprite[PopupParts.Length];
		for (var i = 0; i < width; i++)
		{
			var isAll = i == PopupParts.Length;
			var button = CloneButton(template, m_Content, (i + 1) + (isAll ? "_All" : "_" + PopupParts[i]), isAll ? "UI/WorldMapGear_All" : PartIcons[i]);
			button.transform.localPosition = new Vector3((i - (width - 1) / 2f) * Cell, 0f, 0f);
			var trigger = button.GetComponentInChildren<UIInputTrigger>(true);
			if (isAll)
			{
				var icon = button.GetComponentInChildren<UITexture>(true);
				m_AllMark = AddTexture(icon.transform.parent, "Mark", icon.transform.localPosition + new Vector3(22f, 22f, -1f), MarkSize, MarkSize, icon.depth + 2);
				if (trigger != null)
				{
					trigger.Clicked += () =>
					{
						ToggleAll();
						ShowState();
					};
				}
				continue;
			}
			foreach (var sprite in button.GetComponentsInChildren<UISprite>(true))
			{
				if (sprite.name == "Body")
				{
					m_PartBodies[i] = sprite;
				}
			}
			var part = PopupParts[i];
			if (trigger != null)
			{
				trigger.Clicked += () =>
				{
					Toggle(part);
					ShowState();
				};
			}
		}
		ShowState();
	}

	private void ShowState()
	{
		for (var i = 0; i < PopupParts.Length; i++)
		{
			if (m_PartBodies[i] != null)
			{
				m_PartBodies[i].color = Shows(PopupParts[i]) ? Color.white : OffTint;
			}
		}
		if (m_AllMark != null)
		{
			m_AllMark.mainTexture = Resources.Load<Texture2D>(MostAreOn ? "UI/WorldMapGear_Cross" : "UI/WorldMapGear_Check");
		}
	}

	private void SetOpen(bool open, bool instant = false)
	{
		if (open == m_Open && !instant)
		{
			return;
		}
		m_Open = open;
		if (instant)
		{
			m_Sliding = false;
			m_Content.gameObject.SetActive(open);
			SetSlide(open ? 0f : -SlideDistance);
			return;
		}
		// reversing mid-slide starts the new curve from where the content is (the curve's matching point)
		var from = m_Sliding ? m_Content.localPosition.y : (open ? -SlideDistance : 0f);
		m_SlideTime = TimeOnCurve(open ? OpenCurve : CloseCurve, from, open);
		m_Sliding = true;
		if (open)
		{
			m_Content.gameObject.SetActive(true);
			ShowState();
			Follow();
			SetSlide(from);
		}
	}

	private static float TimeOnCurve(AnimationCurve curve, float value, bool rising)
	{
		var end = curve.keys[curve.length - 1].time;
		for (var t = 0f; t < end; t += 1f / 240f)
		{
			var v = curve.Evaluate(t);
			if (rising ? v >= value : v <= value)
			{
				return t;
			}
		}
		return end;
	}

	private void LateUpdate()
	{
		if (!m_Open && !m_Sliding)
		{
			return;
		}
		// closes with the strip
		if (m_Open && (m_Options == null || !m_Options.m_Open || m_Opener == null || !m_Opener.gameObject.activeInHierarchy))
		{
			SetOpen(false, m_Opener == null || !m_Opener.gameObject.activeInHierarchy);
		}
		Follow();
		if (!m_Sliding)
		{
			return;
		}
		var curve = m_Open ? OpenCurve : CloseCurve;
		m_SlideTime += Time.deltaTime;
		var end = curve.keys[curve.length - 1].time;
		var value = curve.Evaluate(Mathf.Min(m_SlideTime, end));
		SetSlide(value);
		// closing is done once the popup is out of sight, long before the strip's curve ends
		if (m_SlideTime >= end || (!m_Open && value <= -SlideDistance))
		{
			m_Sliding = false;
			if (!m_Open)
			{
				m_Content.gameObject.SetActive(false);
			}
		}
	}

	// UI units below the resting place; -SlideDistance = all the way down behind the clip's bottom edge (the strip's top).
	private void SetSlide(float offset)
	{
		m_Content.localPosition = new Vector3(0f, Mathf.Round(Mathf.Max(offset, -SlideDistance)), 0f);
	}

	private float BodyHeight
	{
		get { return m_BodyWidget != null ? m_BodyWidget.height : Cell; }
	}

	// Top of the content (buttons overhang the body; the select-all mark sits higher still) and the tail's tip.
	private float ContentTop
	{
		get { return Mathf.Max(BodyHeight / 2f, Cell / 2f + 6f); }
	}

	private float ContentBottom
	{
		get { return -BodyHeight / 2f - TailSize * 0.75f + 1f; }
	}

	private float SlideDistance
	{
		get { return ContentTop - ContentBottom + 4f; }
	}

	// Clips the panel to the popup's resting area, whose bottom edge (the tail's tip) is the strip's top edge: sliding
	// down, the popup disappears into the strip.
	private void SetClip()
	{
		if (m_Panel == null)
		{
			return;
		}
		var halfWidth = ((PopupParts.Length + 1) * Cell + 2f * BodyPadding) / 2f + Cell;
		var top = ContentTop + 8f;
		var bottom = ContentBottom;
		m_Panel.clipping = UIDrawCall.Clipping.SoftClip;
		m_Panel.clipSoftness = new Vector2(1f, 1f);
		m_Panel.clipOffset = Vector2.zero;
		m_Panel.baseClipRegion = new Vector4(0f, (top + bottom) / 2f, 2f * halfWidth, top - bottom);
	}

	// Sits above the strip button, its tail pointing at it; slid right when it would stick out left of the strip.
	private void Follow()
	{
		var parent = transform.parent;
		if (parent == null || m_Opener == null)
		{
			return;
		}
		var opener = parent.InverseTransformPoint(m_Opener.position);
		var bodyHeight = BodyHeight;
		var halfWidth = ((PopupParts.Length + 1) * Cell + 2f * BodyPadding) / 2f;
		var x = opener.x;
		if (m_Options.m_Background != null)
		{
			var stripLeft = parent.InverseTransformPoint(m_Options.m_Background.transform.position).x;
			x = Mathf.Max(x, stripLeft + halfWidth);
		}
		// tail tip on the strip body's top edge (the 84 buttons overhang the 76 high body by a few units)
		var stripTop = opener.y + Cell / 2f + GapAboveButton;
		var strip = m_Options.m_Background != null ? m_Options.m_Background.GetComponent<UIWidget>() : null;
		if (strip != null)
		{
			var c = strip.worldCorners;
			stripTop = Mathf.Max(parent.InverseTransformPoint(c[1]).y, parent.InverseTransformPoint(c[2]).y);
		}
		var y = stripTop - ContentBottom;
		transform.localPosition = new Vector3(Mathf.Round(x), Mathf.Round(y), opener.z);
		SetClip();
		if (m_Tail != null)
		{
			// the tail texture's triangle fills its top three quarters; its top edge meets the body's bottom edge
			m_Tail.localPosition = new Vector3(Mathf.Round(opener.x - x), -bodyHeight / 2f - TailSize / 2f + 1f, 0f);
		}
	}
}
