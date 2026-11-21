using System;
using System.IO;
using System.Linq;
using SmoothMoves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Probe for ChristmasPresentView: in play mode at a fixed 30 fps, throws every ChristmasPresent_<Name> prefab from a
// "sack mouth" on the right onto a row of landing spots on the left (a MinionPig stands among them for scale), then
// renders frames to Logs/ChristmasPresentsProbe/. Trigger: touch Temp/ChristmasPresentsProbe.request (open editor) or
// Unity -batchmode -projectPath . -executeMethod ChristmasPresentsProbe.Run (editor closed; it quits when done).
[InitializeOnLoad]
public static class ChristmasPresentsProbe
{
	private const string Dir = "Logs/ChristmasPresentsProbe";

	private const string LogFile = Dir + "/log.txt";

	private const string StateKey = "ChristmasPresentsProbe.State";

	private const string ScenesKey = "ChristmasPresentsProbe.Scenes";

	private const string QuitKey = "ChristmasPresentsProbe.Quit";

	private const string RequestFile = "Temp/ChristmasPresentsProbe.request";

	private static readonly Vector3 SackMouth = new Vector3(420f, 190f, 0f);

	private static int s_Start = -1;

	private static int s_LastFrame = -1;

	private static ChristmasPresentView[] s_Presents;

	static ChristmasPresentsProbe()
	{
		EditorApplication.update -= Update;
		EditorApplication.update += Update;
	}

	public static void Run()
	{
		Directory.CreateDirectory(Dir);
		foreach (var file in Directory.GetFiles(Dir))
		{
			File.Delete(file);
		}
		var open = Enumerable.Range(0, EditorSceneManager.sceneCount).Select(EditorSceneManager.GetSceneAt).ToList();
		if (open.Any(s => s.isDirty))
		{
			Log("an open scene has unsaved changes; save it and request again");
			return;
		}
		EditorPrefs.SetBool(QuitKey, Application.isBatchMode);
		EditorPrefs.SetString(ScenesKey, string.Join("|", open.Where(s => !string.IsNullOrEmpty(s.path)).Select(s => s.path).ToArray()));
		s_Start = -1;
		s_LastFrame = -1;
		EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
		EditorPrefs.SetString(StateKey, "playing");
		EditorApplication.EnterPlaymode();
	}

	private static void Log(string line)
	{
		Directory.CreateDirectory(Dir);
		File.AppendAllText(LogFile, line + "\n");
	}

	private static void Update()
	{
		var state = EditorPrefs.GetString(StateKey, string.Empty);
		if (state == string.Empty)
		{
			if (File.Exists(RequestFile) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
			{
				File.Delete(RequestFile);
				Run();
			}
			return;
		}
		if (state == "restore" && !EditorApplication.isPlayingOrWillChangePlaymode)
		{
			EditorPrefs.DeleteKey(StateKey);
			var scenes = EditorPrefs.GetString(ScenesKey, string.Empty).Split('|').Where(s => s.Length > 0).ToArray();
			for (var i = 0; i < scenes.Length; i++)
			{
				EditorSceneManager.OpenScene(scenes[i], i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive);
			}
			Log("done");
			if (EditorPrefs.GetBool(QuitKey, false))
			{
				EditorPrefs.DeleteKey(QuitKey);
				EditorApplication.Exit(0);
			}
			return;
		}
		if (state != "playing" || !EditorApplication.isPlaying)
		{
			return;
		}
		try
		{
			Step();
		}
		catch (Exception e)
		{
			Log("error: " + e);
			Finish();
		}
	}

	private static void Finish()
	{
		EditorPrefs.SetString(StateKey, "restore");
		EditorApplication.ExitPlaymode();
	}

	private static void Step()
	{
		if (Time.frameCount == s_LastFrame)
		{
			return;
		}
		s_LastFrame = Time.frameCount;
		if (s_Start < 0)
		{
			s_Start = Time.frameCount;
			Time.captureFramerate = 30;
		}
		var f = Time.frameCount - s_Start;
		if (f == 2)
		{
			SpawnPig(new Vector3(-80f, 0f, 5f));
			var names = ChristmasPresentView.PresentNames;
			Log("presents: " + string.Join(", ", names));
			s_Presents = new ChristmasPresentView[names.Length];
			for (var i = 0; i < names.Length; i++)
			{
				var prefab = UnityEngine.Resources.Load<GameObject>("Prefabs/0_Generic/4_ChristmasPresents/ChristmasPresent_" + names[i]);
				if (prefab == null)
				{
					Log("missing prefab for " + names[i]);
					continue;
				}
				var go = UnityEngine.Object.Instantiate(prefab);
				// two rows like a pig formation: back row higher on screen
				var row = i % 2;
				go.transform.position = new Vector3(-430f + i * 105f + row * 20f, row * 70f, row * 10f);
				s_Presents[i] = go.GetComponent<ChristmasPresentView>();
				s_Presents[i].m_IdleJiggle = false;
				var index = i;
				s_Presents[i].Landed += p => Log("landed " + names[index] + " at frame " + (Time.frameCount - s_Start));
				s_Presents[i].Toss(SackMouth, i * 0.18f);
				Log(names[i] + " height " + s_Presents[i].Height);
			}
		}
		if (f == 75 && s_Presents != null && s_Presents[0] != null)
		{
			// an idle jiggle, to see it too
			s_Presents[0].Jolt(0.35f, 5f);
			s_Presents[3].Jolt(0.35f, -5f);
		}
		if (f >= 3 && f <= 110)
		{
			Render(Dir + "/f_" + f.ToString("000") + ".png");
		}
		if (f == 112)
		{
			Finish();
		}
	}

	private static void SpawnPig(Vector3 position)
	{
		var prefab = UnityEngine.Resources.Load<GameObject>("Prefabs/4_Characters/8_Base/MinionPig");
		if (prefab == null)
		{
			Log("no MinionPig prefab");
			return;
		}
		var holder = new GameObject("ProbeHolder");
		holder.SetActive(false);
		var rig = UnityEngine.Object.Instantiate(prefab, holder.transform);
		foreach (var behaviour in rig.GetComponents<MonoBehaviour>().Where(m => !(m is BoneAnimation)).ToList())
		{
			UnityEngine.Object.DestroyImmediate(behaviour);
		}
		rig.transform.SetParent(null, false);
		UnityEngine.Object.Destroy(holder);
		rig.transform.position = position;
		rig.GetComponent<BoneAnimation>().Play("Idle");
	}

	private static void Render(string path)
	{
		const int w = 960;
		const int h = 540;
		var camObject = new GameObject("ProbeCamera");
		var cam = camObject.AddComponent<Camera>();
		cam.orthographic = true;
		cam.orthographicSize = h / 2f;
		cam.transform.position = new Vector3(0f, 170f, -3000f);
		cam.farClipPlane = 10000f;
		cam.clearFlags = CameraClearFlags.SolidColor;
		cam.backgroundColor = new Color(0.84f, 0.9f, 0.84f, 1f);
		var target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
		cam.targetTexture = target;
		cam.Render();
		RenderTexture.active = target;
		var image = new Texture2D(w, h, TextureFormat.RGBA32, false);
		image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
		RenderTexture.active = null;
		File.WriteAllBytes(path, image.EncodeToPNG());
		cam.targetTexture = null;
		UnityEngine.Object.DestroyImmediate(camObject);
		UnityEngine.Object.Destroy(image);
		target.Release();
	}
}
