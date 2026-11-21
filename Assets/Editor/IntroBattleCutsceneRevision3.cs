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

// Third pass on the intro battle (battle_000), after IntroBattleCutsceneRevision: Prince Porky and Wiz Pig make their
// original cutscene entrances during BS_00_PigsRaidCamp instead of walking in before it. BattleMgr puts them on their
// battle slots without the walk-in; MoveByClip nodes start them at the props' clip start poses (same frame) and play
// PrincePorky_Enter / CharacterContainer_BS_02_WizAppears as offsets that end on the slots, with the original bone
// clips and beats (Porky walks, Wiz flies; Porky is surprised by Wiz, Wiz cheers) before the taunts.
// The tree now runs before the pigs' (skipped) walk-in. One-shot: refuses once the tree has a MoveByClip node.
// Menu item, or Temp/IntroBattleCutsceneRevision3.request.
public static class IntroBattleCutsceneRevision3
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroBattleCutsceneRevision3.request";

	private const string LogFile = "Temp/IntroBattleCutsceneRevision3.log";

	private const string SequencesPath = "Battleground_BirdCamp_01/2_StorySequences";

	private const string PorkyPropPath = "Battleground_BirdCamp_01/1_Scenery/2_Landscape/3_Midground/PrincePorky";

	private const string ForegroundPath = "Battleground_BirdCamp_01/1_Scenery/2_Landscape/4_Foreground";

	private const string PorkyEnterClip = "Assets/AnimationClip/PrincePorky_Enter.anim";

	private const string WizAppearsClip = "Assets/AnimationClip/CharacterContainer_BS_02_WizAppears.anim";

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

	[MenuItem("Tools/Build/Battle/Revise Intro Battle Cutscenes (3)")]
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
		Debug.Log("[IntroBattleCutsceneRevision3]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static bool ReviseInternal(Scene scene)
	{
		var sequences = Find(scene, SequencesPath);
		var tree = sequences.Find("BS_00_PigsRaidCamp").GetComponent<ActionTree>();
		if (tree.nodes.Any(n => n.type == NodeType.MoveByClip))
		{
			Log("already revised, unchanged");
			return false;
		}
		var byId = tree.nodes.ToDictionary(n => n.nodeID);
		if (byId.Count != 21 || byId[5].text != "Move_Fly" || byId[11].text != "Taunt")
		{
			Log("BS_00_PigsRaidCamp is not the tree IntroBattleCutsceneRevision made, unchanged");
			return false;
		}
		var porkyEnter = AssetDatabase.LoadAssetAtPath<AnimationClip>(PorkyEnterClip);
		var wizAppears = AssetDatabase.LoadAssetAtPath<AnimationClip>(WizAppearsClip);
		if (porkyEnter == null || wizAppears == null)
		{
			Log("entrance clips not found, unchanged");
			return false;
		}
		// PrincePorky_Enter moves the prop's Animation child (prop space); WizAppears moves Container_WizPig itself
		// (4_Foreground space).
		var porkySpace = Find(scene, PorkyPropPath);
		var wizSpace = Find(scene, ForegroundPath);

		var chain = new List<ActionNode> { byId[1], byId[2], byId[3], byId[4] };
		chain.Add(ClipMove("Porky", porkyEnter, "Animation", porkySpace, true));
		chain.Add(ClipMove("Wiz", wizAppears, string.Empty, wizSpace, true));
		chain.Add(Bone("Wiz", "FlyingState", null));
		chain.AddRange(new[] { byId[7], byId[8], byId[9] });
		chain.Add(Bone("Porky", "Move_Loop", null));
		chain.Add(ClipMove("Porky", porkyEnter, "Animation", porkySpace, false));
		chain.Add(Delay(1f));
		chain.Add(Bone("Wiz", "Move_Fly", null));
		chain.Add(ClipMove("Wiz", wizAppears, string.Empty, wizSpace, false));
		chain.Add(Delay(0.7f));
		chain.Add(Bone("Porky", "Surprised", "Idle"));
		chain.Add(Delay(0.3f));
		chain.Add(Bone("Wiz", "Cheer", "Idle"));
		chain.Add(Delay(1f));
		chain.AddRange(Enumerable.Range(11, 11).Select(id => byId[id]));
		Finish(tree, chain);
		Log("BS_00_PigsRaidCamp: " + chain.Count + " nodes; Porky enters (" + porkyEnter.length.ToString("0.00") + " s) and Wiz appears (" + wizAppears.length.ToString("0.00") + " s) on their slots before the taunts");

		var execute = sequences.GetComponent<ExecuteBattleActionTree>();
		var serialized = new SerializedObject(execute);
		serialized.FindProperty("m_LetPigsWaitForEnter").boolValue = true;
		serialized.ApplyModifiedPropertiesWithoutUndo();
		Log("2_StorySequences: the tree runs before the pigs' walk-in (skipped for this wave)");
		return true;
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
