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

// Ninth pass on the intro battle (battle_000), after IntroBattleCutsceneRevision8.
// - The raid's five minions are rebuilt from the battle MinionPig prefab (mesh built at runtime). The scene's baked rigs
//   (and rev8's clones of one) carry stale submesh runs: the corporal lost its right pupil while running, unlike the
//   battle rig (SceneRigGearProbe). E and C keep their SceneRigGear gear; tree references move to the new rigs.
// - The swarm's walk cycles start at random points (PlayBoneAnimation customBool2).
// - The bird catcher (C) sets off right after MinionPigD trips and runs a little above Chuck.
// - Red's rogue attacks like a battle rogue: stands through Action_Attack_2's wind-up, lunges from its Movement trigger
//   (frame 7) to its Impact (frame 12), and Red takes the first hit.
// - Wiz Pig takes off 0.2 s after the taunts; Porky orders the raid 0.4 s later than before.
// One-shot: refuses once MinionPigB's walk loop starts at a random point. Menu item, or
// Temp/IntroBattleCutsceneRevision9.request.
public static class IntroBattleCutsceneRevision9
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroBattleCutsceneRevision9.request";

	private const string LogFile = "Temp/IntroBattleCutsceneRevision9.log";

	private const string SequencesPath = "Battleground_BirdCamp_01/2_StorySequences";

	private const string MidgroundPath = "Battleground_BirdCamp_01/1_Scenery/2_Landscape/3_Midground/";

	private const float RogueGapX = 180f;

	private const float RogueLunge = 120f;

	private const string BattleMinionPrefab = "Assets/Resources/Prefabs/4_Characters/8_Base/MinionPig.prefab";

	// Action_Attack_2 (24 fps): Movement trigger at frame 7, Impact at frame 12.
	private const float LungeWindUp = 7f / 24f;

	private const float LungeSwing = 5f / 24f;

	// Red's rogue stands this far behind Red's row (battle plane z).
	private const float RogueDepthBehind = 15f;

	// MinionPigC_Move's last key (its Animation child, container space).
	private static readonly Vector3 StickRunEnd = new Vector3(-929.3036f, 0f, 91.38677f);

	// Where MinionPigC's run ends relative to Chuck (battle plane): a little right of him and in front.
	private static readonly Vector3 StickRunEndFromChuck = new Vector3(100f, 0f, 70f);

	// From there it runs on this far left at the swarm's pace (MinionPigA_Move's ~343 units a second).
	private const float StickRunOn = 2300f;

	private const float SwarmSpeed = 343f;

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

	[MenuItem("Tools/Build/Battle/Revise Intro Battle Cutscenes (9)")]
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
		Debug.Log("[IntroBattleCutsceneRevision9]\n" + s_Log);
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
		var paths = new[] { "MinionPigA/Animation/MinionPigA", "MinionPigA/Animation2/MinionPigE", "MinionPigB/Animation/MinionPigB", "MinionPigC/Animation/MinionPigC", "MinionPigD/Animation/MinionPigD" };
		var oldRigs = paths.Select(p => Find(scene, MidgroundPath + p).gameObject).ToArray();
		var rogue = Find(scene, MidgroundPath + "MinionPigB").gameObject;
		var stick = Find(scene, MidgroundPath + "MinionPigC").gameObject;
		var nodes = Ordered(lost);
		var introNodes = Ordered(intro);
		if (nodes.Any(n => n.type == NodeType.PlayBoneAnimation && n.refObject == oldRigs[2] && n.text == "Move_Loop" && n.customBool2))
		{
			Log("already revised, unchanged");
			return false;
		}
		var wizTaunt = nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "Wiz" && n.text == "Taunt");
		var porkyTurn = nodes.FindIndex(n => n.type == NodeType.SetScale && n.objectName == "Porky" && n.customVec1.x < 0f);
		var trip = nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.text == "Action_Fail");
		var stickRun = nodes.FindIndex(n => n.type == NodeType.PlayAnimation && n.text == "MinionPigC_Move");
		var attack = nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.refObject == oldRigs[2] && n.text == "Action_Attack_2");
		var hitOnRed = nodes.FirstOrDefault(n => n.type == NodeType.Instantiate && n.objectType == 1 && n.text == "RedBird");
		if (wizTaunt < 0 || porkyTurn < 1 || nodes[porkyTurn - 1].type != NodeType.Delay || trip < 0 || stickRun < 2 || attack < 1 || nodes[attack - 1].type != NodeType.Delay || attack + 1 >= nodes.Count || nodes[attack + 1].type != NodeType.MoveArc || hitOnRed == null)
		{
			Log("trees are not the expected ones, unchanged");
			return false;
		}

		// Wiz takes off 0.2 s after the taunts, Porky turns to order 0.4 s later than before.
		nodes.Insert(wizTaunt + 1, Delay(0.2f));
		porkyTurn = nodes.FindIndex(n => n.type == NodeType.SetScale && n.objectName == "Porky" && n.customVec1.x < 0f);
		nodes[porkyTurn - 1].customFloat += 0.4f - 0.2f;

		// Red's rogue: wind-up standing, lunge from the Movement trigger to the Impact, Red is hit.
		attack = nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.refObject == oldRigs[2] && n.text == "Action_Attack_2");
		var lunge = nodes[attack + 1];
		lunge.customFloat = LungeSwing;
		nodes.Insert(attack + 1, Delay(LungeWindUp));
		var firstHit = Clone(hitOnRed);
		firstHit.objectName = "Hit0";
		var redHit = NewNode(NodeType.PlayBoneAnimation);
		redHit.objectType = 1;
		redHit.objectName = "RedBird";
		redHit.text = "Hit";
		redHit.QueueIdle = true;
		redHit.secondaryText = "Idle";
		var lungeAt = nodes.IndexOf(lunge);
		nodes.InsertRange(lungeAt + 1, new[] { firstHit, redHit, Delay(0.5f) });

		// The bird catcher sets off right after MinionPigD trips; its run carries on (the MoveArc) once Red's rogue lands.
		stickRun = nodes.FindIndex(n => n.type == NodeType.PlayAnimation && n.text == "MinionPigC_Move");
		var stickNodes = nodes.GetRange(stickRun - 2, 3);
		var runOn = nodes.First(n => n.type == NodeType.MoveArc && n.refObject == stick);
		var stickAnchor = stickNodes.First(n => n.type == NodeType.SetPosition);
		stickAnchor.customVec2 = stick.transform.rotation * (StickRunEndFromChuck - StickRunEnd);
		nodes.RemoveRange(stickRun - 2, 3);
		nodes.Remove(runOn);
		trip = nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.text == "Action_Fail");
		nodes.InsertRange(trip + 1, stickNodes);
		nodes.Insert(nodes.IndexOf(lunge) + 1, runOn);
		runOn.customVec1 = new Vector3(StickRunEndFromChuck.x - StickRunOn - StickRunEnd.x, 0f, 0f);

		// Rebuild the minions from the battle prefab.
		var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattleMinionPrefab);
		var replaced = 0;
		foreach (var old in oldRigs)
		{
			var rig = (GameObject)UnityEngine.Object.Instantiate(prefab, old.transform.parent);
			rig.name = old.name;
			rig.transform.localPosition = old.transform.localPosition;
			rig.transform.localRotation = old.transform.localRotation;
			rig.transform.localScale = old.transform.localScale;
			rig.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
			UnityHelper.SetLayerRecusively(rig, old.layer);
			var oldGear = old.GetComponent<SceneRigGear>();
			if (oldGear != null)
			{
				AddGear(rig.transform, oldGear.m_Swaps.Select(g => Gear(g.BoneName, g.TextureName)).ToArray());
			}
			foreach (var node in nodes.Concat(introNodes))
			{
				if (node.refObject == old) { node.refObject = rig; replaced++; }
				if (node.refObject2 == old) { node.refObject2 = rig; replaced++; }
			}
			UnityEngine.Object.DestroyImmediate(old);
			Log(rig.name + ": battle MinionPig" + (oldGear != null ? " with " + string.Join(", ", oldGear.m_Swaps.Select(g => g.TextureName == string.Empty ? "no " + g.BoneName : g.TextureName).ToArray()) : string.Empty));
		}
		foreach (var mesh in new[] { "Assets/Mesh/Mesh_515_MinionPigE.asset", "Assets/Mesh/Mesh_515_MinionPigC.asset" })
		{
			AssetDatabase.DeleteAsset(mesh);
		}

		// The swarm's walk cycles out of step.
		var newRigs = paths.Select(p => Find(scene, MidgroundPath + p).gameObject).ToList();
		var desynced = 0;
		foreach (var node in nodes.Where(n => n.type == NodeType.PlayBoneAnimation && n.text == "Move_Loop" && newRigs.Contains(n.refObject as GameObject)))
		{
			node.customBool2 = true;
			desynced++;
		}

		Finish(intro, introNodes);
		Finish(lost, nodes);
		Log("trees revised (" + replaced + " minion references moved, " + desynced + " walk cycles desynced): Wiz +0.2 s, Porky +0.4 s, rogue wind-up/lunge with Red hit first, bird catcher starts after the trip and runs above Chuck");
		return true;
	}

	private static SceneRigGear.Swap Gear(string bone, string texture)
	{
		return new SceneRigGear.Swap { BoneName = bone, TextureName = texture };
	}

	private static void AddGear(Transform rig, SceneRigGear.Swap[] swaps)
	{
		var gear = rig.gameObject.AddComponent<SceneRigGear>();
		gear.m_Swaps = swaps.ToList();
		EditorUtility.SetDirty(gear);
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

	private static ActionNode Clone(ActionNode source)
	{
		var copy = new ActionNode();
		foreach (var field in typeof(ActionNode).GetFields(BindingFlags.Public | BindingFlags.Instance))
		{
			var value = field.GetValue(source);
			var array = value as Array;
			field.SetValue(copy, array != null ? array.Clone() : value);
		}
		return copy;
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
