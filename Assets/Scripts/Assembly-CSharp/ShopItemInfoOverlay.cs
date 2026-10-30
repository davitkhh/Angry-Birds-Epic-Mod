using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

// Tap-and-hold overlay for gear and blueprints in the shops. It is a runtime copy of InfoOverlay_Equipment that keeps
// the parchment frame, title label and enter/leave animation, and lays out a ShopItemInfo in its place:
//   name on top; stats with one row per star count on the left; perks on the right with the recipe under them.
// The frame pieces are resized around the content.
public class ShopItemInfoOverlay : MonoBehaviour
{
	private const float Padding = 30f;

	private const float TitleGap = 12f;

	private const float ColumnGap = 40f;

	private const float StatRowHeight = 46f;

	private const float StarSize = 26f;

	private const float StarStep = 21f;

	private const float StatIconSize = 40f;

	private const float PerkIconSize = 44f;

	private const float PerkHeaderHeight = 52f;

	private const float ValueIconSize = 36f;

	private const float ValueRowHeight = 40f;

	private const float ValueIndent = 58f;

	private const float PerkGap = 8f;

	private const float RecipeIconSize = 48f;

	private const float CellGap = 18f;

	private const float MaxPerkWidth = 360f;

	private const float AnchorOffset = 50f;

	// Frame piece sizes (InfoOverlay_TL/T/TR... in GenericElements)
	private const float FrameLeft = 122f;

	private const float FrameRight = 100f;

	private const float FrameTop = 60f;

	private const float FrameBottom = 44f;

	private Transform m_Bubble;

	private Transform m_Content;

	private UILabel m_Title;

	private UILabel m_TextStyle;

	private UILabel m_SmallStyle;

	private UIAtlas m_GenericAtlas;

	private readonly Dictionary<string, UISprite> m_Frame = new Dictionary<string, UISprite>();

	private ContainerControl m_BubbleContainer;

	private ContainerControl m_AllOverlaysContainerControl;

	private Camera m_InterfaceCamera;

	private Vector2 m_Size;

	private readonly Dictionary<string, UIAtlas> m_Atlases = new Dictionary<string, UIAtlas>();

	// Looks up icon atlases by name; the editor preview loads them from the project instead.
	public Func<string, UIAtlas> AtlasResolver;

	public Vector2 Size
	{
		get
		{
			return m_Size;
		}
	}

	public static ShopItemInfoOverlay Create(EquipmentOverlay source, Camera interfaceCamera)
	{
		var copy = Instantiate(source.gameObject, source.transform.parent);
		copy.name = "InfoOverlay_ShopItem";
		copy.SetActive(false);
		DestroyImmediate(copy.GetComponent<EquipmentOverlay>());
		// Own panel, raised above the shop window's panels on every show (the InfoOverlays panel draws under the
		// world shop's offer list)
		copy.AddComponent<UIPanel>();
		NGUITools.MarkParentAsChanged(copy);
		var overlay = copy.AddComponent<ShopItemInfoOverlay>();
		overlay.Init(source.m_AllOverlaysContainerControl, interfaceCamera);
		return overlay;
	}

	private void Init(ContainerControl allOverlays, Camera interfaceCamera)
	{
		m_AllOverlaysContainerControl = allOverlays;
		m_InterfaceCamera = interfaceCamera;
		m_Bubble = transform.Find("Animation/Bubble");
		m_BubbleContainer = m_Bubble.GetComponent<ContainerControl>();
		m_Title = m_Bubble.Find("1_Name/Name").GetComponent<UILabel>();
		m_TextStyle = m_Bubble.Find("4_CraftOrScrapInfo/Text").GetComponent<UILabel>();
		m_SmallStyle = m_Bubble.Find("3_PassiveEffect/Text").GetComponent<UILabel>();
		foreach (var piece in new[] { "TL", "T", "TR", "L", "C", "R", "BL", "B", "BR" })
		{
			m_Frame[piece] = m_Bubble.Find("Body/" + piece).GetComponent<UISprite>();
		}
		m_GenericAtlas = m_Frame["T"].atlas;
		// Style labels move under a hidden holder so their fonts stay referenced; everything else of the equipment
		// overlay's content goes
		var styles = new GameObject("Styles").transform;
		styles.parent = m_Bubble;
		styles.gameObject.SetActive(false);
		m_TextStyle.transform.parent = styles;
		m_SmallStyle.transform.parent = styles;
		foreach (var name in new[] { "2_PrimaryStat", "3_PassiveEffect", "4_CraftOrScrapInfo", "5_Level", "6_EnchantmentLevel" })
		{
			var child = m_Bubble.Find(name);
			if (child != null)
			{
				DestroyImmediate(child.gameObject);
			}
		}
		m_Title.overflowMethod = UILabel.Overflow.ResizeFreely;
	}

	public void Show(Transform root, Camera orientatedCamera, ShopItemInfo info)
	{
		var panel = GetComponent<UIPanel>();
		if (panel != null)
		{
			var top = int.MinValue;
			foreach (var other in UIPanel.list)
			{
				if (other != panel && other.gameObject.layer == gameObject.layer)
				{
					top = Mathf.Max(top, other.depth);
				}
			}
			if (top != int.MinValue && panel.depth <= top)
			{
				panel.depth = top + 1;
			}
		}
		Build(info);
		var anchor = m_InterfaceCamera.ScreenToWorldPoint(orientatedCamera.WorldToScreenPoint(root.position));
		transform.localPosition = new Vector3(anchor.x, anchor.y, transform.localPosition.z);
		// Beside the item, on the side with more room, and kept on screen
		var x = (anchor.x < 0f ? 1f : -1f) * (m_Size.x * 0.5f + AnchorOffset);
		var y = 0f;
		if (m_AllOverlaysContainerControl != null)
		{
			var screenCentre = m_AllOverlaysContainerControl.transform.localPosition;
			var screenHalf = m_AllOverlaysContainerControl.m_Size * 0.5f;
			var minX = screenCentre.x - screenHalf.x + m_Size.x * 0.5f;
			var maxX = screenCentre.x + screenHalf.x - m_Size.x * 0.5f;
			if (minX <= maxX)
			{
				x = Mathf.Clamp(anchor.x + x, minX, maxX) - anchor.x;
			}
			y = InfoOverlayMgr.GetOverlayY(anchor.y, new Vector2(m_Size.y * 0.5f, -m_Size.y * 0.5f), m_AllOverlaysContainerControl) - anchor.y;
		}
		m_Bubble.localPosition = new Vector3(x, y, m_Bubble.localPosition.z);
		GetComponent<Animation>().Play("InfoOverlay_Enter");
		DebugLog.Log("Begin show ShopItemInfoOverlay for: " + info.Name + " size " + m_Size);
	}

	public void Hide()
	{
		if (gameObject.activeInHierarchy)
		{
			GetComponent<Animation>().Play("InfoOverlay_Leave");
			Invoke("Disable", GetComponent<Animation>()["InfoOverlay_Leave"].length);
		}
	}

	private void Disable()
	{
		gameObject.SetActive(false);
	}

	// Lays out the content centred on the bubble and fits the frame around it. Size is the frame's outer size.
	public void Build(ShopItemInfo info)
	{
		CancelInvoke("Disable");
		if (m_Content != null)
		{
			m_Content.gameObject.SetActive(false);
			DestroyImmediate(m_Content.gameObject);
		}
		m_Content = NewChild(m_Bubble, "Content");
		m_Content.localPosition = new Vector3(0f, 0f, m_Title.cachedTransform.localPosition.z);

		// Columns are built from their top left corner (y grows downwards as negative)
		var left = NewChild(m_Content, "Stats");
		var leftSize = BuildStats(left, info);
		var right = NewChild(m_Content, "PerksAndRecipe");
		var rightSize = BuildPerksAndRecipe(right, info);

		m_Title.text = info.Name;
		m_Title.MakePixelPerfect();
		var titleSize = m_Title.printedSize;

		var columnsWidth = leftSize.x + rightSize.x + (leftSize.x > 0f && rightSize.x > 0f ? ColumnGap : 0f);
		var columnsHeight = Mathf.Max(leftSize.y, rightSize.y);
		var width = Mathf.Max(titleSize.x, columnsWidth);
		var height = titleSize.y + (columnsHeight > 0f ? TitleGap + columnsHeight : 0f);
		var top = height * 0.5f;
		var originX = -width * 0.5f + (width - columnsWidth) * 0.5f;
		var columnsTop = top - titleSize.y - TitleGap;
		left.localPosition = new Vector3(originX, columnsTop, 0f);
		right.localPosition = new Vector3(originX + leftSize.x + (leftSize.x > 0f ? ColumnGap : 0f), columnsTop, 0f);

		m_Title.rawPivot = UIWidget.Pivot.Top;
		m_Title.cachedTransform.parent.localPosition = Vector3.zero;
		m_Title.cachedTransform.localPosition = new Vector3(0f, top, m_Title.cachedTransform.localPosition.z);

		m_Size = new Vector2(width + Padding * 2f, height + Padding * 2f);
		FitFrame(m_Size);
		if (m_BubbleContainer != null)
		{
			m_BubbleContainer.m_Size = new Vector3(m_Size.x, m_Size.y, m_BubbleContainer.m_Size.z);
		}
	}

	private Vector2 BuildStats(Transform parent, ShopItemInfo info)
	{
		var rows = info.StatRows;
		if (rows.Count == 0)
		{
			return Vector2.zero;
		}
		// Star counts only mean something when there is more than one row
		var showStars = rows.Count > 1;
		var width = 0f;
		Transform baseRow = null;
		var baseWidth = 0f;
		for (var i = 0; i < rows.Count; i++)
		{
			var row = NewChild(parent, "Row_" + i);
			row.localPosition = new Vector3(0f, -StatRowHeight * (i + 0.5f), 0f);
			var x = 0f;
			// The base (0 star) row has no stars and is centred in the column below
			if (showStars && i > 0)
			{
				for (var star = 0; star < 3; star++)
				{
					var sprite = AddSprite(row, m_GenericAtlas, star < i ? "EquipmentRating_Star2_Full" : "EquipmentRating_Star2_Empty", x + star * StarStep + StarSize * 0.5f, StarSize, 5 + star);
					if (sprite == null)
					{
						break;
					}
				}
				x += StarStep * 2f + StarSize + 12f;
			}
			x = AddCells(row, rows[i], x, StatIconSize, m_TextStyle);
			width = Mathf.Max(width, x);
			if (i == 0)
			{
				baseRow = row;
				baseWidth = x;
			}
		}
		if (baseRow != null)
		{
			baseRow.localPosition += new Vector3((width - baseWidth) * 0.5f, 0f, 0f);
		}
		return new Vector2(width, StatRowHeight * rows.Count);
	}

	private Vector2 BuildPerksAndRecipe(Transform parent, ShopItemInfo info)
	{
		var y = 0f;
		var width = 0f;
		for (var i = 0; i < info.Perks.Count; i++)
		{
			var perk = info.Perks[i];
			if (i > 0)
			{
				y -= PerkGap;
			}
			var header = NewChild(parent, "Perk_" + i);
			header.localPosition = new Vector3(0f, y - PerkHeaderHeight * 0.5f, 0f);
			AddSprite(header, m_GenericAtlas, "Label_Round_XL", PerkIconSize * 0.5f, PerkIconSize + 10f, 4);
			AddIcon(header, perk.Icon, PerkIconSize * 0.5f, PerkIconSize, 6);
			var name = AddLabel(header, m_TextStyle, perk.Name, ShopItemInfo.TextColor, PerkIconSize + 14f);
			width = Mathf.Max(width, PerkIconSize + 14f + name.printedSize.x);
			y -= PerkHeaderHeight;
			// Values wrap onto more lines when they would make the column too wide
			Transform line = null;
			var x = 0f;
			foreach (var value in perk.Values)
			{
				var cellWidth = MeasureCell(value, ValueIconSize, m_TextStyle);
				if (line == null || (x > ValueIndent && x + cellWidth > MaxPerkWidth))
				{
					line = NewChild(parent, "PerkValues_" + i);
					line.localPosition = new Vector3(0f, y - ValueRowHeight * 0.5f, 0f);
					y -= ValueRowHeight;
					x = ValueIndent;
				}
				x = AddCell(line, value, x, ValueIconSize, m_TextStyle) + CellGap;
				width = Mathf.Max(width, x - CellGap);
			}
		}
		if (info.Recipe.Count > 0)
		{
			if (info.Perks.Count > 0)
			{
				y -= PerkGap * 2f;
			}
			var caption = NewChild(parent, "RecipeCaption");
			var captionLabel = AddLabel(caption, m_SmallStyle, info.RecipeCaption, ShopItemInfo.TextColor, 0f);
			caption.localPosition = new Vector3(0f, y - captionLabel.printedSize.y * 0.5f, 0f);
			width = Mathf.Max(width, captionLabel.printedSize.x);
			y -= captionLabel.printedSize.y + 2f;
			var row = NewChild(parent, "Recipe");
			row.localPosition = new Vector3(0f, y - RecipeIconSize * 0.5f, 0f);
			width = Mathf.Max(width, AddCells(row, info.Recipe, 0f, RecipeIconSize, m_TextStyle));
			y -= RecipeIconSize;
		}
		return new Vector2(width, -y);
	}

	private float AddCells(Transform row, List<ShopItemInfo.Cell> cells, float x, float iconSize, UILabel style)
	{
		for (var i = 0; i < cells.Count; i++)
		{
			x = AddCell(row, cells[i], x + (i > 0 ? CellGap : 0f), iconSize, style);
		}
		return x;
	}

	private float AddCell(Transform row, ShopItemInfo.Cell cell, float x, float iconSize, UILabel style)
	{
		if (AddIcon(row, cell, x + iconSize * 0.5f, iconSize, 6) != null)
		{
			x += iconSize + 4f;
		}
		if (!string.IsNullOrEmpty(cell.Text))
		{
			x += AddLabel(row, style, cell.Text, cell.Color, x).printedSize.x;
		}
		return x;
	}

	private float MeasureCell(ShopItemInfo.Cell cell, float iconSize, UILabel style)
	{
		var width = HasIcon(cell) ? iconSize + 4f : 0f;
		if (!string.IsNullOrEmpty(cell.Text))
		{
			var probe = AddLabel(m_Content, style, cell.Text, cell.Color, 0f);
			width += probe.printedSize.x;
			probe.gameObject.SetActive(false);
			DestroyImmediate(probe.gameObject);
		}
		return width;
	}

	private bool HasIcon(ShopItemInfo.Cell cell)
	{
		return cell != null && (!string.IsNullOrEmpty(cell.Texture) || !string.IsNullOrEmpty(cell.Sprite));
	}

	private UIWidget AddIcon(Transform row, ShopItemInfo.Cell cell, float x, float size, int depth)
	{
		if (!HasIcon(cell))
		{
			return null;
		}
		if (!string.IsNullOrEmpty(cell.Texture))
		{
			var texture = Resources.Load<Texture2D>("UI/" + cell.Texture);
			if (texture == null)
			{
				return null;
			}
			var uiTexture = NewChild(row, "Icon").gameObject.AddComponent<UITexture>();
			uiTexture.cachedTransform.localPosition = new Vector3(x, 0f, 0f);
			uiTexture.shader = Shader.Find("Unlit/Transparent Colored");
			uiTexture.mainTexture = texture;
			uiTexture.depth = depth;
			Fit(uiTexture, texture.width, texture.height, size);
			return uiTexture;
		}
		return AddSprite(row, GetAtlas(cell.Atlas), cell.Sprite, x, size, depth);
	}

	private UISprite AddSprite(Transform row, UIAtlas atlas, string spriteName, float x, float size, int depth)
	{
		var data = atlas != null ? atlas.GetSprite(spriteName) : null;
		if (data == null)
		{
			return null;
		}
		var sprite = NewChild(row, "Icon").gameObject.AddComponent<UISprite>();
		sprite.cachedTransform.localPosition = new Vector3(x, 0f, 0f);
		sprite.atlas = atlas;
		sprite.spriteName = spriteName;
		sprite.depth = depth;
		Fit(sprite, data.width, data.height, size);
		return sprite;
	}

	// Fit inside size x size keeping the aspect
	private static void Fit(UIWidget widget, int width, int height, float size)
	{
		var aspect = height > 0 ? (float)width / height : 1f;
		widget.width = Mathf.RoundToInt(aspect >= 1f ? size : size * aspect);
		widget.height = Mathf.RoundToInt(aspect >= 1f ? size / aspect : size);
	}

	private UILabel AddLabel(Transform row, UILabel style, string text, Color color, float x)
	{
		var label = NewChild(row, "Value").gameObject.AddComponent<UILabel>();
		// rawPivot: the pivot setter moves the widget to keep it in place
		label.rawPivot = UIWidget.Pivot.Left;
		if (style.bitmapFont != null)
		{
			label.bitmapFont = style.bitmapFont;
		}
		else
		{
			label.trueTypeFont = style.trueTypeFont;
		}
		label.fontSize = style.fontSize;
		label.fontStyle = style.fontStyle;
		label.effectStyle = style.effectStyle;
		label.effectColor = style.effectColor;
		label.overflowMethod = UILabel.Overflow.ResizeFreely;
		label.supportEncoding = false;
		label.color = color;
		label.depth = 10;
		label.text = text ?? string.Empty;
		label.MakePixelPerfect();
		label.cachedTransform.localPosition = new Vector3(x, 0f, 0f);
		return label;
	}

	private Transform NewChild(Transform parent, string name)
	{
		var child = new GameObject(name).transform;
		child.gameObject.layer = gameObject.layer;
		child.parent = parent;
		child.localPosition = Vector3.zero;
		child.localRotation = Quaternion.identity;
		child.localScale = Vector3.one;
		return child;
	}

	private UIAtlas GetAtlas(string atlasName)
	{
		if (string.IsNullOrEmpty(atlasName) || atlasName == "GenericElements")
		{
			return m_GenericAtlas;
		}
		UIAtlas atlas;
		if (!m_Atlases.TryGetValue(atlasName, out atlas))
		{
			atlas = AtlasResolver != null ? AtlasResolver(atlasName) : LoadAtlas(atlasName);
			m_Atlases[atlasName] = atlas;
		}
		return atlas != null ? atlas : m_GenericAtlas;
	}

	private static UIAtlas LoadAtlas(string atlasName)
	{
		var provider = DIContainerInfrastructure.GetGenericIconAtlasAssetProvider();
		var atlasObject = provider.ContainsAsset(atlasName) ? provider.GetObject(atlasName) as GameObject : null;
		return atlasObject != null ? atlasObject.GetComponent<UIAtlas>() : null;
	}

	// The nine frame pieces around a centre that leaves the given outer size, centred on the bubble
	private void FitFrame(Vector2 size)
	{
		var x0 = -size.x * 0.5f + FrameLeft;
		var x1 = Mathf.Max(x0 + 2f, size.x * 0.5f - FrameRight);
		var y1 = size.y * 0.5f - FrameTop;
		var y0 = Mathf.Min(y1 - 2f, -size.y * 0.5f + FrameBottom);
		var width = Mathf.RoundToInt(x1 - x0);
		var height = Mathf.RoundToInt(y1 - y0);
		var cx = (x0 + x1) * 0.5f;
		var cy = (y0 + y1) * 0.5f;
		Place("C", cx, cy, width, height);
		Place("L", x0, cy, -1, height);
		Place("R", x1, cy, -1, height);
		Place("T", cx, y1, width, -1);
		Place("B", cx, y0, width, -1);
		Place("TL", x0, y1, -1, -1);
		Place("TR", x1, y1, -1, -1);
		Place("BL", x0, y0 - FrameBottom, -1, -1);
		Place("BR", x1, y0 - FrameBottom, -1, -1);
	}

	private void Place(string piece, float x, float y, int width, int height)
	{
		var sprite = m_Frame[piece];
		sprite.cachedTransform.localPosition = new Vector3(x, y, sprite.cachedTransform.localPosition.z);
		if (width > 0)
		{
			sprite.width = width;
		}
		if (height > 0)
		{
			sprite.height = height;
		}
	}
}
