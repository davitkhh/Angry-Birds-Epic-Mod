using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Fifteenth revision of the McCool robbery cutscene: the rogue's lunge keeps only the airborne part of Action_Attack_2.
// The clip winds up on the ground for frames 0-7 (body crouched back), takes off at frame 8, hops (Body +80) at 10 and
// lands with the Impact at 12, then recovers to 18 (24 fps). The PlayBoneAnimation node now starts at frame 8 (its
// "Start at" field), the wind-up delay is removed, the MovePath flies exactly frames 8-12 and the wait after the bag
// grab covers frames 12-18. Run revision 13's re-arc afterwards for the lower arc. Menu item, or
// Temp/McCoolCutsceneRevision15.request.
public static class McCoolCutsceneRevision15
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision15.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision15.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const string AttackClip = "Action_Attack_2";

	private const float Fps = 24f;

	private const float TakeOffFrame = 8f;

	private const float LandingFrame = 12f;

	private const float LastFrame = 18f;

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
		Revise();
	}

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (15)")]
	public static void Revise()
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
			if (ReviseInternal(scene))
			{
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
		}
		catch (Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[McCoolCutsceneRevision15]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static bool ReviseInternal(Scene scene)
	{
		var sequence = Find(scene, SequencePath);
		var tree = sequence.GetComponent<ActionTree>();
		var order = ChainOrder(tree);
		var nodes = tree.nodes.ToList();
		var attack = order.FirstOrDefault(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "Rogue" && n.text == AttackClip);
		var lunge = order.FirstOrDefault(n => n.type == NodeType.MovePath && n.objectName == "Rogue");
		var grab = order.FirstOrDefault(n => n.type == NodeType.SetHandItem && n.objectName == "Rogue" && n.customBool);
		if (attack == null || lunge == null || grab == null)
		{
			Log("rogue's attack clip, lunge or bag grab not found (run revisions 12 and 13 first); unchanged");
			return false;
		}
		var recover = order.SkipWhile(n => n != grab).Skip(1).FirstOrDefault();
		attack.customFloat2 = TakeOffFrame / Fps;
		// Drop any delay between the clip and the lunge (revision 12's wind-up).
		var between = order.SkipWhile(n => n != attack).Skip(1).TakeWhile(n => n != lunge).ToList();
		foreach (var node in between.Where(n => n.type == NodeType.Delay))
		{
			Remove(nodes, node);
			Log("removed wind-up delay node " + node.nodeID);
		}
		lunge.customFloat = 0f;
		lunge.customFloat2 = (LandingFrame - TakeOffFrame) / Fps;
		if (recover != null && recover.type == NodeType.Delay)
		{
			recover.customFloat = (LastFrame - LandingFrame) / Fps;
		}
		Log("node " + attack.nodeID + " starts " + AttackClip + " at " + attack.customFloat2.ToString("0.###") + " s (frame " + TakeOffFrame + "); lunge node " + lunge.nodeID + " flies " + lunge.customFloat2.ToString("0.###") + " s; recovery node " + (recover != null ? recover.nodeID + " " + recover.customFloat.ToString("0.###") + " s" : "none"));
		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
		return true;
	}

	private static void Remove(List<ActionNode> nodes, ActionNode node)
	{
		var outs = node.nodesOut ?? new int[0];
		var ins = node.nodesIn ?? new int[0];
		foreach (var from in nodes.Where(n => n.nodesOut != null && n.nodesOut.Contains(node.nodeID)))
		{
			from.nodesOut = from.nodesOut.SelectMany(x => x == node.nodeID ? outs : new[] { x }).ToArray();
			from.nodesOutIndex = from.nodesOut.Select(x => 0).ToArray();
		}
		foreach (var to in nodes.Where(n => n.nodesIn != null && n.nodesIn.Contains(node.nodeID)))
		{
			to.nodesIn = to.nodesIn.SelectMany(x => x == node.nodeID ? ins : new[] { x }).ToArray();
		}
		nodes.Remove(node);
	}

	private static List<ActionNode> ChainOrder(ActionTree tree)
	{
		var result = new List<ActionNode>();
		var seen = new HashSet<int>();
		var node = tree.GetNodeByID(tree.startNode);
		while (node != null && seen.Add(node.nodeID))
		{
			result.Add(node);
			node = node.nodesOut != null && node.nodesOut.Length > 0 ? tree.GetNodeByID(node.nodesOut[0]) : null;
		}
		return result;
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
