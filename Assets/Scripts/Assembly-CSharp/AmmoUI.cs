using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Battle;
using ABH.GameDatas.Interfaces;
using SmoothMoves;
using UnityEngine;

// Battle UI for Ammo: the ammo icon, the "[icon] xN" tag and ammo bar on skill name plates, the regen charge bar in
// the character overlay and the "no ammo" mark under the blocked icon while dragging.
public static class AmmoUI
{
	// Where an ammo picture comes from: a texture region (projectiles live in SmoothMoves atlases) or an NGUI sprite.
	public class IconSource
	{
		public Texture Texture;

		public Rect Uv;

		public UIAtlas Atlas;

		public string SpriteName;

		public Vector2 Size;
	}

	// Empty share of an ammo bar has this much less saturation and this much more value (absolute, HSV 0..1) than the
	// plate's own colour, which is a full bar. Every lap of overflow adds LapSaturation saturation and takes LapDarkness off the value.
	public const float SaturationStep = 0.32f;

	public const float EmptyLightness = 0.1f;

	public const float LapSaturation = 0.2f;

	public const float LapDarkness = 0.1f;

	// The regen charge bar under the "+X [ammo]" tag in the character overlay
	public static bool ShowRegenChargeBar = false;

	private const string SkillTagName = "AmmoTag";

	private const string SkillFillName = "AmmoFill";

	private const string RegenRootName = "AmmoRegen";

	private const string ChargeDotsName = "AmmoChargeDots";

	// Height of the dotted charge line (one dot's cell), and how far it sits from the plate's middle
	public const int ChargeDotSize = 12;

	private const float ChargeDotsOffsetY = -13f;

	private static readonly Color ChargeDotColor = new Color(1f, 0.88f, 0.1f);

	private static Texture2D s_ChargeDotTexture;

	private static readonly Dictionary<string, IconSource> s_IconCache = new Dictionary<string, IconSource>();

	private static UIAtlas s_GenericAtlas;

	// The ammo is what the combatant's weapon fires: the main hand projectile for ranged weapons, otherwise the icon of
	// the attack that uses it.
	public static IconSource GetIconSource(ICombatant combatant)
	{
		if (combatant == null)
		{
			return null;
		}
		var skills = combatant.GetSkills() ?? new List<SkillBattleDataBase>();
		var skill = skills.FirstOrDefault(s => Ammo.IsAmmoSkill(combatant, s));
		var mainHand = combatant.CombatantMainHandEquipment;
		if (mainHand != null && mainHand.BalancingData != null && mainHand.BalancingData.IsRanged)
		{
			// What it actually shot, else what its weapon fires
			var source = GetProjectileIcon(Ammo.GetFiredProjectile(combatant) ?? mainHand.ProjectileAssetName);
			if (source != null)
			{
				return source;
			}
		}
		return skill != null ? GetSkillIcon(skill) : null;
	}

	public static IconSource GetProjectileIcon(string projectileAssetId)
	{
		if (string.IsNullOrEmpty(projectileAssetId))
		{
			return null;
		}
		IconSource cached;
		if (s_IconCache.TryGetValue(projectileAssetId, out cached) && (cached.Texture != null || cached.Atlas != null))
		{
			return cached;
		}
		var provider = DIContainerInfrastructure.ProjectileAssetProvider;
		var prefab = provider != null && provider.ContainsAsset(projectileAssetId) ? provider.GetObject(projectileAssetId) as GameObject : null;
		var source = prefab != null ? GetIconFromSprites(prefab) : null;
		if (source != null)
		{
			s_IconCache[projectileAssetId] = source;
		}
		return source;
	}

	public static IconSource GetIconFromSprites(GameObject root)
	{
		foreach (var sprite in root.GetComponentsInChildren<CHMeshSprite>(true))
		{
			if (string.IsNullOrEmpty(sprite.m_SpriteName))
			{
				continue;
			}
			if (sprite.m_AtlasType == AtlasTypes.SmoothMoves && sprite.m_SmoothMovesAtlas != null && sprite.m_SmoothMovesAtlas.material != null)
			{
				var atlas = sprite.m_SmoothMovesAtlas;
				var index = atlas.GetTextureIndex(atlas.GetTextureGUIDFromName(sprite.m_SpriteName));
				if (index < 0 || atlas.material.mainTexture == null)
				{
					continue;
				}
				return new IconSource
				{
					Texture = atlas.material.mainTexture,
					Uv = atlas.uvs[index],
					Size = new Vector2(sprite.m_Width > 0 ? sprite.m_Width : atlas.textureSizes[index].x, sprite.m_Height > 0 ? sprite.m_Height : atlas.textureSizes[index].y)
				};
			}
			if (sprite.m_AtlasType == AtlasTypes.Ngui && sprite.m_NguiAtlas != null && sprite.m_NguiAtlas.GetSprite(sprite.m_SpriteName) != null)
			{
				var data = sprite.m_NguiAtlas.GetSprite(sprite.m_SpriteName);
				return new IconSource
				{
					Atlas = sprite.m_NguiAtlas,
					SpriteName = sprite.m_SpriteName,
					Size = new Vector2(data.width, data.height)
				};
			}
		}
		return null;
	}

	private static IconSource GetSkillIcon(SkillBattleDataBase skill)
	{
		var provider = DIContainerInfrastructure.GetGenericIconAtlasAssetProvider();
		var atlasId = skill.Model.Balancing.IconAtlasId;
		var atlasObject = provider != null && provider.ContainsAsset(atlasId) ? provider.GetObject(atlasId) as GameObject : null;
		var atlas = atlasObject != null ? atlasObject.GetComponent<UIAtlas>() : null;
		var data = atlas != null ? atlas.GetSprite(skill.Model.m_SkillIconName) : null;
		if (data == null)
		{
			return null;
		}
		return new IconSource
		{
			Atlas = atlas,
			SpriteName = skill.Model.m_SkillIconName,
			Size = new Vector2(data.width, data.height)
		};
	}

	// An ammo picture fitting in a box x box square. Long thin pictures (arrows) are laid diagonally, pointing up-right.
	public static UIWidget CreateIcon(Transform parent, IconSource source, float box, int depth, string name = "AmmoIcon")
	{
		if (source == null || parent == null)
		{
			return null;
		}
		var go = new GameObject(name);
		go.layer = parent.gameObject.layer;
		go.transform.parent = parent;
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		UIWidget widget;
		if (source.Texture != null)
		{
			var texture = go.AddComponent<UITexture>();
			texture.mainTexture = source.Texture;
			texture.uvRect = source.Uv;
			widget = texture;
		}
		else
		{
			var sprite = go.AddComponent<UISprite>();
			sprite.atlas = source.Atlas;
			sprite.spriteName = source.SpriteName;
			widget = sprite;
		}
		widget.depth = depth;
		var size = new Vector2(Mathf.Max(1f, source.Size.x), Mathf.Max(1f, source.Size.y));
		var diagonal = Mathf.Max(size.x, size.y) / Mathf.Min(size.x, size.y) > 2f;
		var scale = diagonal ? box / ((size.x + size.y) / Mathf.Sqrt(2f)) : box / Mathf.Max(size.x, size.y);
		widget.width = Mathf.Max(2, Mathf.RoundToInt(size.x * scale));
		widget.height = Mathf.Max(2, Mathf.RoundToInt(size.y * scale));
		if (diagonal)
		{
			go.transform.localRotation = Quaternion.Euler(0f, 0f, size.x >= size.y ? 45f : -45f);
		}
		return widget;
	}

	public static UILabel CreateLabel(Transform parent, UILabel style, string text, Color color, UIWidget.Pivot pivot, int fontSize, int depth, string name = "AmmoLabel")
	{
		var go = new GameObject(name);
		go.layer = parent.gameObject.layer;
		go.transform.parent = parent;
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		var label = go.AddComponent<UILabel>();
		// rawPivot: the pivot setter moves the widget to keep it in place
		label.rawPivot = pivot;
		if (style != null)
		{
			if (style.bitmapFont != null)
			{
				label.bitmapFont = style.bitmapFont;
			}
			else
			{
				label.trueTypeFont = style.trueTypeFont;
			}
			label.fontStyle = style.fontStyle;
			label.effectStyle = style.effectStyle;
			label.effectColor = style.effectColor;
			label.effectDistance = style.effectDistance;
		}
		label.fontSize = fontSize;
		label.overflowMethod = UILabel.Overflow.ResizeFreely;
		label.supportEncoding = false;
		label.color = color;
		label.depth = depth;
		label.text = text;
		label.MakePixelPerfect();
		return label;
	}

	public static Color ShiftColor(Color color, float saturation, float value)
	{
		float h, s, v;
		Color.RGBToHSV(color, out h, out s, out v);
		var result = Color.HSVToRGB(h, Mathf.Clamp01(s + saturation), Mathf.Clamp01(v + value));
		result.a = color.a;
		return result;
	}

	// Bar lap n: 0 is empty, 1 the plate's own colour (a full bar), each further lap more saturated and darker.
	public static Color LapColor(Color baseColor, int lap)
	{
		if (lap <= 0)
		{
			return ShiftColor(baseColor, -SaturationStep, EmptyLightness);
		}
		return ShiftColor(baseColor, LapSaturation * (lap - 1), -LapDarkness * (lap - 1));
	}

	// Skill name plate: "[icon] xN" (ammo left) on its right end for skills that use ammo ("[icon] +N" for skills that give some),
	// and the plate's colour strip turned into a bar of the user's ammo (full = the ammo it started the battle with).
	public static void ShowOnSkillPlate(UISprite plate, UILabel style, SkillBattleDataBase skill, ICombatant invoker)
	{
		if (plate == null)
		{
			return;
		}
		var header = plate.cachedTransform.parent;
		ClearSkillPlate(header);
		var state = Ammo.GetState(invoker);
		var gain = Ammo.GetGain(invoker, skill);
		if (gain > 0)
		{
			ShowOnSkillPlate(plate, style, 0, gain, state.Current, state.Start, GetIconSource(invoker), invoker);
			return;
		}
		// Otherwise only the attacks that use the weapon's ammo get a tag
		if (!Ammo.UsesAmmo(invoker, skill) || state == null)
		{
			return;
		}
		ShowOnSkillPlate(plate, style, 1, 0, state.Current, state.Start, GetIconSource(invoker), invoker);
		if (state.MaxCharges > 0)
		{
			ShowChargeDots(plate, state.Charges, state.MaxCharges, invoker);
		}
	}

	// A dotted yellow line along the plate from its left edge, as far right as the weapon is charged.
	// user: whose charge it follows live (null keeps it at charges)
	public static void ShowChargeDots(UISprite plate, int charges, int maxCharges, ICombatant user = null)
	{
		var header = plate.cachedTransform.parent;
		DestroyChild(header, ChargeDotsName);
		var go = new GameObject(ChargeDotsName);
		go.layer = header.gameObject.layer;
		go.transform.parent = header;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		var dots = go.AddComponent<UITexture>();
		dots.mainTexture = GetChargeDotTexture();
		dots.rawPivot = UIWidget.Pivot.Left;
		dots.depth = plate.depth + 2;
		dots.height = ChargeDotSize;
		dots.color = ChargeDotColor;
		var platePos = plate.cachedTransform.localPosition;
		var pivot = plate.pivotOffset;
		go.transform.localPosition = new Vector3(platePos.x - pivot.x * plate.width, platePos.y + (0.5f - pivot.y) * plate.height + ChargeDotsOffsetY, platePos.z);
		go.AddComponent<AmmoChargeDots>().Init(dots, plate.width, charges, maxCharges, user);
	}

	private static Texture2D GetChargeDotTexture()
	{
		if (s_ChargeDotTexture != null)
		{
			return s_ChargeDotTexture;
		}
		// One round dot with a gap, repeated along the line
		const int size = 32;
		s_ChargeDotTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
		s_ChargeDotTexture.name = "AmmoChargeDot";
		s_ChargeDotTexture.wrapMode = TextureWrapMode.Repeat;
		s_ChargeDotTexture.hideFlags = HideFlags.DontSave;
		var center = (size - 1) * 0.5f;
		var radius = size * 0.32f;
		for (var y = 0; y < size; y++)
		{
			for (var x = 0; x < size; x++)
			{
				var distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
				s_ChargeDotTexture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance + 0.5f)));
			}
		}
		s_ChargeDotTexture.Apply();
		return s_ChargeDotTexture;
	}

	// user: whose ammo the bar follows live (null keeps it at current)
	public static void ShowOnSkillPlate(UISprite plate, UILabel style, int cost, int gain, int current, int start, IconSource icon, ICombatant user = null)
	{
		var header = plate.cachedTransform.parent;
		ClearSkillPlate(header);
		if (cost <= 0 && gain <= 0)
		{
			return;
		}
		var tag = BuildPlateTag(header, plate, style, cost > 0 ? "x" + current : "+" + gain, icon);
		if (cost > 0 && start > 0)
		{
			var bar = header.GetComponent<AmmoPlateBar>() ?? header.gameObject.AddComponent<AmmoPlateBar>();
			bar.Init(plate, BuildPlateFill(header, plate), tag, start, current, user);
		}
	}

	// The plate's colour is set by SkillBlind before this runs; nothing to restore but our own widgets.
	public static void ClearSkillPlate(Transform header)
	{
		if (header == null)
		{
			return;
		}
		var bar = header.GetComponent<AmmoPlateBar>();
		if (bar != null)
		{
			bar.Stop();
		}
		DestroyChild(header, SkillTagName);
		DestroyChild(header, SkillFillName);
		DestroyChild(header, ChargeDotsName);
	}

	// The left part of the plate, recoloured one lap up (AmmoPlateBar sets its width and colour)
	public static UISprite BuildPlateFill(Transform header, UISprite plate)
	{
		var go = new GameObject(SkillFillName);
		go.layer = header.gameObject.layer;
		go.transform.parent = header;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		var fill = go.AddComponent<UISprite>();
		fill.atlas = plate.atlas;
		fill.spriteName = plate.spriteName;
		fill.type = plate.type;
		fill.rawPivot = UIWidget.Pivot.Left;
		fill.depth = plate.depth + 1;
		fill.height = plate.height;
		fill.width = 2;
		fill.enabled = false;
		var platePos = plate.cachedTransform.localPosition;
		var pivot = plate.pivotOffset;
		go.transform.localPosition = new Vector3(platePos.x - pivot.x * plate.width, platePos.y + (0.5f - pivot.y) * plate.height, platePos.z);
		return fill;
	}

	public static Transform BuildPlateTag(Transform header, UISprite plate, UILabel style, string text, IconSource icon)
	{
		var root = new GameObject(SkillTagName).transform;
		root.gameObject.layer = header.gameObject.layer;
		root.parent = header;
		root.localRotation = Quaternion.identity;
		root.localScale = Vector3.one;
		var platePos = plate.cachedTransform.localPosition;
		var pivot = plate.pivotOffset;
		var right = platePos.x + (1f - pivot.x) * plate.width - 10f;
		root.localPosition = new Vector3(right, platePos.y + (0.5f - pivot.y) * plate.height, -5f);
		var depth = style != null ? style.depth : plate.depth + 10;
		var label = CreateLabel(root, style, text, style != null ? style.color : Color.black, UIWidget.Pivot.Right, style != null ? style.fontSize : 30, depth);
		label.cachedTransform.localPosition = new Vector3(0f, 2f, 0f);
		var box = plate.height - 6f;
		var widget = CreateIcon(root, icon, box, depth - 1);
		if (widget != null)
		{
			widget.cachedTransform.localPosition = new Vector3(-label.printedSize.x - 4f - box * 0.5f, 0f, 0f);
		}
		return root;
	}

	// New text for a plate tag, keeping the icon just left of it
	public static void SetPlateTagText(Transform tag, string text)
	{
		if (tag == null)
		{
			return;
		}
		var label = tag.GetComponentInChildren<UILabel>(true);
		if (label == null || label.text == text)
		{
			return;
		}
		label.text = text;
		var icon = tag.Find("AmmoIcon");
		if (icon != null)
		{
			var widget = icon.GetComponent<UIWidget>();
			var box = widget != null ? Mathf.Max(widget.width, widget.height) : 0f;
			if (icon.localRotation != Quaternion.identity && widget != null)
			{
				box = (widget.width + widget.height) / Mathf.Sqrt(2f);
			}
			icon.localPosition = new Vector3(-label.printedSize.x - 4f - box * 0.5f, 0f, 0f);
		}
	}

	// Character overlay: "[+X icon]" over a small charge bar, one segment per turn, filling up to the next regen,
	// between the health bar and the power level. template is a skill blind's charge bar (Body + Fill + spacers).
	public static void ShowRegenBar(Transform row, UISprite healthFill, UILabel powerLabel, UILabel style, GameObject template, ICombatant combatant, ref Vector3? powerLabelHome)
	{
		var state = Ammo.HasRegen(combatant) ? Ammo.GetState(combatant) : null;
		ShowRegenBar(row, healthFill, powerLabel, style, template, state != null ? state.Regen : 0, state != null ? state.RegenProgress : 0, state != null ? state.RegenTurns : 0, state != null ? GetIconSource(combatant) : null, ref powerLabelHome, combatant);
	}

	public static void ShowRegenBar(Transform row, UISprite healthFill, UILabel powerLabel, UILabel style, GameObject template, int regen, int progress, int turns, IconSource icon, ref Vector3? powerLabelHome, ICombatant user = null)
	{
		if (row == null)
		{
			return;
		}
		DestroyChild(row, RegenRootName);
		if (powerLabel != null)
		{
			if (powerLabelHome.HasValue)
			{
				powerLabel.cachedTransform.localPosition = powerLabelHome.Value;
			}
			else
			{
				powerLabelHome = powerLabel.cachedTransform.localPosition;
			}
		}
		if (regen <= 0 || turns <= 0 || template == null || healthFill == null)
		{
			return;
		}
		var body = healthFill.cachedTransform.parent != null ? healthFill.cachedTransform.parent.GetComponentsInChildren<UISprite>(true).OrderByDescending(s => s.width).First() : healthFill;
		var healthRight = row.InverseTransformPoint(body.worldCorners[2]).x;
		var space = 72f;
		if (powerLabel != null && powerLabel.gameObject.activeSelf)
		{
			var powerLeft = row.InverseTransformPoint(powerLabel.worldCorners[0]).x;
			var shortBy = healthRight + space - powerLeft;
			if (shortBy > 0f)
			{
				// Make room by moving the power text right (the paper has some margin there)
				var shift = Mathf.Min(shortBy, 30f);
				powerLabel.cachedTransform.localPosition += new Vector3(shift, 0f, 0f);
				powerLeft += shift;
			}
			space = Mathf.Max(40f, powerLeft - healthRight);
		}
		var root = new GameObject(RegenRootName).transform;
		root.gameObject.layer = row.gameObject.layer;
		root.parent = row;
		root.localRotation = Quaternion.identity;
		root.localScale = Vector3.one;
		root.localPosition = new Vector3(healthRight + space * 0.5f, 0f, 0f);
		var barWidth = Mathf.RoundToInt(Mathf.Min(56f, space - 14f));
		var bar = Object.Instantiate(template, root, false);
		bar.name = "Bar";
		bar.SetActive(ShowRegenChargeBar);
		bar.transform.localPosition = new Vector3(0f, -7f, 0f);
		UISprite barBody = null;
		UISprite barFill = null;
		UISprite spacer = null;
		foreach (Transform child in bar.transform)
		{
			var sprite = child.GetComponent<UISprite>();
			if (sprite != null && child.name == "Body")
			{
				barBody = sprite;
			}
			else if (sprite != null && child.name == "Fill")
			{
				barFill = sprite;
			}
			else
			{
				if (spacer == null)
				{
					spacer = child.GetComponentsInChildren<UISprite>(true).FirstOrDefault();
				}
				child.gameObject.SetActive(false);
			}
		}
		if (barBody != null)
		{
			barBody.width = barWidth;
		}
		if (barFill != null)
		{
			barFill.width = barWidth - 4;
			barFill.fillAmount = Mathf.Clamp01((float)progress / turns);
			if (spacer != null)
			{
				var fillPos = barFill.cachedTransform.localPosition;
				var left = fillPos.x - barFill.pivotOffset.x * barFill.width;
				for (var k = 1; k < turns; k++)
				{
					var divider = Object.Instantiate(spacer.gameObject, bar.transform, false);
					divider.name = "Segment_" + k;
					divider.SetActive(true);
					divider.GetComponent<UISprite>().height = barFill.height;
					divider.transform.localPosition = new Vector3(Mathf.Round(left + barFill.width * k / (float)turns), fillPos.y, spacer.cachedTransform.localPosition.z);
				}
			}
			bar.AddComponent<AmmoRegenBar>().Init(barFill, progress, turns, user);
		}
		var tag = new GameObject("Tag").transform;
		tag.gameObject.layer = root.gameObject.layer;
		tag.parent = root;
		tag.localRotation = Quaternion.identity;
		tag.localScale = Vector3.one;
		var depth = style != null ? style.depth : 10;
		var color = style != null ? style.color : new Color(0.353f, 0.208f, 0.104f);
		var label = CreateLabel(tag, style, "+" + regen, color, UIWidget.Pivot.Left, 20, depth);
		const float iconBox = 22f;
		var width = label.printedSize.x + 2f + iconBox;
		label.cachedTransform.localPosition = new Vector3(0f, 1f, 0f);
		var widget = CreateIcon(tag, icon, iconBox, depth - 1);
		if (widget != null)
		{
			widget.cachedTransform.localPosition = new Vector3(label.printedSize.x + 2f + iconBox * 0.5f, 0f, 0f);
		}
		tag.localPosition = new Vector3(-width * 0.5f, ShowRegenChargeBar ? 10f : 0f, 0f);
	}

	// Ammo picture under (behind) a CharacterInteractionBlocked mark, for targets the dragged skill can't afford.
	public static void ShowNoAmmo(GameObject blockedItem, ICombatant user)
	{
		if (blockedItem == null)
		{
			return;
		}
		var mark = blockedItem.GetComponent<AmmoBlockedMark>() ?? blockedItem.AddComponent<AmmoBlockedMark>();
		mark.Show(user);
	}

	public static UIAtlas GetGenericAtlas()
	{
		if (s_GenericAtlas == null)
		{
			s_GenericAtlas = Resources.FindObjectsOfTypeAll<UIAtlas>().FirstOrDefault(a => a.name == "GenericElements" && a.GetSprite("UpgradeArrow") != null);
		}
		if (s_GenericAtlas == null)
		{
			var loaded = Resources.Load("Content/0_Generic/2_Interface/Skins/GenericElements") as GameObject;
			s_GenericAtlas = loaded != null ? loaded.GetComponent<UIAtlas>() : null;
		}
		return s_GenericAtlas;
	}

	private static void DestroyChild(Transform parent, string name)
	{
		var child = parent.Find(name);
		while (child != null)
		{
			// Renamed first so Find doesn't return it again before Destroy runs
			child.name = name + "_Destroyed";
			child.gameObject.SetActive(false);
			if (Application.isPlaying)
			{
				Object.Destroy(child.gameObject);
			}
			else
			{
				Object.DestroyImmediate(child.gameObject);
			}
			child = parent.Find(name);
		}
	}
}

// Keeps the ammo picture behind a blocked mark. The marks are shared with other blocks (consumables, taunts),
// so the picture hides whenever its mark is put away and has to be asked for again.
public class AmmoBlockedMark : MonoBehaviour
{
	private UIWidget m_Icon;

	private ICombatant m_User;

	private string m_Projectile;

	public void Show(ICombatant user)
	{
		var projectile = ABH.GameDatas.Battle.Ammo.GetFiredProjectile(user);
		if (m_Icon == null || m_User != user || m_Projectile != projectile)
		{
			m_Projectile = projectile;
			if (m_Icon != null)
			{
				Destroy(m_Icon.gameObject);
				m_Icon = null;
			}
			m_User = user;
			var blocked = GetComponentInChildren<UISprite>(true);
			var parent = blocked != null ? blocked.cachedTransform : transform;
			var box = blocked != null ? Mathf.Min(blocked.width, blocked.height) * 0.62f : 64f;
			m_Icon = AmmoUI.CreateIcon(parent, AmmoUI.GetIconSource(user), box, blocked != null ? blocked.depth - 1 : 0, "NoAmmoIcon");
		}
		if (m_Icon != null)
		{
			m_Icon.alpha = 1f;
		}
	}

	private void OnDisable()
	{
		if (m_Icon != null)
		{
			m_Icon.alpha = 0f;
		}
	}
}

// A skill plate's ammo bar and "xN" tag, following its user's ammo as it changes.
public class AmmoPlateBar : MonoBehaviour
{
	private UISprite m_Plate;

	private UISprite m_Fill;

	private Color m_BaseColor;

	private int m_Start;

	private Transform m_Tag;

	private int m_Shown;

	private ICombatant m_User;

	public void Init(UISprite plate, UISprite fill, Transform tag, int start, int current, ICombatant user)
	{
		m_Plate = plate;
		m_Fill = fill;
		m_Tag = tag;
		m_BaseColor = plate.color;
		m_Start = start;
		m_Shown = current;
		m_User = user;
		enabled = true;
		Apply();
	}

	public void Stop()
	{
		m_User = null;
		m_Fill = null;
		m_Tag = null;
		enabled = false;
	}

	private void Update()
	{
		var state = Ammo.GetState(m_User);
		if (state == null || m_Plate == null || m_Shown == state.Current)
		{
			return;
		}
		m_Shown = state.Current;
		AmmoUI.SetPlateTagText(m_Tag, "x" + m_Shown);
		Apply();
	}

	private void Apply()
	{
		if (m_Plate == null || m_Start <= 0)
		{
			return;
		}
		var laps = m_Shown / m_Start;
		var rest = (float)(m_Shown % m_Start) / m_Start;
		m_Plate.color = AmmoUI.LapColor(m_BaseColor, laps);
		if (m_Fill == null)
		{
			return;
		}
		var width = Mathf.RoundToInt(m_Plate.width * rest);
		m_Fill.enabled = width >= 2;
		m_Fill.width = Mathf.Max(2, width);
		m_Fill.color = AmmoUI.LapColor(m_BaseColor, laps + 1);
	}
}

// A skill plate's dotted charge line, following its user's charges as they change.
public class AmmoChargeDots : MonoBehaviour
{
	private UITexture m_Dots;

	private int m_PlateWidth;

	private int m_MaxCharges;

	private int m_Shown;

	private ICombatant m_User;

	public void Init(UITexture dots, int plateWidth, int charges, int maxCharges, ICombatant user)
	{
		m_Dots = dots;
		m_PlateWidth = plateWidth;
		m_MaxCharges = Mathf.Max(1, maxCharges);
		m_Shown = charges;
		m_User = user;
		Apply();
	}

	private void Update()
	{
		var state = Ammo.GetState(m_User);
		if (state != null && m_Shown != state.Charges)
		{
			m_Shown = state.Charges;
			Apply();
		}
	}

	private void Apply()
	{
		var width = Mathf.RoundToInt(m_PlateWidth * Mathf.Clamp01((float)m_Shown / m_MaxCharges));
		m_Dots.enabled = width > 0;
		if (width > 0)
		{
			m_Dots.width = Mathf.Max(2, width);
			m_Dots.uvRect = new Rect(0f, 0f, (float)width / AmmoUI.ChargeDotSize, 1f);
		}
	}
}

// The regen charge bar, following its user's progress.
public class AmmoRegenBar : MonoBehaviour
{
	private UISprite m_Fill;

	private int m_Turns;

	private float m_Shown;

	private ICombatant m_User;

	public void Init(UISprite fill, int progress, int turns, ICombatant user)
	{
		m_Fill = fill;
		m_Turns = turns;
		m_Shown = (float)progress / turns;
		m_User = user;
		m_Fill.fillAmount = m_Shown;
	}

	private void Update()
	{
		var state = Ammo.GetState(m_User);
		if (state == null || m_Fill == null || m_Turns <= 0)
		{
			return;
		}
		var target = (float)state.RegenProgress / m_Turns;
		if (m_Shown != target)
		{
			m_Shown = target;
			m_Fill.fillAmount = m_Shown;
		}
	}
}
