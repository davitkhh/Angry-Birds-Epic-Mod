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

// Splits the intro battle's (battle_000) cutscene around a scripted loss against Prince Porky and Wiz Pig.
// - BS_00_PigsRaidCamp (after wave 0): ends once both pigs have taunted. Wiz Pig no longer downgrades the birds' gear
//   (cast, transformation flash, swap to the *_new rigs, mourning); the camera goes back, the battle music resumes and
//   wave 1 (Porky and Wiz) starts. Superseded by IntroBattleCutsceneRevision.
// - BS_00_PigsRaidCamp_AfterLoss (new, lost tree): the birds get up, are swapped for the scene's *_new rigs, Porky laughs
//   and orders the raid, and the original sequence plays on from Porky leaving (Wiz Pig's own order is left out).
// One-shot: refuses to run once the lost tree exists. Menu item, or Temp/IntroBattleCutsceneBuilder.request.
public static class IntroBattleCutsceneBuilder
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroBattleCutsceneBuilder.request";

	private const string LogFile = "Temp/IntroBattleCutsceneBuilder.log";

	private const string SequencesPath = "Battleground_BirdCamp_01/2_StorySequences";

	private const string LostTreeName = "BS_00_PigsRaidCamp_AfterLoss";

	private const string PorkyActorPath = "Battleground_BirdCamp_01/1_Scenery/2_Landscape/3_Midground/PrincePorky";

	private const string WizActorPath = "Battleground_BirdCamp_01/1_Scenery/2_Landscape/4_Foreground/Container_WizPig";

	private const string BattleId = "battle_000";

	private const int BossWaveIndex = 1;

	// Original node ids in BS_00_PigsRaidCamp.
	private const int RageYellowDelay = 66;

	private const int FirstDowngradeNode = 26;

	private const int WizTaunt = 165;

	private const int PorkyTaunt = 15;

	private const int RaidStart = 166;

	private const int WizOrder = 90;

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

	[MenuItem("Tools/Build/Battle/Build Intro Battle Cutscenes")]
	public static void Build()
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
			if (BuildInternal(scene))
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
		Debug.Log("[IntroBattleCutsceneBuilder]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static bool BuildInternal(Scene scene)
	{
		var sequences = Find(scene, SequencesPath);
		if (sequences.Find(LostTreeName) != null)
		{
			Log(LostTreeName + " already exists, unchanged");
			return false;
		}
		var tree = sequences.Find("BS_00_PigsRaidCamp").GetComponent<ActionTree>();
		var original = ChainOrder(tree);
		var ids = original.Select(n => n.nodeID).ToList();
		var downgrade = ids.IndexOf(RageYellowDelay);
		if (downgrade < 0 || downgrade + 1 >= ids.Count || ids[downgrade + 1] != FirstDowngradeNode || !ids.Contains(WizTaunt) || !ids.Contains(RaidStart))
		{
			Log("BS_00_PigsRaidCamp is not the original intro cutscene, unchanged");
			return false;
		}
		var porkyActor = Find(scene, PorkyActorPath);
		var wizActor = Find(scene, WizActorPath);
		var raid = original.Skip(ids.IndexOf(RaidStart)).Where(n => n.nodeID != WizOrder).ToList();
		var byId = original.ToDictionary(n => n.nodeID);

		BuildLostTree(sequences, byId, raid, porkyActor, wizActor);
		TrimIntroTree(tree, original, byId);
		return true;
	}

	// Wave 0 won: everything up to the birds' rage, then both taunts, then back to the battle.
	private static void TrimIntroTree(ActionTree tree, List<ActionNode> original, Dictionary<int, ActionNode> byId)
	{
		var ids = original.Select(n => n.nodeID).ToList();
		var chain = original.Take(ids.IndexOf(RageYellowDelay) + 1).ToList();
		chain.Add(byId[WizTaunt]);
		chain.Add(byId[PorkyTaunt]);
		var nextId = original.Max(n => n.nodeID) + 1;
		var anchor = byId[PorkyTaunt].editorRect;
		var cameraBack = Clone(byId[46], nextId++);
		cameraBack.customFloat = 1.5f;
		var zoomBack = Clone(byId[45], nextId++);
		zoomBack.customFloat2 = 1.5f;
		var storyEnd = Clone(byId[48], nextId++);
		storyEnd.customBool = false;
		var added = new List<ActionNode>
		{
			Delay(nextId++, 0.5f),
			cameraBack,
			zoomBack,
			Delay(nextId++, 1.5f),
			Sound(nextId++, "Music_Combat_Special"),
			storyEnd
		};
		for (var i = 0; i < added.Count; i++)
		{
			added[i].editorRect = new Rect(anchor.x + 230f * (i + 1), anchor.y + 220f, 200f, 120f);
		}
		chain.AddRange(added);
		Link(chain);
		var removed = original.Count + added.Count - chain.Count;
		tree.nodes = chain.ToArray();
		tree.startNode = chain[0].nodeID;
		EditorUtility.SetDirty(tree);
		Log("BS_00_PigsRaidCamp: " + chain.Count + " nodes, ends after Porky's taunt (node " + PorkyTaunt + "); removed " + removed + " nodes (gear downgrade and the raid, now in " + LostTreeName + ")");
	}

	// Wave 1 lost: birds up and swapped for the *_new rigs, Porky laughs and orders the raid, then the original raid.
	private static void BuildLostTree(Transform sequences, Dictionary<int, ActionNode> byId, List<ActionNode> raid, Transform porkyActor, Transform wizActor)
	{
		var go = new GameObject(LostTreeName);
		go.transform.SetParent(sequences, false);
		var tree = go.AddComponent<ActionTree>();
		var nextId = 1;
		var porkyTaunt = Clone(byId[PorkyTaunt], 0);
		var porkyOrder = Clone(byId[PorkyTaunt], 0);
		porkyOrder.text = "Action_Support";
		var cameraIn = Clone(byId[9], 0);
		cameraIn.customFloat = 1.5f;
		var zoomIn = Clone(byId[10], 0);
		zoomIn.customFloat2 = 1.5f;
		var chain = new List<ActionNode>
		{
			Clone(byId[1], 0),
			Clone(byId[2], 0),
			Clone(byId[8], 0),
			Clone(byId[153], 0),
			cameraIn,
			zoomIn,
			Delay(0, 1.5f),
			porkyTaunt,
			BoneAnimation("RedBird", "KnockedOutEnd", false),
			Delay(0, 0.15f),
			BoneAnimation("YellowBird", "KnockedOutEnd", true),
			Clone(byId[76], 0),
			Clone(byId[74], 0),
			Clone(byId[77], 0),
			Clone(byId[75], 0),
			Delay(0, 0.3f),
			porkyOrder
		};
		chain.AddRange(raid.Select(n => Clone(n, 0)));
		for (var i = 0; i < chain.Count; i++)
		{
			chain[i].nodeID = nextId++;
			chain[i].editorRect = new Rect(20f + 230f * (i % 10), 20f + 220f * (i / 10), 200f, 180f);
		}
		Link(chain);
		tree.nodes = chain.ToArray();
		tree.startNode = chain[0].nodeID;
		EditorUtility.SetDirty(tree);

		var execute = go.AddComponent<ExecuteBattleActionTree>();
		var serialized = new SerializedObject(execute);
		serialized.FindProperty("m_ActionTree").objectReferenceValue = tree;
		serialized.FindProperty("m_BattleID").stringValue = BattleId;
		serialized.FindProperty("m_WaveIndex").intValue = BossWaveIndex;
		serialized.FindProperty("m_ExecuteBeforeWave").boolValue = false;
		serialized.FindProperty("m_ExecuteWhenWon").boolValue = false;
		serialized.FindProperty("m_BlockBattleExecution").boolValue = true;
		serialized.FindProperty("m_LetPigsWaitForEnter").boolValue = false;
		serialized.ApplyModifiedPropertiesWithoutUndo();

		Log(LostTreeName + ": " + chain.Count + " nodes (" + raid.Count + " from the original raid, from node " + RaidStart + "; Wiz Pig's order node " + WizOrder + " left out), lost tree for " + BattleId + "");
	}

	private static ActionNode BoneAnimation(string runtimeName, string clip, bool wait)
	{
		var node = NewNode(0, NodeType.PlayBoneAnimation);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.text = clip;
		node.QueueIdle = true;
		node.secondaryText = "Idle";
		node.customInt = wait ? 1 : 0;
		return node;
	}

	private static ActionNode Delay(int id, float seconds)
	{
		var node = NewNode(id, NodeType.Delay);
		node.customFloat = seconds;
		return node;
	}

	private static ActionNode Sound(int id, string sound)
	{
		var node = NewNode(id, NodeType.PlaySound);
		node.text = sound;
		return node;
	}

	private static ActionNode NewNode(int id, NodeType type)
	{
		return new ActionNode
		{
			nodeID = id,
			type = type,
			enabled = true,
			text = string.Empty,
			objectName = string.Empty,
			secondaryText = string.Empty,
			param = new string[0],
			nodesIn = new int[0],
			nodesOut = new int[0],
			nodesOutIndex = new int[0],
			editorRect = new Rect(0f, 0f, 200f, 120f)
		};
	}

	private static ActionNode Clone(ActionNode source, int id)
	{
		var copy = new ActionNode();
		foreach (var field in typeof(ActionNode).GetFields(BindingFlags.Public | BindingFlags.Instance))
		{
			var value = field.GetValue(source);
			var array = value as Array;
			field.SetValue(copy, array != null ? array.Clone() : value);
		}
		copy.nodeID = id;
		return copy;
	}

	private static void Link(List<ActionNode> chain)
	{
		for (var i = 0; i < chain.Count; i++)
		{
			chain[i].nodesIn = i > 0 ? new[] { chain[i - 1].nodeID } : new int[0];
			chain[i].nodesOut = i + 1 < chain.Count ? new[] { chain[i + 1].nodeID } : new int[0];
			chain[i].nodesOutIndex = i + 1 < chain.Count ? new[] { 0 } : new int[0];
		}
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
