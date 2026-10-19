using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Renders the intro battleground's camp around the egg nest (scenery only: world z 550 to 650, so no characters or
// foreground) to Temp/IntroNestRender.png, 8 pixels per world unit, for the empty-nest patch the raid shows once Wiz Pig
// takes the eggs. The rendered rect is written to Temp/IntroNestRender.log. Temp/IntroNestRender.request.
public static class IntroNestRender
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroNestRender.request";

	public static readonly Rect WorldRect = new Rect(-300f, 205f, 120f, 90f);

	public const float PixelsPerUnit = 8f;

	[InitializeOnLoadMethod]
	private static void WatchForRequests()
	{
		EditorApplication.update -= PollRequest;
		EditorApplication.update += PollRequest;
	}

	private static void PollRequest()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		var request = File.ReadAllText(RequestFile).Trim();
		File.Delete(RequestFile);
		try
		{
			Render(request);
		}
		catch (Exception e)
		{
			File.WriteAllText("Temp/IntroNestRender.log", "error: " + e);
		}
	}

	// The request may hold "x y width height pixelsWide pixelsHigh nearZ farZ output [scenePath [hiddenNames]]" to render
	// another rect, optionally of another scene with objects of those names (comma separated) hidden.
	private static void Render(string request)
	{
		var rect = WorldRect;
		var width = Mathf.RoundToInt(WorldRect.width * PixelsPerUnit);
		var height = Mathf.RoundToInt(WorldRect.height * PixelsPerUnit);
		var nearZ = 550f;
		var farZ = 650f;
		var output = "Temp/IntroNestRender.png";
		var values = request.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (values.Length >= 9)
		{
			var f = new float[8];
			for (var i = 0; i < 8; i++)
			{
				f[i] = float.Parse(values[i], System.Globalization.CultureInfo.InvariantCulture);
			}
			rect = new Rect(f[0], f[1], f[2], f[3]);
			width = (int)f[4];
			height = (int)f[5];
			nearZ = f[6];
			farZ = f[7];
			output = values[8];
		}
		var scenePath = values.Length >= 10 ? values[9] : ScenePath;
		var hidden = values.Length >= 11 ? values[10].Split(',') : new string[0];
		var scene = SceneManager.GetSceneByPath(scenePath);
		var opened = false;
		if (!scene.isLoaded)
		{
			scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			opened = true;
		}
		var hiddenObjects = new System.Collections.Generic.List<GameObject>();
		foreach (var root in scene.GetRootGameObjects())
		{
			foreach (var t in root.GetComponentsInChildren<Transform>(false))
			{
				if (System.Array.IndexOf(hidden, t.name) >= 0)
				{
					hiddenObjects.Add(t.gameObject);
				}
			}
		}
		hiddenObjects.ForEach(g => g.SetActive(false));
		var cameraObject = new GameObject("IntroNestRenderCamera");
		SceneManager.MoveGameObjectToScene(cameraObject, scene);
		var camera = cameraObject.AddComponent<Camera>();
		camera.scene = scene;
		camera.orthographic = true;
		camera.orthographicSize = rect.height * 0.5f;
		camera.aspect = rect.width / rect.height;
		camera.transform.position = new Vector3(rect.center.x, rect.center.y, nearZ);
		camera.nearClipPlane = 0.01f;
		camera.farClipPlane = farZ - nearZ;
		camera.cullingMask = scenePath == ScenePath ? 1 << 9 : -1;
		camera.clearFlags = CameraClearFlags.SolidColor;
		camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
		var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
		camera.targetTexture = target;
		camera.Render();
		RenderTexture.active = target;
		var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
		image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
		image.Apply();
		RenderTexture.active = null;
		camera.targetTexture = null;
		File.WriteAllBytes(output, image.EncodeToPNG());
		UnityEngine.Object.DestroyImmediate(cameraObject);
		UnityEngine.Object.DestroyImmediate(target);
		hiddenObjects.ForEach(g => g.SetActive(true));
		File.WriteAllText("Temp/IntroNestRender.log", "rendered " + output + " " + width + "x" + height + " world rect " + rect + " z " + nearZ + " to " + farZ + "\n");
		if (opened)
		{
			EditorSceneManager.CloseScene(scene, true);
		}
	}
}
