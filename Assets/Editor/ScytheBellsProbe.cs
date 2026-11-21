using System;
using System.IO;
using System.Linq;
using SmoothMoves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Probe for ScytheBells: plays a MinionPig holding the belled Dark Cupid scythe (one facing right, one mirrored) in play
// mode at a fixed 30 fps: Idle, then Action_Attack_1, then Hit. Renders frames to Logs/ScytheBellsProbe/ and logs the
// bells' state. Trigger: touch Temp/ScytheBellsProbe.request (open editor) or
// Unity -batchmode -projectPath . -executeMethod ScytheBellsProbe.Run (editor closed; it quits when done).
[InitializeOnLoad]
public static class ScytheBellsProbe
{
	private const string Dir = "Logs/ScytheBellsProbe";

	private const string LogFile = Dir + "/log.txt";

	private const string StateKey = "ScytheBellsProbe.State";

	private const string ScenesKey = "ScytheBellsProbe.Scenes";

	private const string QuitKey = "ScytheBellsProbe.Quit";

	private const string RequestFile = "Temp/ScytheBellsProbe.request";

	private const string Item = "pig_weapon_minion_cupid_dark_bells_01";

	private static int s_Start = -1;

	private static GameObject[] s_Rigs;

	private static ScytheBells[] s_Bells;

	static ScytheBellsProbe()
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

	private static int s_LastFrame = -1;

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
			var prefab = UnityEngine.Resources.Load<GameObject>("Prefabs/4_Characters/8_Base/MinionPig");
			s_Rigs = new GameObject[2];
			s_Bells = new ScytheBells[2];
			for (var i = 0; i < 2; i++)
			{
				// strip the rig's other scripts before they wake (their trigger delegates outlive them otherwise)
				var holder = new GameObject("ProbeHolder");
				holder.SetActive(false);
				var rig = UnityEngine.Object.Instantiate(prefab, holder.transform);
				foreach (var behaviour in rig.GetComponents<MonoBehaviour>().Where(m => !(m is BoneAnimation)).ToList())
				{
					UnityEngine.Object.DestroyImmediate(behaviour);
				}
				rig.transform.SetParent(null, false);
				UnityEngine.Object.Destroy(holder);
				rig.transform.position = new Vector3(i * 2000f, 0f, 0f);
				if (i == 1)
				{
					var s = rig.transform.localScale;
					rig.transform.localScale = new Vector3(-s.x, s.y, s.z);
				}
				s_Rigs[i] = rig;
			}
		}
		else if (f == 4)
		{
			for (var i = 0; i < 2; i++)
			{
				var anim = s_Rigs[i].GetComponent<BoneAnimation>();
				var bone = anim.mBoneSource.First(b => b.boneName == "MainHand");
				var frame = anim.triggerFrames.First(t => t.GetTriggerFrameBone(bone.boneNodeIndex) != null && !string.IsNullOrEmpty(t.GetTriggerFrameBone(bone.boneNodeIndex).originalTextureGUID));
				var guid = frame.GetTriggerFrameBone(bone.boneNodeIndex).originalTextureGUID;
				var atlas = anim.textureAtlases.First(a => a.GetTextureIndex(guid) != -1);
				var target = anim.textureAtlases.First(a => a.textureNames.Contains("MainHand_DarkCupid"));
				anim.SwapBoneTexture("MainHand", atlas.name, atlas.textureNames[atlas.GetTextureIndex(guid)], target.name, "MainHand_DarkCupid");
				anim.Play("Idle");
				s_Bells[i] = ScytheBells.Attach(s_Rigs[i], anim, s_Rigs[i].transform, Item, "MainHand");
				Log(i + " bells " + (s_Bells[i] != null));
				if (i == 1)
				{
					// the mirrored pig's bells: silver (big) and bronze (small)
					const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
					var weapon = (ScytheBells.WeaponEntry)typeof(ScytheBells).GetField("m_Weapon", any).GetValue(s_Bells[1]);
					weapon.materials = new[] { "Bronze", "Bronze" };
					typeof(ScytheBells).GetMethod("SetMaterials", any).Invoke(s_Bells[1], null);
				}
				if (i == 0)
				{
					// each bell's strike, cut out of bell_ring.wav as the game does
					var table = typeof(ScytheBells).GetMethod("Config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
					var bellsField = table.GetType().GetField("bells");
					var cut = typeof(ScytheBells).GetMethod("StrikeClip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
					var metals = (ScytheBells.MaterialEntry[])table.GetType().GetField("materials").GetValue(table);
					foreach (ScytheBells.BellEntry entry in (ScytheBells.BellEntry[])bellsField.GetValue(table))
					foreach (var metal in metals)
					{
						var clip = (AudioClip)cut.Invoke(null, new object[] { entry, metal });
						var data = clip != null ? new float[clip.samples * clip.channels] : new float[0];
						if (clip != null)
						{
							clip.GetData(data, 0);
						}
						var peak = data.Length > 0 ? data.Max(Mathf.Abs) : 0f;
						var tail = data.Length > 0 ? data.Skip(data.Length - 200).Max(Mathf.Abs) : 0f;
						Log("strike " + entry.sprite + " " + metal.name + ": " + (clip != null ? clip.length.ToString("F3") + " s, " + clip.frequency + " Hz, peak " + peak.ToString("F3") + ", tail " + tail.ToString("F4") + ", pitch " + entry.pitch : "none"));
					}
				}
			}
		}
		else if (f == 70)
		{
			foreach (var rig in s_Rigs)
			{
				var anim = rig.GetComponent<BoneAnimation>();
				anim.Play("Action_Attack_1");
				anim.PlayQueued("Idle");
			}
		}
		else if (f == 150)
		{
			foreach (var rig in s_Rigs)
			{
				var anim = rig.GetComponent<BoneAnimation>();
				anim.Play("Hit");
				anim.PlayQueued("Idle");
			}
		}
		if (f >= 6)
		{
			Log(f + " t " + Time.time.ToString("F3") + " A: " + s_Bells[0].Describe() + " || B: " + s_Bells[1].Describe());
			var zs = s_Rigs[0].GetComponentsInChildren<Transform>().Where(x => x.name == "MainHand_Sprite" || x.name == "Body_Sprite" || x.name == "Root" || x.name == "Head_Sprite").Select(x => x.name + " z " + x.position.z.ToString("F2") + " lz " + x.localPosition.z.ToString("F2"));
			Log("   depth " + string.Join(", ", zs.ToArray()));
			var hand = s_Rigs[0].GetComponentsInChildren<Transform>().First(x => x.name == "MainHand_Sprite");
			Log("   at capture: frame " + Time.frameCount + " ring " + hand.TransformPoint(new Vector3(62.379f, -42.375f, 0f)).ToString("F1"));
		}
		if (f == 20 || f == 66 || (f >= 70 && f <= 200 && f % 2 == 0))
		{
			Render(s_Rigs[0], Dir + "/a_" + f.ToString("000") + ".png");
			if (f % 6 == 0 || f == 66)
			{
				Render(s_Rigs[1], Dir + "/b_" + f.ToString("000") + ".png");
			}
		}
		if (f == 202)
		{
			Finish();
		}
	}

	private static void Render(GameObject rig, string path)
	{
		var camObject = new GameObject("ProbeCamera");
		var cam = camObject.AddComponent<Camera>();
		cam.orthographic = true;
		cam.orthographicSize = 200f;
		cam.transform.position = rig.transform.position + new Vector3(0f, 110f, -3000f);
		cam.farClipPlane = 10000f;
		cam.clearFlags = CameraClearFlags.SolidColor;
		cam.backgroundColor = new Color(0.86f, 0.84f, 0.8f, 1f);
		var target = new RenderTexture(400, 400, 24, RenderTextureFormat.ARGB32);
		cam.targetTexture = target;
		cam.Render();
		RenderTexture.active = target;
		var image = new Texture2D(400, 400, TextureFormat.RGBA32, false);
		image.ReadPixels(new Rect(0, 0, 400, 400), 0, 0);
		RenderTexture.active = null;
		File.WriteAllBytes(path, image.EncodeToPNG());
		cam.targetTexture = null;
		UnityEngine.Object.DestroyImmediate(camObject);
		target.Release();
	}
}
