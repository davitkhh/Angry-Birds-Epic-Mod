using System;
using System.IO;
using System.Linq;
using SmoothMoves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-off probe: renders PigPirate in play mode bare-headed and with each pirate headgear, and logs the Headgear bone
// transforms, to fit new headgear art. Touch Temp/PirateHeadgearProbe.request; output Temp/PirateHeadgearProbe*.
[InitializeOnLoad]
public static class PirateHeadgearProbe
{
	private const string LogFile = "Temp/PirateHeadgearProbe.log";

	private const string StateKey = "PirateHeadgearProbe.State";

	private const string ScenesKey = "PirateHeadgearProbe.Scenes";

	private const string RequestFile = "Temp/PirateHeadgearProbe.request";

	private static readonly string[] Headgears = { "", "Headgear_PirateBucket_1", "Headgear_PirateBucket_2", "Headgear_PirateBucket_3", "Headgear_PirateBucket_4" };

	private static int s_Frame;

	private static GameObject[] s_Rigs;

	static PirateHeadgearProbe()
	{
		EditorApplication.update -= Update;
		EditorApplication.update += Update;
	}

	public static void Run()
	{
		File.WriteAllText(LogFile, string.Empty);
		var open = Enumerable.Range(0, EditorSceneManager.sceneCount).Select(EditorSceneManager.GetSceneAt).ToList();
		if (open.Any(s => s.isDirty))
		{
			Log("an open scene has unsaved changes; save it and request again");
			return;
		}
		EditorPrefs.SetString(ScenesKey, string.Join("|", open.Where(s => !string.IsNullOrEmpty(s.path)).Select(s => s.path).ToArray()));
		s_Frame = 0;
		EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
		EditorPrefs.SetString(StateKey, "playing");
		EditorApplication.EnterPlaymode();
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
			Log("restored " + scenes.Length + " scene(s), done");
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
		s_Frame++;
		if (s_Frame == 2)
		{
			var prefab = UnityEngine.Resources.Load<GameObject>("Prefabs/4_Characters/2_Pigs/PigPirate");
			s_Rigs = new GameObject[Headgears.Length];
			for (var i = 0; i < Headgears.Length; i++)
			{
				var rig = UnityEngine.Object.Instantiate(prefab);
				foreach (var behaviour in rig.GetComponents<MonoBehaviour>().Where(m => !(m is BoneAnimation)).ToList())
				{
					UnityEngine.Object.DestroyImmediate(behaviour);
				}
				rig.transform.position = new Vector3(i * 2000f, 0f, 0f);
				s_Rigs[i] = rig;
			}
			Log("root scale " + prefab.transform.localScale);
		}
		else if (s_Frame == 5)
		{
			for (var i = 0; i < Headgears.Length; i++)
			{
				var anim = s_Rigs[i].GetComponent<BoneAnimation>();
				var bone = anim.mBoneSource.First(b => b.boneName == "Headgear");
				var frame = anim.triggerFrames.First(f => f.GetTriggerFrameBone(bone.boneNodeIndex) != null && !string.IsNullOrEmpty(f.GetTriggerFrameBone(bone.boneNodeIndex).originalTextureGUID));
				var guid = frame.GetTriggerFrameBone(bone.boneNodeIndex).originalTextureGUID;
				var atlas = anim.textureAtlases.First(a => a.GetTextureIndex(guid) != -1);
				var original = atlas.textureNames[atlas.GetTextureIndex(guid)];
				if (i == 0)
				{
					Log("original headgear " + atlas.name + "/" + original);
					anim.HideBone("Headgear", true);
					anim.HideBone("Headgear_Back", true);
				}
				else
				{
					var target = anim.textureAtlases.First(a => a.textureNames.Contains(Headgears[i]));
					anim.SwapBoneTexture("Headgear", atlas.name, original, target.name, Headgears[i]);
					var backBone = anim.mBoneSource.First(b => b.boneName == "Headgear_Back");
					var backFrame = anim.triggerFrames.FirstOrDefault(f => f.GetTriggerFrameBone(backBone.boneNodeIndex) != null && !string.IsNullOrEmpty(f.GetTriggerFrameBone(backBone.boneNodeIndex).originalTextureGUID));
					var backTarget = anim.textureAtlases.FirstOrDefault(a => a.textureNames.Contains(Headgears[i] + "_Back"));
					if (backFrame != null && backTarget != null)
					{
						var backGuid = backFrame.GetTriggerFrameBone(backBone.boneNodeIndex).originalTextureGUID;
						var backAtlas = anim.textureAtlases.First(a => a.GetTextureIndex(backGuid) != -1);
						anim.HideBone("Headgear_Back", false);
						anim.SwapBoneTexture("Headgear_Back", backAtlas.name, backAtlas.textureNames[backAtlas.GetTextureIndex(backGuid)], backTarget.name, Headgears[i] + "_Back");
						Log(i + " back swapped from " + backAtlas.name + "/" + backAtlas.textureNames[backAtlas.GetTextureIndex(backGuid)]);
					}
					else
					{
						anim.HideBone("Headgear_Back", true);
						Log(i + " back hidden (frame " + (backFrame != null) + ")");
					}
				}
				anim.Play("Idle");
			}
		}
		else if (s_Frame == 30)
		{
			for (var i = 0; i < Headgears.Length; i++)
			{
				var rig = s_Rigs[i];
				foreach (var t in rig.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Headgear") || t.name == "Body" || t.name == "Body_Sprite" || t.name == "Root"))
				{
					Log(i + " " + t.name + " world " + (t.position - rig.transform.position).ToString("F2") + " rot " + t.eulerAngles.z.ToString("F2") + " lossy " + t.lossyScale.ToString("F3") + " local " + t.localPosition.ToString("F2"));
				}
				Render(rig, "Temp/PirateHeadgearProbe_" + i + ".png");
			}
			Finish();
		}
	}

	private static void Render(GameObject rig, string path)
	{
		var camObject = new GameObject("ProbeCamera");
		var cam = camObject.AddComponent<Camera>();
		cam.orthographic = true;
		cam.orthographicSize = 256f;
		cam.transform.position = rig.transform.position + new Vector3(0f, 150f, -3000f);
		cam.farClipPlane = 10000f;
		cam.clearFlags = CameraClearFlags.SolidColor;
		cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
		var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
		cam.targetTexture = target;
		cam.Render();
		RenderTexture.active = target;
		var image = new Texture2D(512, 512, TextureFormat.RGBA32, false);
		image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
		RenderTexture.active = null;
		File.WriteAllBytes(path, image.EncodeToPNG());
		cam.targetTexture = null;
		UnityEngine.Object.DestroyImmediate(camObject);
		Log("rendered " + path);
	}
}
