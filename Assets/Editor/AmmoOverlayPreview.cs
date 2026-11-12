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

// Preview of the ammo UI in the bird character overlay (InfoOverlays.unity), rendered in edit mode with mock ammo:
// the Trickster's attack plate (ammo bar + "[arrow] x1"), support plate ("+2") and the regen charge bar.
// Request: Temp/AmmoOverlayPreview.request. Output: Temp/AmmoOverlayPreview_<ammo>.png (+ .log). Scene closed unsaved.
public static class AmmoOverlayPreview
{
	private const string RequestFile = "Temp/AmmoOverlayPreview.request";

	private const string LogFile = "Temp/AmmoOverlayPreview.log";

	private const string ScenePath = "Assets/Scenes/0_Generic/1_Interface/InfoOverlays.unity";

	private const string ProjectilePrefab = "Assets/Resources/Prefabs/4_Characters/4_Projectiles/Projectile_BlueBirds_Bow_01.prefab";

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

	private static T Get<T>(object o, string field) where T : class
	{
		return o.GetType().GetField(field, Fields).GetValue(o) as T;
	}

	private static void Render(StringBuilder log)
	{
		var previousActive = SceneManager.GetActiveScene();
		var alreadyOpen = SceneManager.GetSceneByPath(ScenePath).isLoaded;
		if (alreadyOpen)
		{
			log.AppendLine("InfoOverlays is open in the editor; close it first");
			return;
		}
		var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
		try
		{
			SceneManager.SetActiveScene(scene);
			var overlay = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterOverlay>(true)).First(o => o.name == "InfoOverlay_Character");
			for (var t = overlay.transform; t != null; t = t.parent)
			{
				t.gameObject.SetActive(true);
			}
			var animation = overlay.GetComponent<Animation>();
			var enter = animation != null ? animation.GetClip("InfoOverlay_Enter") : null;
			if (enter != null)
			{
				enter.SampleAnimation(overlay.gameObject, enter.length);
			}
			foreach (var p in overlay.GetComponentsInChildren<UIPanel>(true))
			{
				log.AppendLine("panel " + p.name + " alpha " + p.alpha + " layer " + p.gameObject.layer);
			}
			log.AppendLine("anim scale " + overlay.transform.Find("Animation").localScale);
			Get<UILabel>(overlay, "m_Header").text = "Tricksters";
			Get<UILabel>(overlay, "m_HealthText").text = "1234/1234";
			var power = Get<UILabel>(overlay, "m_PowerLevelText");
			power.gameObject.SetActive(true);
			power.text = "Power: 1234";
			var powerIcon = Get<GameObject>(overlay, "m_PowerLevelIcon");
			if (powerIcon != null)
			{
				powerIcon.SetActive(false);
			}
			Get<UISprite>(overlay, "m_HealthBar").fillAmount = 0.8f;
			var effects = overlay.m_EffectsRoot;
			effects.gameObject.SetActive(false);
			var blinds = Get<List<SkillBlind>>(overlay, "m_SkillBlinds");
			var icon = AmmoUI.GetIconFromSprites(AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePrefab));
			log.AppendLine("icon " + (icon != null ? (icon.Texture != null ? icon.Texture.name + " " + icon.Uv + " " + icon.Size : icon.SpriteName) : "null"));
			var names = new[] { "Itching Powder", "Cheer", "Rage" };
			var colors = new[] { Get<object>(blinds[0], "m_OffensiveColor"), Get<object>(blinds[0], "m_SupportColor"), Get<object>(blinds[0], "m_PassiveColor") };
			for (var i = 0; i < blinds.Count; i++)
			{
				Get<UILabel>(blinds[i], "m_SkillName").text = names[Mathf.Min(i, 2)];
				Get<UILabel>(blinds[i], "m_SkillDescription").text = "Deals 1234 damage and removes all buffs from the target.";
				var charge = blinds[i].ChargeProgressTemplate;
				if (charge != null)
				{
					charge.SetActive(false);
				}
			}
			foreach (var ammo in new[] { 10, 4, 12, 23 })
			{
				for (var i = 0; i < 2; i++)
				{
					var plate = Get<UISprite>(blinds[i], "m_Background");
					plate.color = (Color)colors[i];
					AmmoUI.ShowOnSkillPlate(plate, Get<UILabel>(blinds[i], "m_SkillName"), i == 0 ? 1 : 0, i == 1 ? 2 : 0, ammo, 10, icon, null);
					if (i == 0)
					{
						// Weapon charge line: half at 10 ammo, full at 4, none at 12, full over an overflow bar at 23
						AmmoUI.ShowChargeDots(plate, ammo == 10 ? 1 : ammo == 12 ? 0 : 2, 2);
					}
				}
				var row = Get<Transform>(overlay, "m_HealthBarTransform");
				Vector3? home = null;
				AmmoUI.ShowRegenBar(row, Get<UISprite>(overlay, "m_HealthBar"), power, power, blinds[0].ChargeProgressTemplate, 1, ammo % 3, 3, icon, ref home);
				GameObject popup = null;
				if (ammo == 4)
				{
					// Ammo gain popup content, drawn over the paper for a look (in battle it floats beside the bird)
					popup = new GameObject("PopupPreview");
					popup.layer = overlay.gameObject.layer;
					popup.transform.parent = overlay.transform.Find("Animation/1_CharacterInfo");
					popup.transform.localPosition = new Vector3(-120f, -300f, -20f);
					popup.transform.localRotation = Quaternion.identity;
					popup.transform.localScale = Vector3.one;
					var combatText = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/CombatText_Value.prefab").GetComponent<ScrollingCombatText>();
					log.AppendLine("popup width " + AmmoGainPopup.BuildContent(popup.transform, combatText.m_LabelHealthChange, 2, icon, out _));
				}
				RenderOverlay(overlay, "Temp/AmmoOverlayPreview_" + ammo + ".png", log);
				if (popup != null)
				{
					Object.DestroyImmediate(popup);
				}
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

	private static void RenderOverlay(CharacterOverlay overlay, string output, StringBuilder log)
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
		log.AppendLine("drawcalls " + UIDrawCall.activeList.size);
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
		var info = overlay.transform.Find("Animation/1_CharacterInfo");
		var bounds = NGUIMath.CalculateAbsoluteWidgetBounds(info);
		camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, previousPosition.z);
		camera.targetTexture = rt;
		camera.Render();
		camera.targetTexture = previous;
		foreach (var renderer in hidden)
		{
			renderer.enabled = true;
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
