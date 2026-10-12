using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Draws an upgrade blueprint's stat changes as rows of  [icon] old  ->  [icon] new  under a label, using that label's
// font. Icons and the green arrow (UpgradeArrow turned to point right) come from the GenericElements atlas.
public static class UpgradeStatChangeView
{
	public const float RowHeight = 48f;

	private const string ContainerName = "UpgradeStatChanges";

	private const float IconSize = 40f;

	private const float ArrowSize = 34f;

	private const float Gap = 8f;

	private static readonly Color NewValueColor = new Color(0.45f, 0.95f, 0.3f, 1f);

	private static UIAtlas s_Atlas;

	public static void Clear(Transform parent)
	{
		if (parent == null)
		{
			return;
		}
		var existing = parent.Find(ContainerName);
		if (existing != null)
		{
			existing.gameObject.SetActive(false);
			Object.Destroy(existing.gameObject);
		}
	}

	// Builds the rows under parent with their top edge at topLeft (parent-local); centered rows are centered on
	// topLeft.x instead. Returns the root, whose size is rows * RowHeight high.
	public static GameObject Build(Transform parent, UILabel style, Vector3 topLeft, List<UpgradeBlueprints.StatChange> changes, bool centered)
	{
		Clear(parent);
		if (changes == null || changes.Count == 0 || style == null)
		{
			return null;
		}
		var atlas = GetAtlas();
		var root = new GameObject(ContainerName);
		root.layer = style.gameObject.layer;
		root.transform.parent = parent;
		root.transform.localPosition = topLeft;
		root.transform.localRotation = Quaternion.identity;
		root.transform.localScale = Vector3.one;
		var depth = style.depth + 1;
		for (var i = 0; i < changes.Count; i++)
		{
			var change = changes[i];
			var row = new GameObject("Row_" + i);
			row.layer = root.layer;
			row.transform.parent = root.transform;
			row.transform.localRotation = Quaternion.identity;
			row.transform.localScale = Vector3.one;
			var x = 0f;
			x = AddIcon(row.transform, atlas, change.OldIcon, x, depth);
			x = AddValue(row.transform, style, change.OldValue, style.color, x, depth);
			x += Gap;
			x = AddArrow(row.transform, atlas, x, depth);
			x += Gap;
			x = AddIcon(row.transform, atlas, change.NewIcon, x, depth);
			x = AddValue(row.transform, style, change.NewValue, NewValueColor, x, depth);
			var rowX = centered ? -x * 0.5f : 0f;
			row.transform.localPosition = new Vector3(rowX, -RowHeight * (i + 0.5f), 0f);
		}
		return root;
	}

	// Shop tile: rows replace the effect text, left-aligned under the offer title, ShopTileScale of full size and
	// shrunk further only if they would be taller than ShopTileMaxHeight.
	public static float ShopTileScale = 0.9f;

	public static float ShopTileMaxHeight = 90f;

	public static Vector2 ShopTileOffset = new Vector2(0f, 8f);

	public static GameObject ShowInShopTile(UILabel label, List<UpgradeBlueprints.StatChange> changes)
	{
		label.text = string.Empty;
		var pivot = label.pivotOffset;
		var height = GetHeight(changes.Count);
		var scale = Mathf.Min(ShopTileScale, ShopTileMaxHeight / height);
		var topLeft = new Vector3(-pivot.x * label.width + ShopTileOffset.x, (1f - pivot.y) * label.height + ShopTileOffset.y, 0f);
		var rows = Build(label.transform, label, topLeft, changes, false);
		if (rows != null)
		{
			rows.transform.localScale = new Vector3(scale, scale, 1f);
		}
		return rows;
	}

	// Size of a class picture on a blueprint (about the visual size of the Foolish Wand equipment icon).
	public static float ClassIconScale = 0.85f;

	// Share of the stage view the picture may fill before the view (and the tile's display) is widened.
	public static float StageFill = 0.92f;

	// Puts a class picture in an EquipmentIconPreviewPool stage at ClassIconScale, centred. When it's bigger than the
	// camera's view, the view is widened to fit. Returns the factor the view grew by, so the tile can grow its display
	// by the same amount and the picture keeps its on-screen size. The pool resets the camera when the stage is released.
	public static float FitIconToStage(GameObject icon, Camera stageCamera)
	{
		if (stageCamera == null || !stageCamera.orthographic)
		{
			icon.transform.localScale = Vector3.one * 0.45f;
			return 1f;
		}
		// The stage camera only renders its own layer (not the stage root's)
		for (var layer = 0; layer < 32; layer++)
		{
			if ((stageCamera.cullingMask & (1 << layer)) != 0)
			{
				SetLayer(icon.transform, layer);
				break;
			}
		}
		var rect = MeasureCHSprites(icon.transform);
		var size = Vector2.Max(rect.size, MeasureRenderers(icon.transform));
		if (size.x <= 0f || size.y <= 0f)
		{
			icon.transform.localScale = Vector3.one * 0.45f;
			return 1f;
		}
		var scale = ClassIconScale;
		icon.transform.localScale = Vector3.one * scale;
		icon.transform.localPosition = new Vector3(-rect.center.x * scale, -rect.center.y * scale, icon.transform.localPosition.z);
		var needed = Mathf.Max(size.x, size.y) * scale * 0.5f / StageFill;
		if (needed <= stageCamera.orthographicSize)
		{
			return 1f;
		}
		var factor = needed / stageCamera.orthographicSize;
		stageCamera.orthographicSize = needed;
		return factor;
	}

	// Sprite extents from CHMeshSprite width/height (their meshes are only built at runtime, so renderer bounds are empty).
	private static Rect MeasureCHSprites(Transform root)
	{
		var min = new Vector2(float.MaxValue, float.MaxValue);
		var max = new Vector2(float.MinValue, float.MinValue);
		foreach (var component in root.GetComponentsInChildren<Component>(true))
		{
			if (component == null || component.GetType().Name != "CHMeshSprite")
			{
				continue;
			}
			var type = component.GetType();
			var widthField = type.GetField("m_Width", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			var heightField = type.GetField("m_Height", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			if (widthField == null || heightField == null)
			{
				continue;
			}
			var t = component.transform;
			var half = new Vector2(System.Convert.ToSingle(widthField.GetValue(component)), System.Convert.ToSingle(heightField.GetValue(component))) * 0.5f;
			var centre = (Vector2)root.InverseTransformPoint(t.position);
			var extent = new Vector2(half.x * Mathf.Abs(t.lossyScale.x / root.lossyScale.x), half.y * Mathf.Abs(t.lossyScale.y / root.lossyScale.y));
			min = Vector2.Min(min, centre - extent);
			max = Vector2.Max(max, centre + extent);
		}
		return min.x > max.x ? new Rect() : Rect.MinMaxRect(min.x, min.y, max.x, max.y);
	}

	private static Vector2 MeasureRenderers(Transform root)
	{
		var found = false;
		var bounds = new Bounds();
		foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
		{
			if (renderer.bounds.size.sqrMagnitude <= 0f)
			{
				continue;
			}
			if (!found)
			{
				bounds = renderer.bounds;
				found = true;
			}
			else
			{
				bounds.Encapsulate(renderer.bounds);
			}
		}
		if (!found)
		{
			return Vector2.zero;
		}
		var scale = root.lossyScale;
		return new Vector2(bounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f), bounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f));
	}

	private static void SetLayer(Transform t, int layer)
	{
		t.gameObject.layer = layer;
		foreach (Transform child in t)
		{
			SetLayer(child, layer);
		}
	}

	public static float GetHeight(int rows)
	{
		return rows * RowHeight;
	}

	private static float AddIcon(Transform row, UIAtlas atlas, string spriteName, float x, int depth)
	{
		if (string.IsNullOrEmpty(spriteName) || atlas == null || atlas.GetSprite(spriteName) == null)
		{
			return x;
		}
		var sprite = NewWidget<UISprite>(row, "Icon", x + IconSize * 0.5f);
		sprite.atlas = atlas;
		sprite.spriteName = spriteName;
		sprite.depth = depth;
		sprite.width = (int)IconSize;
		sprite.height = (int)IconSize;
		return x + IconSize + Gap * 0.5f;
	}

	private static float AddArrow(Transform row, UIAtlas atlas, float x, int depth)
	{
		if (atlas == null || atlas.GetSprite("UpgradeArrow") == null)
		{
			return x;
		}
		var arrow = NewWidget<UISprite>(row, "Arrow", x + ArrowSize * 0.5f);
		arrow.atlas = atlas;
		arrow.spriteName = "UpgradeArrow";
		arrow.depth = depth;
		arrow.width = (int)ArrowSize;
		arrow.height = (int)ArrowSize;
		arrow.cachedTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
		return x + ArrowSize;
	}

	private static float AddValue(Transform row, UILabel style, string text, Color color, float x, int depth)
	{
		var label = NewWidget<UILabel>(row, "Value", x);
		// rawPivot: the pivot setter moves the widget to keep it in place, which would centre the text on x
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
		label.depth = depth;
		label.text = text;
		label.MakePixelPerfect();
		label.cachedTransform.localPosition = new Vector3(x, 0f, 0f);
		return x + label.printedSize.x;
	}

	private static T NewWidget<T>(Transform row, string name, float x) where T : UIWidget
	{
		var go = new GameObject(name);
		go.layer = row.gameObject.layer;
		go.transform.parent = row;
		go.transform.localPosition = new Vector3(x, 0f, 0f);
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		return go.AddComponent<T>();
	}

	private static UIAtlas GetAtlas()
	{
		if (s_Atlas == null)
		{
			s_Atlas = Resources.FindObjectsOfTypeAll<UIAtlas>().FirstOrDefault(a => a.name == "GenericElements" && a.GetSprite("UpgradeArrow") != null);
		}
		return s_Atlas;
	}
}
