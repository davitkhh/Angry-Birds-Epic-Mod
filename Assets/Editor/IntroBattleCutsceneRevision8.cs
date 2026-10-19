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

// Eighth pass on the intro battle (battle_000), after IntroBattleCutsceneRevision7.
// - The raid's corporal (MinionPigE) and stick pig (MinionPigC) rigs ignore bone texture swaps on the bones their
//   original scene setup hid (verified with SceneRigGearProbe; MinionPigB's rig takes them). Both are replaced by clones
//   of MinionPigB's rig (own copy of its mesh) wearing their gear through SceneRigGear: E the Lolly Popper's corporal
//   set, C the bird catcher's set. Tree references to the old rigs are moved to the clones.
// - MinionPigC runs past Chuck to the left: its run is anchored to Chuck and carries on offscreen at the swarm's pace.
// - Red's rogue lunges with its overhand attack (Action_Attack_2) and stands a little behind Red's row (equal depth
//   made their shadows flicker).
// - Earlier: the birds' rage at Porky and Wiz Pig's entrance, Red's look back and his surprise during the raid.
// One-shot: refuses once MinionPigE is a clone (has MainHand_Corporal gear). Menu item, or
// Temp/IntroBattleCutsceneRevision8.request.
public static class IntroBattleCutsceneRevision8
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroBattleCutsceneRevision8.request";

	private const string LogFile = "Temp/IntroBattleCutsceneRevision8.log";

	private const string SequencesPath = "Battleground_BirdCamp_01/2_StorySequences";

	private const string MidgroundPath = "Battleground_BirdCamp_01/1_Scenery/2_Landscape/3_Midground/";

	private const float RogueGapX = 180f;

	private const float RogueLunge = 120f;

	// Red's rogue stands this far behind Red's row (battle plane z).
	private const float RogueDepthBehind = 15f;

	// MinionPigC_Move's last key (its Animation child, container space).
	private static readonly Vector3 StickRunEnd = new Vector3(-929.3036f, 0f, 91.38677f);

	// Where MinionPigC's run ends relative to Chuck (battle plane): a little right of him and in front.
	private static readonly Vector3 StickRunEndFromChuck = new Vector3(100f, 0f, -50f);

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

	[MenuItem("Tools/Battle/Revise Intro Battle Cutscenes (8)")]
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
		Debug.Log("[IntroBattleCutsceneRevision8]\n" + s_Log);
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
		var rogue = Find(scene, MidgroundPath + "MinionPigB").gameObject;
		var rogueRig = Find(scene, MidgroundPath + "MinionPigB/Animation/MinionPigB").gameObject;
		var stick = Find(scene, MidgroundPath + "MinionPigC").gameObject;
		var oldStickRig = Find(scene, MidgroundPath + "MinionPigC/Animation/MinionPigC").gameObject;
		var oldCorporalRig = Find(scene, MidgroundPath + "MinionPigA/Animation2/MinionPigE").gameObject;
		var oldGear = oldCorporalRig.GetComponent<SceneRigGear>();
		if (oldGear != null && oldGear.m_Swaps.Any(g => g.TextureName == "MainHand_Corporal"))
		{
			Log("already revised, unchanged");
			return false;
		}
		var introNodes = Ordered(intro);
		var nodes = Ordered(lost);
		var rage = introNodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "RedBird" && n.text == "Rage");
		var redTurn = nodes.FindIndex(n => n.type == NodeType.SetScale && n.objectName == "RedBird" && n.customVec1.x < 0f);
		var anchor = nodes.FirstOrDefault(n => n.type == NodeType.SetPosition && n.customInt == 3 && n.refObject == rogue);
		var lungeAttack = nodes.FirstOrDefault(n => n.type == NodeType.PlayBoneAnimation && n.refObject == rogueRig && n.text == "Action_Attack_1");
		var stickRun = nodes.FindIndex(n => n.type == NodeType.PlayAnimation && n.text == "MinionPigC_Move");
		var stickIdle = nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.refObject == oldStickRig && n.text == "Idle");
		if (rage < 1 || introNodes[rage - 1].type != NodeType.Delay || redTurn < 1 || nodes[redTurn - 1].type != NodeType.Delay || nodes[redTurn + 1].type != NodeType.Delay || anchor == null || lungeAttack == null || stickRun < 0 || stickIdle < 0)
		{
			Log("trees are not the expected ones, unchanged");
			return false;
		}

		// Timing.
		introNodes[rage - 1].customFloat = 0.6f;
		nodes[redTurn - 1].customFloat = 0.6f;
		nodes[redTurn + 1].customFloat = 0.15f;

		// Red's rogue: overhand lunge, a little behind Red's row.
		lungeAttack.text = "Action_Attack_2";
		anchor.customVec2 = rogue.transform.rotation * (new Vector3(RogueGapX + RogueLunge, 0f, RogueDepthBehind) - RogueRunEnd);

		// The stick pig runs past Chuck and on to the left.
		var stickAnchor = NewNode(NodeType.SetPosition);
		stickAnchor.refObject = stick;
		stickAnchor.customInt = 3;
		stickAnchor.text = "YellowBird";
		stickAnchor.customVec2 = stick.transform.rotation * (StickRunEndFromChuck - StickRunEnd);
		var runOn = NewNode(NodeType.MoveArc);
		runOn.refObject = stick;
		runOn.secondaryText = "YellowBird";
		runOn.customFloat = StickRunOn / SwarmSpeed;
		runOn.customFloat2 = 0f;
		// The container ends where its rig (StickRunEnd.x into it) is StickRunOn past the run's end.
		runOn.customVec1 = new Vector3(StickRunEndFromChuck.x - StickRunOn - StickRunEnd.x, 0f, 0f);
		nodes[stickIdle] = runOn;
		nodes.Insert(stickRun, stickAnchor);

		// E and C become clones of the rogue's rig in their gear.
		var corporalRig = CloneRig(rogueRig, oldCorporalRig, "Assets/Mesh/Mesh_515_MinionPigE.asset");
		AddGear(corporalRig.transform, new[] { Gear("Headgear", "Headgear_Corporal"), Gear("Headgear_Back", "Headgear_Corporal_Back"), Gear("MainHand", "MainHand_Corporal"), Gear("OffHand", string.Empty) });
		var stickRig = CloneRig(rogueRig, oldStickRig, "Assets/Mesh/Mesh_515_MinionPigC.asset");
		AddGear(stickRig.transform, new[] { Gear("Headgear", "Headgear_BirdCatcher"), Gear("Headgear_Back", "Headgear_BirdCatcher_Back"), Gear("MainHand", "MainHand_BirdCatcher"), Gear("OffHand", "OffHand_BirdCatcher") });
		var moved = 0;
		foreach (var node in nodes.Concat(introNodes))
		{
			if (node.refObject == oldCorporalRig) { node.refObject = corporalRig; moved++; }
			if (node.refObject == oldStickRig) { node.refObject = stickRig; moved++; }
			if (node.refObject2 == oldCorporalRig) { node.refObject2 = corporalRig; moved++; }
			if (node.refObject2 == oldStickRig) { node.refObject2 = stickRig; moved++; }
		}
		UnityEngine.Object.DestroyImmediate(oldCorporalRig);
		UnityEngine.Object.DestroyImmediate(oldStickRig);
		Log("MinionPigE and MinionPigC replaced by rogue rig clones (" + moved + " node references moved)");

		Finish(intro, introNodes);
		Finish(lost, nodes);
		Log("trees revised: rage 0.6 s after the taunts, Red turns 0.6 s into the raid and is surprised 0.15 s later, overhand lunge " + RogueDepthBehind + " behind Red's row, stick pig runs past Chuck");
		return true;
	}

	private static GameObject CloneRig(GameObject source, GameObject replaces, string meshPath)
	{
		var clone = UnityEngine.Object.Instantiate(source, replaces.transform.parent);
		clone.name = replaces.name;
		clone.transform.localPosition = replaces.transform.localPosition;
		clone.transform.localRotation = replaces.transform.localRotation;
		clone.transform.localScale = replaces.transform.localScale;
		clone.transform.SetSiblingIndex(replaces.transform.GetSiblingIndex());
		foreach (var gear in clone.GetComponents<SceneRigGear>())
		{
			UnityEngine.Object.DestroyImmediate(gear);
		}
		var sourceRenderer = source.GetComponent<SkinnedMeshRenderer>();
		var sourceMeshPath = AssetDatabase.GetAssetPath(sourceRenderer.sharedMesh);
		if (!File.Exists(meshPath) && !AssetDatabase.CopyAsset(sourceMeshPath, meshPath))
		{
			throw new Exception("could not copy " + sourceMeshPath + " to " + meshPath);
		}
		var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
		var renderer = clone.GetComponent<SkinnedMeshRenderer>();
		renderer.sharedMesh = mesh;
		var bones = clone.GetComponents<MonoBehaviour>().First(m => m != null && m.GetType().Name == "BoneAnimation");
		var serialized = new SerializedObject(bones);
		serialized.FindProperty("mMesh").objectReferenceValue = mesh;
		serialized.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(renderer);
		Log(replaces.name + ": clone of " + source.name + " with mesh " + meshPath);
		return clone;
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
