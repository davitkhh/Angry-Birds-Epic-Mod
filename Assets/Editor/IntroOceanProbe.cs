using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One-off probe: samples the intro battleground's beach (water and wet sand) animations at several times and renders the
// bottom of the screen to Temp/IntroOceanProbe_<t>.png, then reloads the scene without saving. With "hide" in the
// request, the wet sand (Ocean_Shadow) is hidden too, to see what it adds. Temp/IntroOceanProbe.request.
public static class IntroOceanProbe
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroOceanProbe.request";

	[InitializeOnLoadMethod]
	private static void Watch()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		var request = File.ReadAllText(RequestFile);
		File.Delete(RequestFile);
		try
		{
			Run(request.Contains("hide"));
		}
		catch (Exception e)
		{
			File.WriteAllText("Temp/IntroOceanProbe.log", "error: " + e);
		}
	}

	private static void Run(bool hideWetSand)
	{
		var scene = SceneManager.GetSceneByPath(ScenePath);
		if (scene.isLoaded && scene.isDirty)
		{
			File.WriteAllText("Temp/IntroOceanProbe.log", "scene has unsaved changes");
			return;
		}
		var wasLoaded = scene.isLoaded;
		if (!scene.isLoaded)
		{
			scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
		}
		var animations = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Animation>(true)).Where(a => a.name.Contains("Ocean") && a.clip != null).ToList();
		if (hideWetSand)
		{
			foreach (var a in animations.Where(a => a.name.Contains("Shadow")))
			{
				a.gameObject.SetActive(false);
			}
		}
		// Edit mode skins a mesh once: have them follow the sampled bones every render.
		foreach (var skinned in animations.SelectMany(an => an.GetComponentsInChildren<SkinnedMeshRenderer>(true)))
		{
			skinned.forceMatrixRecalculationPerRender = true;
		}
		var saved = animations.SelectMany(a => a.GetComponentsInChildren<Transform>(true)).Select(t => new { t, p = t.localPosition, r = t.localRotation, s = t.localScale }).ToList();
		var log = "animations: " + string.Join(", ", animations.Select(a => a.name + "=" + AssetDatabase.GetAssetPath(a.clip)).ToArray()) + "\n";
		var cameraObject = new GameObject("IntroOceanProbeCamera");
		SceneManager.MoveGameObjectToScene(cameraObject, scene);
		var camera = cameraObject.AddComponent<Camera>();
		camera.scene = scene;
		camera.orthographic = true;
		var rect = new Rect(-733.7f, -346.2f, 1368f, 260f);
		camera.orthographicSize = rect.height * 0.5f;
		camera.aspect = rect.width / rect.height;
		camera.transform.position = new Vector3(rect.center.x, rect.center.y, -500f);
		camera.nearClipPlane = 0.01f;
		camera.farClipPlane = 2000f;
		camera.cullingMask = -1;
		camera.clearFlags = CameraClearFlags.SolidColor;
		camera.backgroundColor = Color.magenta;
		var target = new RenderTexture(1029, 196, 24);
		camera.targetTexture = target;
		for (var t = 0f; t < 6f; t += 0.5f)
		{
			foreach (var a in animations)
			{
				a.clip.SampleAnimation(a.gameObject, t);
			}
			var center = animations.SelectMany(an => an.GetComponentsInChildren<Transform>(true)).FirstOrDefault(tr => tr.name == "B_Center");
			log += t.ToString("F1") + " B_Center scale " + (center != null ? center.localScale.ToString("F3") : "-") + "\n";
			camera.Render();
			RenderTexture.active = target;
			var image = new Texture2D(1029, 196, TextureFormat.RGB24, false);
			image.ReadPixels(new Rect(0, 0, 1029, 196), 0, 0);
			RenderTexture.active = null;
			File.WriteAllBytes("Temp/IntroOceanProbe_" + (hideWetSand ? "hide_" : string.Empty) + t.ToString("F1") + ".png", image.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(image);
		}
		camera.targetTexture = null;
		UnityEngine.Object.DestroyImmediate(cameraObject);
		UnityEngine.Object.DestroyImmediate(target);
		// Put the sampled poses back.
		foreach (var item in saved)
		{
			item.t.localPosition = item.p;
			item.t.localRotation = item.r;
			item.t.localScale = item.s;
		}
		foreach (var a in animations)
		{
			a.gameObject.SetActive(true);
		}
		if (!wasLoaded)
		{
			EditorSceneManager.CloseScene(scene, true);
		}
		File.WriteAllText("Temp/IntroOceanProbe.log", log + "done");
	}
}
