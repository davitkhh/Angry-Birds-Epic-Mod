using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Twenty-first pass on the intro battle's raid: the two pigs carrying Chuck (the bird catcher MinionPigC and the guard
// MinionPigG) carry him without off-hands (SceneRigGear hides the OffHand bone). Scene gear only; the raid's tree is
// unchanged. One-shot: refuses once both have their off-hand hidden. Menu item, or
// Temp/IntroBattleCutsceneRevision21.request.
public static class IntroBattleCutsceneRevision21
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroBattleCutsceneRevision21.request";

	private const string LogFile = "Temp/IntroBattleCutsceneRevision21.log";

	private const string MidgroundPath = "1_Scenery/2_Landscape/3_Midground/";

	private static readonly string[] CarrierRigs = { "MinionPigC/Animation/MinionPigC", "MinionPigG/Animation/MinionPigG" };

	private static readonly StringBuilder s_Log = new StringBuilder();

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
		File.Delete(RequestFile);
		Revise();
	}

	[MenuItem("Tools/Build/Battle/Revise Intro Battle Cutscenes (21)")]
	public static void Revise()
	{
		s_Log.Length = 0;
		try
		{
			var scene = SceneManager.GetSceneByPath(ScenePath);
			var opened = false;
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
				opened = true;
			}
			var hadUnsavedChanges = scene.isDirty;
			var root = scene.GetRootGameObjects().First(g => g.name == "Battleground_BirdCamp_01").transform;
			var changed = 0;
			foreach (var path in CarrierRigs)
			{
				var gear = root.Find(MidgroundPath + path).GetComponent<SceneRigGear>();
				var offHand = gear.m_Swaps.FirstOrDefault(s => s.BoneName == "OffHand");
				if (offHand != null && offHand.TextureName == string.Empty)
				{
					continue;
				}
				if (offHand == null)
				{
					gear.m_Swaps.Add(new SceneRigGear.Swap { BoneName = "OffHand", TextureName = string.Empty });
				}
				else
				{
					offHand.TextureName = string.Empty;
				}
				EditorUtility.SetDirty(gear);
				changed++;
				s_Log.AppendLine(path + ": off-hand hidden");
			}
			if (changed == 0)
			{
				s_Log.AppendLine("already revised, unchanged");
			}
			else
			{
				EditorSceneManager.MarkSceneDirty(scene);
				if (hadUnsavedChanges)
				{
					s_Log.AppendLine("not saved: the scene already had unsaved changes, save it in the editor");
				}
				else
				{
					EditorSceneManager.SaveScene(scene);
					s_Log.AppendLine("saved " + ScenePath);
				}
			}
			if (opened && !scene.isDirty)
			{
				EditorSceneManager.CloseScene(scene, true);
			}
		}
		catch (Exception e)
		{
			s_Log.AppendLine("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[IntroBattleCutsceneRevision21]\n" + s_Log);
	}
}
