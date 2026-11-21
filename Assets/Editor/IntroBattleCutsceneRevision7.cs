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

// Seventh pass on the intro battle's lost tree (BS_00_PigsRaidCamp_AfterLoss), after IntroBattleCutsceneRevision6.
// - Wiz Pig takes off a moment before Porky turns to order the raid.
// - Red is surprised while he looks back (turned left), not after turning back.
// - MinionPigC stops its walk cycle (Idle) when its run ends; it used to die to Chuck's lightning there.
// - Red's rogue (MinionPigB) runs to RogueLunge short of its fighting spot, then lunges the rest of the way with its
//   attack (MoveArc without a dip) as its run ends.
// One-shot: refuses once the tree has a lunge (a MoveArc on MinionPigB). Menu item, or
// Temp/IntroBattleCutsceneRevision7.request.
public static class IntroBattleCutsceneRevision7
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroBattleCutsceneRevision7.request";

	private const string LogFile = "Temp/IntroBattleCutsceneRevision7.log";

	private const string SequencesPath = "Battleground_BirdCamp_01/2_StorySequences";

	private const string MidgroundPath = "Battleground_BirdCamp_01/1_Scenery/2_Landscape/3_Midground/";

	private const float RogueGapX = 180f;

	private const float RogueLunge = 120f;

	private const float LungeTime = 0.3f;

	// MinionPigB_Move is 2 s; the run starts this long before the delay that ends with the lunge (Red's Focus_MainHand
	// 0.42 s + two 0.2 s delays).
	private const float RunTimeBeforeLungeDelay = 0.82f;

	// MinionPigB_Move's last key (its Animation child, container space).
	private static readonly Vector3 RogueRunEnd = new Vector3(-851.5499f, 0f, 386.5454f);

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

	[MenuItem("Tools/Build/Battle/Revise Intro Battle Cutscenes (7)")]
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
		Debug.Log("[IntroBattleCutsceneRevision7]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static bool ReviseInternal(Scene scene)
	{
		var sequences = Find(scene, SequencesPath);
		var lost = sequences.Find("BS_00_PigsRaidCamp_AfterLoss").GetComponent<ActionTree>();
		var rogue = Find(scene, MidgroundPath + "MinionPigB").gameObject;
		var rogueRig = Find(scene, MidgroundPath + "MinionPigB/Animation/MinionPigB").gameObject;
		var stickRig = Find(scene, MidgroundPath + "MinionPigC/Animation/MinionPigC").gameObject;
		var nodes = Ordered(lost);
		if (nodes.Any(n => n.type == NodeType.MoveArc && n.refObject == rogue))
		{
			Log("already revised, unchanged");
			return false;
		}
		var takeoff = nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "Wiz" && n.text == "FlyingState");
		var porkyTurn = nodes.FindIndex(n => n.type == NodeType.SetScale && n.objectName == "Porky" && n.customVec1.x < 0f);
		var redTurn = nodes.FindIndex(n => n.type == NodeType.SetScale && n.objectName == "RedBird" && n.customVec1.x < 0f);
		var redSurprised = nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "RedBird" && n.text == "Surprised");
		var anchor = nodes.FirstOrDefault(n => n.type == NodeType.SetPosition && n.customInt == 3 && n.refObject == rogue);
		var rogueIdle = nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.text == "Idle" && n.refObject == rogue);
		var stickRun = nodes.FindIndex(n => n.type == NodeType.PlayAnimation && n.text == "MinionPigC_Move");
		var beat = nodes.FindIndex(n => n.type == NodeType.Delay && Mathf.Approximately(n.customFloat, 0.6f));
		if (takeoff < 0 || porkyTurn < 0 || takeoff < porkyTurn || redTurn < 0 || redSurprised < redTurn || anchor == null || rogueIdle < 1 || nodes[rogueIdle - 1].type != NodeType.Delay || stickRun < 0 || beat < stickRun)
		{
			Log("tree is not the expected one, unchanged");
			return false;
		}

		// Wiz takes off (FlyingState, Move_Fly, the exit MoveByClip), then a beat, then Porky turns and orders.
		var wizLeaves = nodes.GetRange(takeoff, 3);
		nodes.RemoveRange(takeoff, 3);
		nodes.InsertRange(porkyTurn, wizLeaves);
		nodes.Insert(porkyTurn + 3, Delay(0.3f));

		// Red: turn left, surprised (waits for the clip), turn back.
		var surprised = nodes[nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "RedBird" && n.text == "Surprised")];
		nodes.Remove(surprised);
		surprised.customInt = 1;
		redTurn = nodes.FindIndex(n => n.type == NodeType.SetScale && n.objectName == "RedBird" && n.customVec1.x < 0f);
		var redTurnBack = nodes.FindIndex(redTurn, n => n.type == NodeType.SetScale && n.objectName == "RedBird" && n.customVec1.x > 0f);
		var lookBack = nodes[redTurn + 1];
		if (lookBack.type == NodeType.Delay && redTurnBack == redTurn + 2)
		{
			lookBack.customFloat = 0.3f;
		}
		nodes.Insert(redTurn + 2, surprised);

		// The stick pig idles once its 2.5 s run is over (the 0.6 s beat ends about then).
		beat = nodes.FindIndex(n => n.type == NodeType.Delay && Mathf.Approximately(n.customFloat, 0.6f));
		var stickIdle = NewNode(NodeType.PlayBoneAnimation);
		stickIdle.refObject = stickRig;
		stickIdle.text = "Idle";
		nodes.Insert(beat + 1, stickIdle);

		// The rogue's run stops RogueLunge short; as it ends, the rogue attacks and lunges onto its spot.
		anchor.customVec2 = rogue.transform.rotation * (new Vector3(RogueGapX + RogueLunge, 0f, 0f) - RogueRunEnd);
		rogueIdle = nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.text == "Idle" && n.refObject == rogue);
		nodes[rogueIdle - 1].customFloat = 2f - RunTimeBeforeLungeDelay;
		var attack = NewNode(NodeType.PlayBoneAnimation);
		attack.refObject = rogueRig;
		attack.text = "Action_Attack_1";
		attack.QueueIdle = true;
		attack.secondaryText = "Idle";
		var lunge = NewNode(NodeType.MoveArc);
		lunge.refObject = rogue;
		lunge.secondaryText = "RedBird";
		lunge.customFloat = LungeTime;
		lunge.customFloat2 = 0f;
		// The container ends where its rig (RogueRunEnd.x into it) stands RogueGapX in front of Red.
		lunge.customVec1 = new Vector3(RogueGapX - RogueRunEnd.x, 0f, 0f);
		lunge.customInt = 1;
		nodes[rogueIdle] = attack;
		nodes.Insert(rogueIdle + 1, lunge);

		Finish(lost, nodes);
		Log("BS_00_PigsRaidCamp_AfterLoss: " + nodes.Count + " nodes; Wiz leaves before Porky's order, Red surprised while turned, stick pig idles, rogue lunges " + RogueLunge + " onto its spot " + RogueGapX + " in front of Red");
		return true;
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
