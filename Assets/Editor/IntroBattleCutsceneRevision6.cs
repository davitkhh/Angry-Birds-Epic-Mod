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

// Sixth pass on the intro battle (battle_000), after IntroBattleCutsceneRevision5.
// - Both cutscene cameras zoom in centred (no offset): the old +100 x pan went past the background's edge.
// - BS_00_PigsRaidCamp_AfterLoss: Red fights MinionPigB again (a plain rogue; MinionPigC carries a stick), and MinionPigC
//   runs its own route again. B's container is anchored to Red so B stops RogueGapX in front of him (melee spacing:
//   Red's OffsetFromEnemyX 100 + the minion's 80).
// - The raid's corporal (MinionPigE) and stick pig (MinionPigC) wore their gear as quads pinned to the bones, which drew
//   in an unstable order against the rig. SceneRigGear swaps the bones to those textures instead and hides the quads.
// One-shot: refuses once MinionPigE has SceneRigGear. Menu item, or Temp/IntroBattleCutsceneRevision6.request.
public static class IntroBattleCutsceneRevision6
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroBattleCutsceneRevision6.request";

	private const string LogFile = "Temp/IntroBattleCutsceneRevision6.log";

	private const string SequencesPath = "Battleground_BirdCamp_01/2_StorySequences";

	private const string MidgroundPath = "Battleground_BirdCamp_01/1_Scenery/2_Landscape/3_Midground/";

	private const float RogueGapX = 180f;

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

	[MenuItem("Tools/Build/Battle/Revise Intro Battle Cutscenes (6)")]
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
		Debug.Log("[IntroBattleCutsceneRevision6]\n" + s_Log);
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
		var corporalRig = Find(scene, MidgroundPath + "MinionPigA/Animation2/MinionPigE");
		var stickRig = Find(scene, MidgroundPath + "MinionPigC/Animation/MinionPigC");
		if (corporalRig.GetComponent<SceneRigGear>() != null)
		{
			Log("already revised, unchanged");
			return false;
		}
		var b = new[] { Find(scene, MidgroundPath + "MinionPigB"), Find(scene, MidgroundPath + "MinionPigB/Animation"), Find(scene, MidgroundPath + "MinionPigB/Animation/MinionPigB") }.Select(t => t.gameObject).ToArray();
		var c = new[] { Find(scene, MidgroundPath + "MinionPigC"), Find(scene, MidgroundPath + "MinionPigC/Animation"), stickRig }.Select(t => t.gameObject).ToArray();
		var introNodes = Ordered(intro);
		var lostNodes = Ordered(lost);
		var anchor = lostNodes.FirstOrDefault(n => n.type == NodeType.SetPosition && n.customInt == 3 && n.text == "RedBird" && n.refObject == c[0]);
		var rogueWait = Enumerable.Range(0, lostNodes.Count - 1).Where(i => lostNodes[i].type == NodeType.Delay && Mathf.Approximately(lostNodes[i].customFloat, 1.5f) && lostNodes[i + 1].text == "Idle").DefaultIfEmpty(-1).First();
		if (anchor == null || rogueWait < 0)
		{
			Log("trees are not the expected ones, unchanged");
			return false;
		}

		var centred = CentreCamera(introNodes) + CentreCamera(lostNodes);
		Log("camera pans removed from " + centred + " MoveTo nodes (zoom only)");

		var swapped = 0;
		foreach (var node in lostNodes)
		{
			var before = node.refObject;
			node.refObject = SwapBC(node.refObject, b, c);
			node.refObject2 = SwapBC(node.refObject2, b, c);
			if (node.text == "MinionPigB_Move" || node.text == "MinionPigC_Move")
			{
				node.text = node.text == "MinionPigB_Move" ? "MinionPigC_Move" : "MinionPigB_Move";
			}
			if (before != node.refObject)
			{
				swapped++;
			}
		}
		anchor.customVec2 = b[0].transform.rotation * (new Vector3(RogueGapX, 0f, 0f) - RogueRunEnd);
		// MinionPigB's run is 2 s (MinionPigC's was 2.5 s).
		lostNodes[rogueWait].customFloat = 1f;
		Finish(lost, lostNodes);
		Log("BS_00_PigsRaidCamp_AfterLoss: Red fights MinionPigB again (" + swapped + " node refs swapped back), anchored " + RogueGapX + " in front of Red");
		Finish(intro, introNodes);

		AddGear(corporalRig, new[] { Gear("Headgear", "Headgear_Corporal", corporalRig.Find("Root/Headgear/Mesh")), Gear("Headgear_Back", "Headgear_Corporal_Back", corporalRig.Find("Root/Headgear/Headgear_Back/Mesh")) });
		AddGear(stickRig, new[] { Gear("MainHand", "MainHand_Stickpig", stickRig.Find("Root/MainHand/Mesh")) });
		return true;
	}

	private static int CentreCamera(List<ActionNode> nodes)
	{
		var count = 0;
		foreach (var node in nodes.Where(n => n.type == NodeType.MoveTo && n.refObject != null && n.refObject.name == "1_Camera"))
		{
			node.customVec1 = Vector3.zero;
			node.customVec2 = Vector3.zero;
			count++;
		}
		return count;
	}

	private static UnityEngine.Object SwapBC(UnityEngine.Object value, GameObject[] b, GameObject[] c)
	{
		for (var i = 0; i < b.Length; i++)
		{
			if (value == b[i])
			{
				return c[i];
			}
			if (value == c[i])
			{
				return b[i];
			}
		}
		return value;
	}

	private static SceneRigGear.Swap Gear(string bone, string texture, Transform quad)
	{
		if (quad == null)
		{
			Log("warning: no quad for " + bone);
		}
		return new SceneRigGear.Swap { BoneName = bone, TextureName = texture, Replaces = quad != null ? quad.gameObject : null };
	}

	private static void AddGear(Transform rig, SceneRigGear.Swap[] swaps)
	{
		var gear = rig.gameObject.AddComponent<SceneRigGear>();
		gear.m_Swaps = swaps.ToList();
		EditorUtility.SetDirty(gear);
		Log(rig.name + ": SceneRigGear " + string.Join(", ", swaps.Select(s => s.BoneName + " -> " + s.TextureName).ToArray()));
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
