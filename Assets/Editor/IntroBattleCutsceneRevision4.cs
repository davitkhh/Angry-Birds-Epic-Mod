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

// Fourth pass on the intro battle (battle_000), after IntroBattleCutsceneRevision3.
// - Wiz Pig's shadow: FlyingState (a looping layer 2 state) shrinks his Shadow bone, and nothing ever ended it. He now
//   lands with HoveringState (same layer, shadow at full size) before his cheer.
// - BS_00_PigsRaidCamp_AfterLoss: Wiz Pig leaves like in the original cutscene instead of the MoveArc swoop: he turns
//   (CharacterContainer_BS_00_WizTurn) after Porky's order, and after Red's first hit on the rogue he takes off
//   (FlyingState, Move_Fly) along CharacterContainer_BS_02_WizLeaves, from where he is to the clip's own end.
// One-shot: refuses once the lost tree has no MoveArc node. Menu item, or Temp/IntroBattleCutsceneRevision4.request.
public static class IntroBattleCutsceneRevision4
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroBattleCutsceneRevision4.request";

	private const string LogFile = "Temp/IntroBattleCutsceneRevision4.log";

	private const string SequencesPath = "Battleground_BirdCamp_01/2_StorySequences";

	private const string ForegroundPath = "Battleground_BirdCamp_01/1_Scenery/2_Landscape/4_Foreground";

	private const string WizTurnClip = "Assets/AnimationClip/CharacterContainer_BS_00_WizTurn.anim";

	private const string WizLeavesClip = "Assets/AnimationClip/CharacterContainer_BS_02_WizLeaves.anim";

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

	[MenuItem("Tools/Build/Battle/Revise Intro Battle Cutscenes (4)")]
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
		Debug.Log("[IntroBattleCutsceneRevision4]\n" + s_Log);
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
		if (!lostNodes.Any(n => n.type == NodeType.MoveArc))
		{
			Log("already revised, unchanged");
			return false;
		}
		var cheer = introNodes.FindIndex(n => n.objectName == "Wiz" && n.text == "Cheer");
		var swoop = lostNodes.FindIndex(n => n.type == NodeType.MoveArc);
		var hide = lostNodes.FindIndex(n => n.type == NodeType.SetActive && n.objectName == "Wiz");
		var firstHit = lostNodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.text == "Hit" && n.objectType == 0);
		var beat = lostNodes.FindIndex(n => n.type == NodeType.Delay && Mathf.Approximately(n.customFloat, 0.6f));
		var wizTurn = AssetDatabase.LoadAssetAtPath<AnimationClip>(WizTurnClip);
		var wizLeaves = AssetDatabase.LoadAssetAtPath<AnimationClip>(WizLeavesClip);
		if (cheer < 0 || swoop < 0 || hide < 0 || firstHit < 0 || beat < 0 || wizTurn == null || wizLeaves == null)
		{
			Log("trees or clips are not the expected ones, unchanged");
			return false;
		}
		var foreground = Find(scene, ForegroundPath);

		introNodes.Insert(cheer, Bone("Wiz", "HoveringState", null));
		Finish(intro, introNodes);
		Log("BS_00_PigsRaidCamp: Wiz lands with HoveringState (his shadow comes back) before his cheer");

		var hideNode = lostNodes[hide];
		var hitNode = lostNodes[firstHit];
		var beatNode = lostNodes[beat];
		lostNodes[swoop] = ClipMove("Wiz", wizTurn, string.Empty, foreground, false);
		lostNodes.Remove(hideNode);
		lostNodes.InsertRange(lostNodes.IndexOf(hitNode) + 1, new[]
		{
			Bone("Wiz", "FlyingState", null),
			Bone("Wiz", "Move_Fly", null),
			Exit(ClipMove("Wiz", wizLeaves, string.Empty, foreground, false))
		});
		// Gone once the 2 s takeoff is over.
		lostNodes.Insert(lostNodes.IndexOf(beatNode) + 1, hideNode);
		Finish(lost, lostNodes);
		Log("BS_00_PigsRaidCamp_AfterLoss: " + lostNodes.Count + " nodes; Wiz turns after Porky's order and takes off after Red's first hit, hidden " + "after the takeoff");
		return true;
	}

	private static ActionNode Exit(ActionNode clipMove)
	{
		clipMove.customInt2 = 1;
		return clipMove;
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
