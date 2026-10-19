using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-off probe: renders the intro raid's corporal (MinionPigE) and a plain rogue (MinionPigB) in play mode, with and
// without SceneRigGear's corporal swap, to see whether the swap shows. Copies both rigs into temporary prefabs, plays an
// empty scene, captures Temp/SceneRigGearProbe_*.png, then restores the open scenes. Temp/SceneRigGearProbe.request.
[InitializeOnLoad]
public static class SceneRigGearProbe
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/SceneRigGearProbe.request";

	private const string LogFile = "Temp/SceneRigGearProbe.log";

	private const string ProbeFolder = "Assets/_SceneRigGearProbe";

	private const string StateKey = "SceneRigGearProbe.State";

	private const string ScenesKey = "SceneRigGearProbe.Scenes";

	private static int s_Frame;

	private static readonly string[] RedClips = { "Action_Attack_1", "Hit" };

	private static GameObject[] s_Variants;

	private static readonly string[] Names = { "Red_hr" };

	static SceneRigGearProbe()
	{
		EditorApplication.update -= Update;
		EditorApplication.update += Update;
	}

	private static void Log(string line)
	{
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
				File.WriteAllText(LogFile, string.Empty);
				try
				{
					Prepare();
				}
				catch (Exception e)
				{
					Log("error: " + e);
					EditorPrefs.DeleteKey(StateKey);
				}
			}
			return;
		}
		if (state == "playing" && EditorApplication.isPlaying)
		{
			try
			{
				PlayStep();
			}
			catch (Exception e)
			{
				Log("error: " + e);
				EditorPrefs.SetString(StateKey, "restore");
				EditorApplication.ExitPlaymode();
			}
		}
		else if (state == "restore" && !EditorApplication.isPlayingOrWillChangePlaymode)
		{
			EditorPrefs.DeleteKey(StateKey);
			var scenes = EditorPrefs.GetString(ScenesKey, string.Empty).Split('|').Where(s => s.Length > 0).ToArray();
			for (var i = 0; i < scenes.Length; i++)
			{
				EditorSceneManager.OpenScene(scenes[i], i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive);
			}
			AssetDatabase.DeleteAsset(ProbeFolder);
			Log("restored " + scenes.Length + " scene(s), done");
		}
	}

	private static void Prepare()
	{
		var open = Enumerable.Range(0, EditorSceneManager.sceneCount).Select(EditorSceneManager.GetSceneAt).ToList();
		if (open.Any(s => s.isDirty))
		{
			Log("an open scene has unsaved changes; save it and request again");
			return;
		}
		EditorPrefs.SetString(ScenesKey, string.Join("|", open.Where(s => !string.IsNullOrEmpty(s.path)).Select(s => s.path).ToArray()));
		if (!AssetDatabase.IsValidFolder(ProbeFolder))
		{
			AssetDatabase.CreateFolder("Assets", "_SceneRigGearProbe");
		}
		EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
		EditorPrefs.SetString(StateKey, "playing");
		s_Frame = 0;
		EditorApplication.EnterPlaymode();
	}

	private static void PlayStep()
	{
		s_Frame++;
		if (s_Frame == 3)
		{
			var holder = new GameObject("Probe_Wiz");
			var rig = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/4_Characters/2_Pigs/WizPig"), holder.transform);
			foreach (var behaviour in rig.GetComponents<MonoBehaviour>().Where(m => m.GetType().Name != "BoneAnimation").ToList())
			{
				UnityEngine.Object.DestroyImmediate(behaviour);
			}
			rig.transform.localPosition = Vector3.zero;
			var cloud = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/0_Generic/3_Props/Prop_MagicCloud_Battle.prefab"));
			cloud.transform.position = new Vector3(1000f, 0f, 0f);
			s_Variants = new[] { rig, cloud };
		}
		else if (s_Frame == 6 && s_Variants != null)
		{
			Play(s_Variants[0], "HoveringState");
		}
		else if (s_Frame == 12 && s_Variants != null)
		{
			Play(s_Variants[0], "FlyingState");
		}
		else if (s_Frame == 24 && s_Variants != null)
		{
			Play(s_Variants[0], "HoveringState");
		}
		if (s_Frame >= 8 && s_Frame <= 32 && s_Variants != null)
		{
			var rig = s_Variants[0].transform;
			var shadow = rig.GetComponentsInChildren<Transform>(true).First(t => t.name == "Shadow");
			var sprite = shadow.Find("Shadow_Sprite");
			var smr = rig.GetComponent<SkinnedMeshRenderer>();
			var bones = smr.bones;
			Log(s_Frame + " shadow " + shadow.localScale + " sprite " + (sprite != null ? sprite.localScale.ToString() : "-") + " spriteActive " + (sprite != null && sprite.gameObject.activeInHierarchy) + " inBones " + (sprite != null && System.Array.IndexOf(bones, sprite) >= 0) + "/" + (System.Array.IndexOf(bones, shadow) >= 0));
			if (s_Frame == 10 || s_Frame == 13 || s_Frame == 14 || s_Frame == 25 || s_Frame == 26)
			{
				Render(s_Variants[0], "Temp/SceneRigGearProbe_wiz_" + s_Frame + ".png");
			}
		}
		if (s_Frame == 34 && s_Variants != null)
		{
			Render(s_Variants[1], "Temp/SceneRigGearProbe_cloud.png");
			var cloud = s_Variants[1].transform;
			foreach (var r in cloud.GetComponentsInChildren<Renderer>(true))
			{
				Log("cloud renderer " + r.name + " enabled " + r.enabled + " active " + r.gameObject.activeInHierarchy + " bounds " + r.bounds + " mat " + (r.sharedMaterial != null ? r.sharedMaterial.name + "/" + r.sharedMaterial.shader.name : "null") + " layer " + r.gameObject.layer);
			}
		}
		else if (s_Frame == 38)
		{
			EditorPrefs.SetString(StateKey, "restore");
			EditorApplication.ExitPlaymode();
		}
	}

	private static void Play(GameObject rig, string clip)
	{
		var bones = rig.GetComponents<MonoBehaviour>().First(m => m.GetType().Name == "BoneAnimation");
		bones.GetType().GetMethod("Play", new[] { typeof(string) }).Invoke(bones, new object[] { clip });
	}

	private static void Render(GameObject rig, string path)
	{
		var renderer = (Renderer)rig.GetComponentInChildren<SkinnedMeshRenderer>() ?? rig.GetComponentInChildren<Renderer>();
		var bounds = renderer.bounds;
		foreach (var other in UnityEngine.Object.FindObjectsOfType<Renderer>())
		{
			if (other.enabled && other.gameObject.activeInHierarchy && Mathf.Abs(other.bounds.center.x - bounds.center.x) < 400f)
			{
				bounds.Encapsulate(other.bounds);
				Log("  renderer " + other.name + " " + other.bounds);
			}
		}
		var camObject = new GameObject("ProbeCamera");
		var cam = camObject.AddComponent<Camera>();
		cam.orthographic = true;
		cam.orthographicSize = 260f;
		cam.transform.position = rig.transform.position + new Vector3(0f, 90f, -3000f);
		cam.farClipPlane = 10000f;
		cam.clearFlags = CameraClearFlags.SolidColor;
		cam.backgroundColor = new Color(0.8f, 0.8f, 0.8f, 1f);
		cam.cullingMask = -1;
		var target = new RenderTexture(512, 512, 24);
		cam.targetTexture = target;
		cam.Render();
		RenderTexture.active = target;
		var image = new Texture2D(512, 512, TextureFormat.RGB24, false);
		image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
		RenderTexture.active = null;
		File.WriteAllBytes(path, image.EncodeToPNG());
		cam.targetTexture = null;
		UnityEngine.Object.DestroyImmediate(camObject);
		Log("rendered " + path + " bounds " + bounds);
	}
}
