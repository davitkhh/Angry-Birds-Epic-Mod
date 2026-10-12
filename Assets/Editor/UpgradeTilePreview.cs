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

// Preview of a shop tile showing a class upgrade blueprint (Knight's Training Manual), rendered from Window_WorldShop.unity
// in edit mode with the same layout code the game uses. Request: Temp/UpgradeTilePreview.request, optional content
// "scale maxHeight offsetX offsetY stageFill". Output: Temp/UpgradeTilePreview.png (+ .log). The scene is closed unsaved.
public static class UpgradeTilePreview
{
	private const string RequestFile = "Temp/UpgradeTilePreview.request";

	private const string LogFile = "Temp/UpgradeTilePreview.log";

	private const string OutputFile = "Temp/UpgradeTilePreview.png";

	private const string ScenePath = "Assets/Scenes/1_WorldMap/2_Interface/Window_WorldShop.unity";

	private const string IconPrefab = "Assets/Resources/Prefabs/0_Generic/1_Interface/BirdEquipment/Headgear/1_RedBird/Headgear_Knight.prefab";

	private static bool s_Plain;

	private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

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
		var request = File.ReadAllText(RequestFile).Trim();
		File.Delete(RequestFile);
		var log = new StringBuilder();
		try
		{
			var values = request.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
			if (values.Length > 0) UpgradeStatChangeView.ShopTileScale = values[0];
			if (values.Length > 1) UpgradeStatChangeView.ShopTileMaxHeight = values[1];
			if (values.Length > 3) UpgradeStatChangeView.ShopTileOffset = new Vector2(values[2], values[3]);
			if (values.Length > 4) UpgradeStatChangeView.StageFill = values[4];
			s_Plain = values.Length > 5 && values[5] > 0f;
			Render(log);
		}
		catch (Exception e)
		{
			log.AppendLine("EXCEPTION " + e);
		}
		File.WriteAllText(LogFile, log.ToString());
	}

	private static T Get<T>(object o, string field) where T : class
	{
		return o.GetType().GetField(field, Fields).GetValue(o) as T;
	}

	private static void Render(StringBuilder log)
	{
		var previousActive = SceneManager.GetActiveScene();
		var alreadyOpen = SceneManager.GetSceneByPath(ScenePath).isLoaded;
		var scene = alreadyOpen ? SceneManager.GetSceneByPath(ScenePath) : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
		var created = new List<Object>();
		try
		{
			SceneManager.SetActiveScene(scene);
			var roots = scene.GetRootGameObjects();
			var blinds = roots.SelectMany(r => r.GetComponentsInChildren<ShopOfferBlindWorldmap>(true)).ToList();
			var blind = blinds.First();
			for (var t = blind.transform; t != null; t = t.parent)
			{
				t.gameObject.SetActive(true);
			}
			log.AppendLine("blinds " + blinds.Count + ", using " + blind.name);
			// The list isn't laid out in edit mode, so the other tiles sit on top of this one
			foreach (var other in blinds.Where(x => x != blind))
			{
				other.gameObject.SetActive(false);
			}

			Get<GameObject>(blind, "m_RecipeScrollRoot").SetActive(true);
			UpgradeBlueprints.SetGoldParchment(Get<GameObject>(blind, "m_RecipeScrollRoot"), !s_Plain);
			Get<GameObject>(blind, "m_WeaponStatsRoot").SetActive(false);
			Get<GameObject>(blind, "m_ConsumableEffectRoot").SetActive(true);
			Get<GameObject>(blind, "m_SlotRoot").SetActive(false);
			Get<GameObject>(blind, "m_MasteryTextRoot").SetActive(false);
			Get<GameObject>(blind, "m_ClassNameRoot").SetActive(false);
			Get<UISprite>(blind, "m_ItemIconSprite").gameObject.SetActive(false);
			Get<UISprite>(blind, "m_AdditionalSprite").spriteName = "Target_RedBird";
			Get<UILabel>(blind, "m_ConsumableLabel").text = "Knight's Training Manual";

			var changes = new List<UpgradeBlueprints.StatChange>
			{
				new UpgradeBlueprints.StatChange { OldIcon = "Character_Damage_Large", OldValue = "+0%", NewIcon = "Character_Damage_Large", NewValue = "+10%" },
				new UpgradeBlueprints.StatChange { OldIcon = "Character_Health_Large", OldValue = "+0%", NewIcon = "Character_Health_Large", NewValue = "+10%" }
			};
			var effectLabel = Get<UILabel>(blind, "m_ConsumableEffectInfo");
			log.AppendLine("effect label size " + effectLabel.width + "x" + effectLabel.height + " pivot " + effectLabel.pivot + " font " + effectLabel.fontSize + " scale " + effectLabel.transform.lossyScale);
			UpgradeStatChangeView.ShowInShopTile(effectLabel, changes);

			// Icon through a preview stage, as in game
			var pool = roots.SelectMany(r => r.GetComponentsInChildren<EquipmentIconPreviewPool>(true)).FirstOrDefault();
			var stages = pool == null ? null : pool.GetType().GetField("m_Stages", Fields).GetValue(pool) as List<EquipmentIconPreviewPool.PreviewStage>;
			var iconRoot = Get<Transform>(blind, "m_IconRoot");
			if (stages != null && stages.Count > 0)
			{
				var stage = stages[0];
				var icon = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(IconPrefab), stage.StageRoot);
				created.Add(icon);
				icon.transform.localPosition = Vector3.zero;
				var defaultOrtho = stage.PreviewCamera.orthographicSize;
				var factor = UpgradeStatChangeView.FitIconToStage(icon, stage.PreviewCamera);
				log.AppendLine("display factor " + factor + " ortho " + stage.PreviewCamera.orthographicSize);
				log.AppendLine("icon scale " + icon.transform.localScale.x);
				foreach (var component in icon.GetComponentsInChildren<MonoBehaviour>(true))
				{
					foreach (var method in new[] { "Awake", "OnEnable", "Start", "LateUpdate" })
					{
						var m = component.GetType().GetMethod(method, Fields, null, Type.EmptyTypes, null);
						if (m != null)
						{
							try { m.Invoke(component, null); } catch (Exception) { }
						}
					}
				}
				foreach (var r in icon.GetComponentsInChildren<Renderer>(true))
				{
					var mf = r.GetComponent<MeshFilter>();
					log.AppendLine("  renderer " + r.name + " enabled " + r.enabled + " layer " + r.gameObject.layer + " bounds " + r.bounds + " mesh " + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount.ToString() : "none") + " mat " + (r.sharedMaterial != null ? r.sharedMaterial.name : "null"));
				}
				var cam = stage.PreviewCamera;
				log.AppendLine("  stage cam pos " + cam.transform.position + " mask " + cam.cullingMask + " stage layer " + stage.StageRoot.gameObject.layer + " root pos " + stage.StageRoot.position + " active " + cam.gameObject.activeInHierarchy);
				stage.PreviewCamera.enabled = true;
				stage.PreviewCamera.Render();
				RenderTexture.active = stage.RenderTexture;
				var probe = new Texture2D(stage.RenderTexture.width, stage.RenderTexture.height, TextureFormat.RGBA32, false);
				probe.ReadPixels(new Rect(0, 0, probe.width, probe.height), 0, 0);
				RenderTexture.active = null;
				log.AppendLine("  stage rt " + probe.width + "x" + probe.height + " opaque pixels " + probe.GetPixels32().Count(c => c.a > 20));
				File.WriteAllBytes("Temp/UpgradeTilePreview_stage.png", probe.EncodeToPNG());
				Object.DestroyImmediate(probe);
				stage.PreviewCamera.orthographicSize = defaultOrtho;
				var texture = iconRoot.GetComponentInChildren<UITexture>(true);
				if (texture != null)
				{
					texture.mainTexture = stage.RenderTexture;
					texture.gameObject.SetActive(true);
					texture.width = Mathf.RoundToInt(texture.width * factor);
					texture.height = Mathf.RoundToInt(texture.height * factor);
				}
				log.AppendLine("stage rendered, ui texture " + (texture != null));
			}

			RenderTile(blind, log);
		}
		finally
		{
			foreach (var o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			if (previousActive.IsValid())
			{
				SceneManager.SetActiveScene(previousActive);
			}
			if (!alreadyOpen)
			{
				EditorSceneManager.CloseScene(scene, true);
			}
		}
	}

	private static void RenderTile(ShopOfferBlindWorldmap blind, StringBuilder log)
	{
		var root = blind.transform.root.gameObject;
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
		// The list isn't laid out in edit mode; put the tile in the middle of its scroll panel's visible region
		var clipPanel = NGUITools.FindInParents<UIPanel>(blind.transform.parent.gameObject);
		while (clipPanel != null && clipPanel.clipping == UIDrawCall.Clipping.None && clipPanel.transform.parent != null)
		{
			clipPanel = NGUITools.FindInParents<UIPanel>(clipPanel.transform.parent.gameObject);
		}
		if (clipPanel != null)
		{
			var region = clipPanel.finalClipRegion;
			var target = clipPanel.transform.TransformPoint(new Vector3(region.x, region.y, 0f));
			blind.transform.position = new Vector3(target.x, target.y, blind.transform.position.z);
			log.AppendLine("moved into panel " + clipPanel.name + " region " + region);
			foreach (var panel in UIPanel.list.ToList())
			{
				updateFrame.SetValue(updateFrame.IsStatic ? null : panel, -1);
			}
			lateUpdate.Invoke(UIPanel.list[0], null);
		}
		var camera = NGUITools.FindCameraForLayer(blind.gameObject.layer);
		if (camera == null)
		{
			log.AppendLine("no camera for layer " + blind.gameObject.layer);
			return;
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
		var tileBounds = NGUIMath.CalculateAbsoluteWidgetBounds(blind.transform);
		camera.transform.position = new Vector3(tileBounds.center.x, tileBounds.center.y, previousPosition.z);
		camera.targetTexture = rt;
		camera.Render();
		camera.targetTexture = previous;
		foreach (var renderer in hidden)
		{
			renderer.enabled = true;
		}
		RenderTexture.active = rt;
		var full = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
		full.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
		full.Apply();
		File.WriteAllBytes("Temp/UpgradeTilePreview_full.png", full.EncodeToPNG());
		Object.DestroyImmediate(full);
		RenderTexture.active = null;
		var centre = camera.WorldToViewportPoint(blind.transform.position);
		log.AppendLine("blind viewport " + centre);
		var bounds = tileBounds;
		log.AppendLine("bounds " + bounds);
		camera.transform.position = new Vector3(tileBounds.center.x, tileBounds.center.y, previousPosition.z);
		var a = camera.WorldToViewportPoint(bounds.min);
		var b = camera.WorldToViewportPoint(bounds.max);
		var x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) * rt.width) - 10, 0, rt.width - 1);
		var y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y) * rt.height) - 10, 0, rt.height - 1);
		var x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x) * rt.width) + 10, x0 + 1, rt.width);
		var y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y) * rt.height) + 10, y0 + 1, rt.height);
		camera.transform.position = previousPosition;
		RenderTexture.active = rt;
		var image = new Texture2D(x1 - x0, y1 - y0, TextureFormat.RGB24, false);
		image.ReadPixels(new Rect(x0, y0, x1 - x0, y1 - y0), 0, 0);
		image.Apply();
		RenderTexture.active = null;
		File.WriteAllBytes(OutputFile, image.EncodeToPNG());
		log.AppendLine("rendered " + (x1 - x0) + "x" + (y1 - y0) + " from camera " + camera.name);
		Object.DestroyImmediate(image);
		rt.Release();
		Object.DestroyImmediate(rt);
	}
}
