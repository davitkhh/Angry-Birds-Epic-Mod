using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Edit-mode preview of bonus perks shown as separate perks (BonusPerkRows): the bird window's equipment row
// (Window_BirdManager Info_Equipment) and the equipment hold overlay (InfoOverlays InfoOverlay_Equipment), with a mock
// Vigor shield that has a Bulwark bonus perk. Request: touch Temp/BonusPerkPreview.request. Output:
// Temp/BonusPerkPreview_row.png, Temp/BonusPerkPreview_overlay.png (+ .log). Scenes that are open in the editor are skipped.
public static class BonusPerkPreview
{
	private const string RequestFile = "Temp/BonusPerkPreview.request";

	private const string LogFile = "Temp/BonusPerkPreview.log";

	private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

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
			var bonus = new List<EquipmentPerk> { new EquipmentPerk { Type = PerkType.Bulwark, ProbablityInPercent = 100f, PerkValue = 20f } };
			Render("Assets/Scenes/2_Camp/2_Interface/Window_BirdManager.unity", "Info_Equipment", "Temp/BonusPerkPreview_row.png", log, root =>
			{
				var info = root.GetComponent<EquipmentItemInfo>();
				var sprite = (UISprite)typeof(EquipmentItemInfo).GetField("m_PerkSprite", Flags).GetValue(info);
				var name = (UILabel)typeof(EquipmentItemInfo).GetField("m_PerkName", Flags).GetValue(info);
				var equipmentName = (UILabel)typeof(EquipmentItemInfo).GetField("m_EquipmentName", Flags).GetValue(info);
				sprite.spriteName = "PassiveEffect_Vigor";
				name.text = "Vigor";
				if (equipmentName != null)
				{
					equipmentName.text = "Shield";
				}
				BonusPerkRows.ApplyToRow(sprite.cachedTransform.parent, name, bonus, p => "Bulwark");
			});
			Render("Assets/Scenes/0_Generic/1_Interface/InfoOverlays.unity", "InfoOverlay_Equipment", "Temp/BonusPerkPreview_overlay.png", log, root =>
			{
				var overlay = root.GetComponent<EquipmentOverlay>();
				overlay.m_Header.text = "Shield";
				overlay.m_PerkIcon.spriteName = "PassiveEffect_Vigor";
				BonusPerkRows.ApplyToOverlay(overlay.m_PerkIcon.cachedTransform.parent, overlay.m_PerkDesc, bonus, "Reduces taken damage by [00a800]5%[-].", p => "[c8a000]Bulwark:[-] Red's support shields protect allies [00a800]+20%[-] more.");
			});
		}
		catch (Exception e)
		{
			log.AppendLine("EXCEPTION " + e);
		}
		File.WriteAllText(LogFile, log.ToString());
	}

	private static void Render(string scenePath, string rootName, string output, StringBuilder log, Action<GameObject> setup)
	{
		if (SceneManager.GetSceneByPath(scenePath).isLoaded)
		{
			log.AppendLine(scenePath + " is open in the editor; skipped");
			return;
		}
		var previousActive = SceneManager.GetActiveScene();
		var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
		try
		{
			SceneManager.SetActiveScene(scene);
			var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
			var target = all.First(t => t.name == rootName).gameObject;
			for (var t = target.transform; t != null; t = t.parent)
			{
				t.gameObject.SetActive(true);
			}
			// Same-named siblings (other list templates) would draw on top
			foreach (Transform sibling in target.transform.parent)
			{
				if (sibling != target.transform)
				{
					sibling.gameObject.SetActive(false);
				}
			}
			foreach (var animation in target.GetComponentsInChildren<Animation>(true))
			{
				animation.enabled = false;
			}
			setup(target);
			RenderRoot(target, output, log);
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

	private static void RenderRoot(GameObject target, string output, StringBuilder log)
	{
		var root = target.transform.root.gameObject;
		foreach (var rect in root.GetComponentsInChildren<UIRect>().OrderBy(r => r is UIPanel ? 0 : 1))
		{
			foreach (var method in new[] { "OnEnable", "Start", "Update" })
			{
				var m = rect.GetType().GetMethod(method, Flags, null, Type.EmptyTypes, null);
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
		var camera = NGUITools.FindCameraForLayer(target.layer);
		if (camera == null)
		{
			log.AppendLine("no camera for layer " + target.layer);
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
		var bounds = NGUIMath.CalculateAbsoluteWidgetBounds(target.transform);
		var rt = new RenderTexture(1600, 900, 24);
		var previous = camera.targetTexture;
		var previousPosition = camera.transform.position;
		var previousOrtho = camera.orthographicSize;
		camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, previousPosition.z);
		camera.orthographicSize = Mathf.Max(previousOrtho, bounds.extents.y * 1.15f, bounds.extents.x * 1.15f * 900f / 1600f);
		camera.targetTexture = rt;
		camera.Render();
		camera.targetTexture = previous;
		camera.transform.position = previousPosition;
		camera.orthographicSize = previousOrtho;
		foreach (var renderer in hidden)
		{
			renderer.enabled = true;
		}
		RenderTexture.active = rt;
		var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
		image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
		image.Apply();
		RenderTexture.active = null;
		File.WriteAllBytes(output, image.EncodeToPNG());
		log.AppendLine("rendered " + output + " bounds " + bounds + " camera " + camera.name);
		Object.DestroyImmediate(image);
		rt.Release();
		Object.DestroyImmediate(rt);
	}
}
