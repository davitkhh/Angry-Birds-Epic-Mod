using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Preview of the shop tap-and-hold overlay (ShopItemInfoOverlay) built from InfoOverlays.unity in edit mode, with mock
// content for the Foolish Wand, a Stick Sword blueprint and the Knight upgrade blueprint (game services don't run here).
// Request: Temp/ShopItemOverlayPreview.request. Output: Temp/ShopItemOverlayPreview_<n>.png (+ .log). Scene closed unsaved.
public static class ShopItemOverlayPreview
{
	private const string RequestFile = "Temp/ShopItemOverlayPreview.request";

	private const string LogFile = "Temp/ShopItemOverlayPreview.log";

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
		var log = new StringBuilder();
		try
		{
			Render(log);
		}
		catch (Exception e)
		{
			log.AppendLine("EXCEPTION " + e);
		}
		File.WriteAllText(LogFile, log.ToString());
	}

	private static List<ShopItemInfo> MockInfos()
	{
		var text = ShopItemInfo.TextColor;
		var wand = new ShopItemInfo { Name = "Foolish Wand" };
		wand.StatRows.Add(new List<ShopItemInfo.Cell> { ShopItemInfo.Cell.FromSprite("Character_Damage_Large", "49", text) });
		wand.Perks.Add(new ShopItemInfo.PerkInfo
		{
			Icon = ShopItemInfo.Cell.FromSprite("PassiveEffect_HocusPocus", null, text),
			Name = "Calming Waves",
			Values =
			{
				ShopItemInfo.Cell.FromTexture(ShopItemInfo.ChanceIcon, "50%", ShopItemInfo.Blue),
				ShopItemInfo.Cell.FromSprite("PassiveEffect_HocusPocus", "15%", ShopItemInfo.Green),
				ShopItemInfo.Cell.FromTexture(ShopItemInfo.RageDrainIcon, "10%", ShopItemInfo.Red)
			}
		});

		var sword = new ShopItemInfo { Name = "Blueprint: Stick Sword", RecipeCaption = "Recipe" };
		foreach (var value in new[] { "16", "18", "19", "21" })
		{
			sword.StatRows.Add(new List<ShopItemInfo.Cell> { ShopItemInfo.Cell.FromSprite("Character_Damage_Large", value, text) });
		}
		sword.Perks.Add(new ShopItemInfo.PerkInfo
		{
			Icon = ShopItemInfo.Cell.FromTexture(ShopItemInfo.WeakenIcon, null, text),
			Name = "Weaken",
			Values = { ShopItemInfo.Cell.FromTexture(ShopItemInfo.WeakenIcon, "30%", ShopItemInfo.Purple) }
		});
		sword.Recipe.Add(ShopItemInfo.Cell.FromSprite("Stone", "6", text, "Resources"));
		sword.Recipe.Add(ShopItemInfo.Cell.FromSprite("Wood", "4", text, "Resources"));
		sword.Recipe.Add(ShopItemInfo.Cell.FromSprite("Flotsam", "2", ShopItemInfo.Red, "Resources"));

		var knight = new ShopItemInfo { Name = "Knight Helmet Upgrade", RecipeCaption = "Recipe" };
		foreach (var value in new[] { "+10%", "+11%", "+12%", "+13%" })
		{
			knight.StatRows.Add(new List<ShopItemInfo.Cell> { ShopItemInfo.Cell.FromSprite("Character_Damage_Large", value, text), ShopItemInfo.Cell.FromSprite("Character_Health_Large", value, text) });
		}
		knight.Recipe.Add(ShopItemInfo.Cell.FromSprite("Wood", "8", text, "Resources"));
		knight.Recipe.Add(ShopItemInfo.Cell.FromSprite("Stone", "12", text, "Resources"));
		return new List<ShopItemInfo> { wand, sword, knight };
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
			var overlay = ShopItemInfoOverlay.Create(source, null);
			overlay.AtlasResolver = name =>
			{
				var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/" + name + ".prefab");
				return prefab != null ? prefab.GetComponent<UIAtlas>() : null;
			};
			overlay.gameObject.SetActive(true);
			var animation = overlay.GetComponent<Animation>();
			var enter = animation != null ? animation.GetClip("InfoOverlay_Enter") : null;
			var infos = MockInfos();
			for (var i = 0; i < infos.Count; i++)
			{
				overlay.Build(infos[i]);
				if (enter != null)
				{
					enter.SampleAnimation(overlay.gameObject, enter.length);
				}
				log.AppendLine(infos[i].Name + " size " + overlay.Size);
				RenderOverlay(overlay, "Temp/ShopItemOverlayPreview_" + i + ".png", log);
			}
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

	private static void RenderOverlay(ShopItemInfoOverlay overlay, string output, StringBuilder log)
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
		for (var pass = 0; pass < 3 && UIPanel.list.Count > 0; pass++)
		{
			foreach (var panel in UIPanel.list.ToList())
			{
				updateFrame.SetValue(updateFrame.IsStatic ? null : panel, -1);
			}
			lateUpdate.Invoke(UIPanel.list[0], null);
		}
		var camera = NGUITools.FindCameraForLayer(overlay.gameObject.layer);
		if (camera == null)
		{
			log.AppendLine("no camera for layer " + overlay.gameObject.layer);
			return;
		}
		var hidden = new List<Renderer>();
		var bubble = overlay.transform.Find("Animation/Bubble");
		foreach (var drawCall in UIDrawCall.activeList)
		{
			var renderer = drawCall != null ? drawCall.GetComponent<Renderer>() : null;
			if (renderer != null && renderer.enabled && (drawCall.manager == null || !drawCall.manager.transform.IsChildOf(root.transform)))
			{
				renderer.enabled = false;
				hidden.Add(renderer);
			}
		}
		// Only this overlay: hide the scene's other overlays' widgets
		var others = root.GetComponentsInChildren<UIWidget>().Where(w => !w.transform.IsChildOf(bubble) && w.enabled).ToList();
		foreach (var w in others)
		{
			w.enabled = false;
		}
		for (var pass = 0; pass < 2 && UIPanel.list.Count > 0; pass++)
		{
			foreach (var panel in UIPanel.list.ToList())
			{
				updateFrame.SetValue(updateFrame.IsStatic ? null : panel, -1);
			}
			lateUpdate.Invoke(UIPanel.list[0], null);
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
