using ABH.Shared.Generic;
using UnityEngine;

// Tap-and-hold overlay of a battle star: the star's condition icon on the left, its description on the right
// (the number in red), on the usual info overlay paper. Runtime copy of InfoOverlay_Equipment that keeps the
// frame and the enter/leave animation, like ShopItemInfoOverlay.
public class StarConditionOverlay : MonoBehaviour
{
	private const float Padding = 26f;

	private const float IconHeight = 96f;

	private const float IconGap = 26f;

	private const float AnchorOffset = 50f;

	private const float FrameLeft = 122f;

	private const float FrameRight = 100f;

	private const float FrameTop = 60f;

	private const float FrameBottom = 44f;

	public static readonly Color TextColor = new Color(0.05f, 0.05f, 0.05f, 1f);

	private Transform m_Bubble;

	private Transform m_Content;

	private UILabel m_TextStyle;

	private readonly System.Collections.Generic.Dictionary<string, UISprite> m_Frame = new System.Collections.Generic.Dictionary<string, UISprite>();

	private ContainerControl m_BubbleContainer;

	private ContainerControl m_AllOverlaysContainerControl;

	private Camera m_InterfaceCamera;

	private Vector2 m_Size;

	public Vector2 Size
	{
		get
		{
			return m_Size;
		}
	}

	public static StarConditionOverlay Create(EquipmentOverlay source, Camera interfaceCamera)
	{
		var copy = Instantiate(source.gameObject, source.transform.parent);
		copy.name = "InfoOverlay_StarCondition";
		copy.SetActive(false);
		DestroyImmediate(copy.GetComponent<EquipmentOverlay>());
		copy.AddComponent<UIPanel>();
		NGUITools.MarkParentAsChanged(copy);
		var overlay = copy.AddComponent<StarConditionOverlay>();
		overlay.Init(source.m_AllOverlaysContainerControl, interfaceCamera);
		return overlay;
	}

	private void Init(ContainerControl allOverlays, Camera interfaceCamera)
	{
		m_AllOverlaysContainerControl = allOverlays;
		m_InterfaceCamera = interfaceCamera;
		m_Bubble = transform.Find("Animation/Bubble");
		m_BubbleContainer = m_Bubble.GetComponent<ContainerControl>();
		m_TextStyle = m_Bubble.Find("4_CraftOrScrapInfo/Text").GetComponent<UILabel>();
		foreach (var piece in new[] { "TL", "T", "TR", "L", "C", "R", "BL", "B", "BR" })
		{
			m_Frame[piece] = m_Bubble.Find("Body/" + piece).GetComponent<UISprite>();
		}
		var styles = new GameObject("Styles").transform;
		styles.parent = m_Bubble;
		styles.gameObject.SetActive(false);
		m_TextStyle.transform.parent = styles;
		foreach (var name in new[] { "1_Name", "2_PrimaryStat", "3_PassiveEffect", "4_CraftOrScrapInfo", "5_Level", "6_EnchantmentLevel" })
		{
			var child = m_Bubble.Find(name);
			if (child != null)
			{
				DestroyImmediate(child.gameObject);
			}
		}
	}

	public void Show(Transform root, Camera orientatedCamera, StarCondition star, string scoreText = null)
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
		Build(star, scoreText);
		var anchor = m_InterfaceCamera.ScreenToWorldPoint(orientatedCamera.WorldToScreenPoint(root.position));
		transform.localPosition = new Vector3(anchor.x, anchor.y, transform.localPosition.z);
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

	// Icon (if any) left of the text, centred on the bubble; the frame is fitted around them
	public void Build(StarCondition star, string scoreText = null)
	{
		CancelInvoke("Disable");
		if (m_Content != null)
		{
			m_Content.gameObject.SetActive(false);
			DestroyImmediate(m_Content.gameObject);
		}
		m_Content = NewChild(m_Bubble, "Content");
		m_Content.localPosition = new Vector3(0f, 0f, -1f);

		var label = NewChild(m_Content, "Text").gameObject.AddComponent<UILabel>();
		label.rawPivot = UIWidget.Pivot.Left;
		if (m_TextStyle.bitmapFont != null)
		{
			label.bitmapFont = m_TextStyle.bitmapFont;
		}
		else
		{
			label.trueTypeFont = m_TextStyle.trueTypeFont;
		}
		label.fontSize = m_TextStyle.fontSize;
		label.fontStyle = m_TextStyle.fontStyle;
		label.effectStyle = UILabel.Effect.None;
		label.overflowMethod = UILabel.Overflow.ResizeFreely;
		label.alignment = NGUIText.Alignment.Left;
		label.supportEncoding = true;
		label.color = TextColor;
		label.depth = 10;
		label.text = BattleStars.Description(star, scoreText);
		label.MakePixelPerfect();
		var textSize = label.printedSize;

		var iconWidth = 0f;
		var iconHeight = 0f;
		// The rimmed icon: the plain white one vanishes on the beige paper
		var texture = BattleStars.Icon(star, true);
		UITexture icon = null;
		if (texture != null)
		{
			icon = NewChild(m_Content, "Icon").gameObject.AddComponent<UITexture>();
			icon.shader = Shader.Find("Unlit/Transparent Colored");
			icon.mainTexture = texture;
			icon.depth = 9;
			iconHeight = Mathf.Max(IconHeight, textSize.y);
			iconWidth = iconHeight * texture.width / Mathf.Max(1f, texture.height);
			icon.width = Mathf.RoundToInt(iconWidth);
			icon.height = Mathf.RoundToInt(iconHeight);
		}
		var gap = icon != null ? IconGap : 0f;
		var width = iconWidth + gap + textSize.x;
		var height = Mathf.Max(iconHeight, textSize.y);
		var left = -width * 0.5f;
		if (icon != null)
		{
			icon.cachedTransform.localPosition = new Vector3(left + iconWidth * 0.5f, 0f, 0f);
		}
		label.cachedTransform.localPosition = new Vector3(left + iconWidth + gap, 0f, 0f);

		m_Size = new Vector2(width + Padding * 2f + 40f, height + Padding * 2f);
		FitFrame(m_Size);
		if (m_BubbleContainer != null)
		{
			m_BubbleContainer.m_Size = new Vector3(m_Size.x, m_Size.y, m_BubbleContainer.m_Size.z);
		}
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
