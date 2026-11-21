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

// Fifth pass on the intro battle (battle_000), after IntroBattleCutsceneRevision4.
// - BS_00_PigsRaidCamp: Wiz Pig is posed at his entrance's start (top right) a second before he flies in, so he is
//   hidden until his entrance starts (FlyingState again on showing; deactivating may drop the state).
// - BS_00_PigsRaidCamp_AfterLoss: no turn. Wiz Pig takes off where he used to turn (after Porky's order) instead of
//   after Red's first hit.
// One-shot: refuses once the lost tree has no WizTurn node. Menu item, or Temp/IntroBattleCutsceneRevision5.request.
public static class IntroBattleCutsceneRevision5
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroBattleCutsceneRevision5.request";

	private const string LogFile = "Temp/IntroBattleCutsceneRevision5.log";

	private const string SequencesPath = "Battleground_BirdCamp_01/2_StorySequences";

	private const string ForegroundPath = "Battleground_BirdCamp_01/1_Scenery/2_Landscape/4_Foreground";

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

	[MenuItem("Tools/Build/Battle/Revise Intro Battle Cutscenes (5)")]
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
			if (opened && !scene.isDirty)
			{
				EditorSceneManager.CloseScene(scene, true);
			}
		}
		catch (Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[IntroBattleCutsceneRevision5]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static bool ReviseInternal(Scene scene)
	{
		var sequences = Find(scene, SequencesPath);
		var intro = sequences.Find("BS_00_PigsRaidCamp").GetComponent<ActionTree>();
		var lost = sequences.Find("BS_00_PigsRaidCamp_AfterLoss").GetComponent<ActionTree>();
		var introNodes = Ordered(intro);
		var lostNodes = Ordered(lost);
		var turn = lostNodes.FindIndex(n => n.type == NodeType.MoveByClip && n.refObject != null && n.refObject.name == "CharacterContainer_BS_00_WizTurn");
		if (turn < 0)
		{
			Log("already revised, unchanged");
			return false;
		}
		var leave = lostNodes.FindIndex(n => n.type == NodeType.MoveByClip && n.customInt2 == 1 && n.objectName == "Wiz");
		var pose = introNodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "Wiz" && n.text == "FlyingState");
		var entrance = introNodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "Wiz" && n.text == "Move_Fly");
		if (leave < 2 || pose < 0 || entrance < 0 || lostNodes[leave - 2].text != "FlyingState" || lostNodes[leave - 1].text != "Move_Fly")
		{
			Log("trees are not the expected ones, unchanged");
			return false;
		}

		introNodes.Insert(entrance, Bone("Wiz", "FlyingState", null));
		introNodes.Insert(entrance, Active("Wiz", true));
		introNodes.Insert(pose + 1, Active("Wiz", false));
		Finish(intro, introNodes);
		Log("BS_00_PigsRaidCamp: Wiz hidden from his start pose until his entrance");

		var takeoff = lostNodes.GetRange(leave - 2, 3);
		lostNodes.RemoveRange(leave - 2, 3);
		lostNodes.RemoveAt(turn);
		lostNodes.InsertRange(turn, takeoff);
		Finish(lost, lostNodes);
		Log("BS_00_PigsRaidCamp_AfterLoss: " + lostNodes.Count + " nodes; Wiz takes off where he used to turn");
		return true;
	}

	private static ActionNode Active(string runtimeName, bool active)
	{
		var node = NewNode(NodeType.SetActive);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.customBool = active;
		return node;
	}

	private static List<ActionNode> Ordered(ActionTree tree)
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

	private static ActionNode ClipMove(string runtimeName, AnimationClip clip, string path, Transform space, bool startPoseOnly)
	{
		var node = NewNode(NodeType.MoveByClip);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.refObject = clip;
		node.refObject1 = space;
		node.text = path;
		node.customBool = startPoseOnly;
		return node;
	}

	private static ActionNode Bone(string runtimeName, string clip, string queued)
	{
		var node = NewNode(NodeType.PlayBoneAnimation);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.text = clip;
		node.QueueIdle = queued != null;
		node.secondaryText = queued ?? string.Empty;
		return node;
	}

	private static ActionNode Delay(float seconds)
	{
		var node = NewNode(NodeType.Delay);
		node.customFloat = seconds;
		return node;
	}

	private static ActionNode NewNode(NodeType type)
	{
		return new ActionNode
		{
			type = type,
			enabled = true,
			text = string.Empty,
			objectName = string.Empty,
			secondaryText = string.Empty,
			param = new string[0],
			nodesIn = new int[0],
			nodesOut = new int[0],
			nodesOutIndex = new int[0]
		};
	}

	// Numbers the nodes 1..n in order, lays them out in rows of ten and links them into one chain.
	private static void Finish(ActionTree tree, List<ActionNode> chain)
	{
		for (var i = 0; i < chain.Count; i++)
		{
			chain[i].nodeID = i + 1;
			chain[i].editorRect = new Rect(20f + 230f * (i % 10), 20f + 220f * (i / 10), 200f, 180f);
		}
		for (var i = 0; i < chain.Count; i++)
		{
			chain[i].nodesIn = i > 0 ? new[] { chain[i - 1].nodeID } : new int[0];
			chain[i].nodesOut = i + 1 < chain.Count ? new[] { chain[i + 1].nodeID } : new int[0];
			chain[i].nodesOutIndex = i + 1 < chain.Count ? new[] { 0 } : new int[0];
		}
		tree.nodes = chain.ToArray();
		tree.startNode = 1;
		EditorUtility.SetDirty(tree);
	}

	private static Transform Find(Scene scene, string path)
	{
		var parts = path.Split('/');
		foreach (var root in scene.GetRootGameObjects())
		{
			if (root.name == parts[0])
			{
				var found = parts.Length == 1 ? root.transform : root.transform.Find(string.Join("/", parts.Skip(1).ToArray()));
				if (found != null)
				{
					return found;
				}
			}
		}
		throw new Exception("not found in " + scene.name + ": " + path);
	}
}
