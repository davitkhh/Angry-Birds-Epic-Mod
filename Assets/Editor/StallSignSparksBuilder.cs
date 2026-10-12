using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The merchant stall's sign and "Shop" label shoot out the generic hotspot activation sparks (FX_SetActive, as used by
// castles, gates and huts) when they pop in: each gets an InstantiateAndTriggerParticleSystemByAnimation fed by the
// scene's AssetProvider_FX_Battleground, and its pop-in clip fires PlayAndInstantiateParticleSystem("FX_SetActive")
// at its first frame. Menu item, or touch Temp/StallSparks.request with the editor open.
public static class StallSignSparksBuilder
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/StallSparks.request";

	private const string LogFile = "Temp/StallSparks.log";

	private const string StallPath = "WorldMap/2_Scenery/Zone_02/3_Hotspots/Hotspot_008_02_Merchant/MerchantStall";

	private const string ProviderPath = "WorldMap/3_AssetProviders/AssetProvider_FX_Battleground";

	private const string Sparks = "FX_SetActive";

	private const string EventFunction = "PlayAndInstantiateParticleSystem";

	private static readonly StringBuilder s_Log = new StringBuilder();

	[InitializeOnLoadMethod]
	private static void WatchForRequests()
	{
		EditorApplication.update -= PollRequest;
		EditorApplication.update += PollRequest;
	}

	private static void PollRequest()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		File.Delete(RequestFile);
		Build();
	}

	[MenuItem("Tools/World Map/Add Stall Sign Sparks")]
	public static void Build()
	{
		s_Log.Length = 0;
		try
		{
			var scene = SceneManager.GetSceneByPath(ScenePath);
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
			}
			var hadUnsavedChanges = scene.isDirty;
			var provider = Find(scene, ProviderPath).GetComponent<GenericAssetProvider>();
			var stall = Find(scene, StallPath);
			foreach (var part in new[] { "Sign", "ShopSign" })
			{
				AddSparks(stall.Find(part), provider);
			}
			EditorSceneManager.MarkSceneDirty(scene);
			if (hadUnsavedChanges)
			{
				Log("not saved: the scene already had unsaved changes, save it in the editor");
			}
			else
			{
				EditorSceneManager.SaveScene(scene);
				Log("saved " + ScenePath);
			}
		}
		catch (Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[StallSignSparksBuilder]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static void AddSparks(Transform part, GenericAssetProvider provider)
	{
		var spawner = part.GetComponent<InstantiateAndTriggerParticleSystemByAnimation>();
		if (spawner == null)
		{
			spawner = part.gameObject.AddComponent<InstantiateAndTriggerParticleSystemByAnimation>();
		}
		spawner.m_ParticleSystemsAssetProvider = provider;
		EditorUtility.SetDirty(spawner);

		var clip = part.GetComponent<Animation>().clip;
		var events = AnimationUtility.GetAnimationEvents(clip).ToList();
		if (events.Any(e => e.functionName == EventFunction && e.stringParameter == Sparks))
		{
			Log(part.name + ": " + clip.name + " already fires " + Sparks);
		}
		else
		{
			events.Insert(0, new AnimationEvent { time = 0f, functionName = EventFunction, stringParameter = Sparks });
			AnimationUtility.SetAnimationEvents(clip, events.ToArray());
			EditorUtility.SetDirty(clip);
			AssetDatabase.SaveAssets();
			Log(part.name + ": " + clip.name + " fires " + Sparks + " at 0 s");
		}
	}

	private static Transform Find(Scene scene, string path)
	{
		var parts = path.Split('/');
		foreach (var root in scene.GetRootGameObjects())
		{
			if (root.name == parts[0])
			{
				var found = root.transform.Find(path.Substring(parts[0].Length + 1));
				if (found != null)
				{
					return found;
				}
			}
		}
		throw new Exception("not found in scene: " + path);
	}
}
