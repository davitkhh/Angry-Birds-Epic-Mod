using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ABH.Shared.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Preview of the battle star condition overlay (StarConditionOverlay) and of condition icons on star sprites, built
// from InfoOverlays.unity in edit mode.
// Request: Temp/StarConditionPreview.request (open editor) or batchmode -executeMethod StarConditionPreview.RenderBatch.
// Output: Library/StarConditionPreview/StarConditionPreview_<n>.png + Temp/StarConditionPreview.log. Scene closed unsaved.
public static class StarConditionPreview
{
	private const string RequestFile = "Temp/StarConditionPreview.request";

	private const string LogFile = "Temp/StarConditionPreview.log";

	private const string ScenePath = "Assets/Scenes/0_Generic/1_Interface/InfoOverlays.unity";

	[InitializeOnLoadMethod]
	private static void Init()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		File.Delete(RequestFile);
		RunLogged();
	}

	public static void RenderBatch()
	{
		RunLogged();
	}

	private static void RunLogged()
	{
		var log = new StringBuilder();
		Directory.CreateDirectory("Library/StarConditionPreview");
		try
		{
			Render(log);
		}
		catch (Exception e)
		{
			log.AppendLine("EXCEPTION " + e);
		}
		Directory.CreateDirectory("Temp");
		Directory.CreateDirectory("Library/StarConditionPreview");
		File.WriteAllText(LogFile, log.ToString());
		Debug.Log("[StarConditionPreview]\n" + log);
	}

	private static void Render(StringBuilder log)
	{
		var previousActive = SceneManager.GetActiveScene();
		if (SceneManager.GetSceneByPath(ScenePath).isLoaded)
		{
			log.AppendLine("InfoOverlays is open in the editor; close it first");
			return;
		}
		var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
		try
		{
			SceneManager.SetActiveScene(scene);
			var source = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EquipmentOverlay>(true)).First(o => o.name == "InfoOverlay_Equipment");
			for (var t = source.transform.parent; t != null; t = t.parent)
			{
				t.gameObject.SetActive(true);
			}
			source.gameObject.SetActive(false);
			var overlay = StarConditionOverlay.Create(source, null);
			overlay.gameObject.SetActive(true);
			var animation = overlay.GetComponent<Animation>();
			var enter = animation != null ? animation.GetClip("InfoOverlay_Enter") : null;
			var stars = new List<StarCondition>
			{
				new StarCondition { Type = StarConditionType.OnlyXBirds, Value = 1 },
				new StarCondition { Type = StarConditionType.OnlyXBirds, Value = 2 },
				null
			};
			for (var i = 0; i < stars.Count; i++)
			{
				overlay.Build(stars[i], stars[i] == null ? "45000" : null);
				if (enter != null)
				{
					enter.SampleAnimation(overlay.gameObject, enter.length);
				}
				log.AppendLine("overlay " + i + " size " + overlay.Size);
				foreach (var w in overlay.GetComponentsInChildren<UIWidget>(true))
				{
					var l = w as UILabel;
					var tx = w as UITexture;
					log.AppendLine("  " + w.name + " " + w.GetType().Name + " d" + w.depth + " " + w.width + "x" + w.height + " pos " + w.cachedTransform.localPosition + " active " + w.gameObject.activeInHierarchy + " col " + w.color + (l != null ? " font " + (l.bitmapFont != null ? "bmp " + l.bitmapFont.name : "ttf " + (l.trueTypeFont != null ? l.trueTypeFont.name : "null")) + " size " + l.fontSize + " text '" + l.text + "'" : "") + (tx != null ? " tex " + (tx.mainTexture != null ? tx.mainTexture.name + " " + tx.mainTexture.width + "x" + tx.mainTexture.height : "null") + " shader " + (tx.shader != null ? tx.shader.name : "null") : ""));
				}
				RenderOverlay(overlay, "Library/StarConditionPreview/StarConditionPreview_" + i + ".png", log);
			}

			// Condition icon on stars: battle prep rule tile (BattleStars, 86) and the result screen star (178)
			overlay.Build(null);
			var bubble = overlay.transform.Find("Animation/Bubble");
			var tiles = new GameObject("Tiles").transform;
			tiles.gameObject.layer = overlay.gameObject.layer;
			tiles.parent = bubble;
			tiles.localPosition = new Vector3(0f, 0f, -20f);
			tiles.localScale = Vector3.one;
			var generic = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/GenericElements.prefab").GetComponent<UIAtlas>();
			var battle = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/BattleElements.prefab").GetComponent<UIAtlas>();
			var condition = stars[0];
			AddStar(tiles, generic, "BattleStars", 86, new Vector3(-420f, 0f, 0f), condition, true);
			AddStar(tiles, generic, "BattleStars", 86, new Vector3(-320f, 0f, 0f), null, true);
			AddStar(tiles, battle, "ResultStar_Center", 178, new Vector3(-120f, 0f, 0f), condition, true);
			AddStar(tiles, battle, "ResultStar_Center_Desaturated", 178, new Vector3(80f, 0f, 0f), condition, false);
			AddStar(tiles, battle, "ResultStar_Left_Desaturated", 178, new Vector3(280f, 0f, 0f), null, false);
			RenderOverlay(overlay, "Library/StarConditionPreview/StarConditionPreview_3.png", log);
		}
		finally
		{
			if (previousActive.IsValid())
			{
				SceneManager.SetActiveScene(previousActive);
			}
			EditorSceneManager.CloseScene(scene, true);
		}
	}

	private static void AddStar(Transform parent, UIAtlas atlas, string sprite, int size, Vector3 position, StarCondition condition, bool earned)
	{
		var go = new GameObject(sprite);
		go.layer = parent.gameObject.layer;
		go.transform.parent = parent;
		go.transform.localPosition = position;
		go.transform.localScale = Vector3.one;
		var star = go.AddComponent<UISprite>();
		star.atlas = atlas;
		star.spriteName = sprite;
		star.width = size;
		star.height = size;
		star.depth = 30;
		StarConditionView.Attach(star, condition, false).SetEarned(earned);
	}

	private static void RenderOverlay(StarConditionOverlay overlay, string output, StringBuilder log)
	{
		var root = overlay.transform.root.gameObject;
		var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
		foreach (var rect in root.GetComponentsInChildren<UIRect>().OrderBy(r => r is UIPanel ? 0 : 1))
		{
			foreach (var method in new[] { "OnEnable", "Start", "Update" })
			{
				var m = rect.GetType().GetMethod(method, flags, null, Type.EmptyTypes, null);
				if (m != null)
				{
					try { m.Invoke(rect, null); } catch (Exception) { }
				}
			}
		}
		var updateFrame = typeof(UIPanel).GetField("mUpdateFrame", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
		var lateUpdate = typeof(UIPanel).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
		var camera = NGUITools.FindCameraForLayer(overlay.gameObject.layer);
		if (camera == null)
		{
			log.AppendLine("no camera for layer " + overlay.gameObject.layer);
			return;
		}
		var bubble = overlay.transform.Find("Animation/Bubble");
		var others = root.GetComponentsInChildren<UIWidget>().Where(w => !w.transform.IsChildOf(bubble) && w.enabled).ToList();
		foreach (var w in others)
		{
			w.enabled = false;
		}
		for (var pass = 0; pass < 3 && UIPanel.list.Count > 0; pass++)
		{
			foreach (var panel in UIPanel.list.ToList())
			{
				updateFrame.SetValue(updateFrame.IsStatic ? null : panel, -1);
			}
			lateUpdate.Invoke(UIPanel.list[0], null);
		}
		var hidden = new List<Renderer>();
		foreach (var drawCall in UIDrawCall.activeList)
		{
			var renderer = drawCall != null ? drawCall.GetComponent<Renderer>() : null;
			if (renderer != null && renderer.enabled && (drawCall.manager == null || !drawCall.manager.transform.IsChildOf(root.transform)))
			{
				renderer.enabled = false;
				hidden.Add(renderer);
			}
		}
		var rt = new RenderTexture(1365, 768, 24);
		var previous = camera.targetTexture;
		var previousPosition = camera.transform.position;
		var previousColor = camera.backgroundColor;
		var previousClear = camera.clearFlags;
		var bounds = NGUIMath.CalculateAbsoluteWidgetBounds(bubble);
		camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, previousPosition.z);
		camera.targetTexture = rt;
		camera.clearFlags = CameraClearFlags.SolidColor;
		camera.backgroundColor = new Color(0.25f, 0.35f, 0.45f, 1f);
		camera.Render();
		camera.targetTexture = previous;
		camera.backgroundColor = previousColor;
		camera.clearFlags = previousClear;
		foreach (var renderer in hidden)
		{
			renderer.enabled = true;
		}
		foreach (var w in others)
		{
			w.enabled = true;
		}
		var a = camera.WorldToViewportPoint(bounds.min);
		var b = camera.WorldToViewportPoint(bounds.max);
		camera.transform.position = previousPosition;
		var x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) * rt.width) - 10, 0, rt.width - 1);
		var y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y) * rt.height) - 10, 0, rt.height - 1);
		var x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x) * rt.width) + 10, x0 + 1, rt.width);
		var y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y) * rt.height) + 10, y0 + 1, rt.height);
		RenderTexture.active = rt;
		var image = new Texture2D(x1 - x0, y1 - y0, TextureFormat.RGB24, false);
		image.ReadPixels(new Rect(x0, y0, x1 - x0, y1 - y0), 0, 0);
		image.Apply();
		RenderTexture.active = null;
		File.WriteAllBytes(output, image.EncodeToPNG());
		log.AppendLine("rendered " + output + " " + (x1 - x0) + "x" + (y1 - y0) + " from " + camera.name);
		Object.DestroyImmediate(image);
		rt.Release();
		Object.DestroyImmediate(rt);
	}
}
