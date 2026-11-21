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

// Second pass on the intro battle (battle_000) cutscenes, after IntroBattleCutsceneBuilder. Both cutscenes now use the
// battle's own combatants instead of the scene's stand-ins, and no cutscene changes the music.
// - BS_00_PigsRaidCamp now runs before wave 1, after the birds' wave-clear cheer and once Porky and Wiz Pig (flying)
//   have entered to their battle slots: camera in, both pigs taunt, the birds rage halfway through, camera back out.
// - BS_00_PigsRaidCamp_AfterLoss (lost tree): both pigs laugh, Porky turns right and orders the raid, Wiz Pig swoops
//   offscreen left (lowest over Chuck), only Red gets up. Chuck stays down: his moves, the lightning and his exit are
//   gone. The rogue Red fights is MinionPigC (it went for Chuck, so it is the one nearest Red), anchored to Red so it
//   stops in front of him; MinionPigB takes MinionPigC's run. Red no longer walks off (Red_Move).
// One-shot: refuses once the lost tree has a MoveArc node. Menu item, or Temp/IntroBattleCutsceneRevision.request.
public static class IntroBattleCutsceneRevision
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroBattleCutsceneRevision.request";

	private const string LogFile = "Temp/IntroBattleCutsceneRevision.log";

	private const string SequencesPath = "Battleground_BirdCamp_01/2_StorySequences";

	private const string MidgroundPath = "Battleground_BirdCamp_01/1_Scenery/2_Landscape/3_Midground/";

	// Where Red's rogue stops: this far in front of Red (battle plane x), on his row. MinionPigB stopped about this far
	// from Red once Red had walked up to it.
	private const float RogueGapX = 250f;

	// MinionPigC_Move's last key (its Animation child, container space).
	private static readonly Vector3 RogueRunEnd = new Vector3(-929.3036f, 0f, 91.38677f);

	// Taunt lengths: Porky 1.5 s, Wiz Pig 2 s. The birds rage a little after the halfway point.
	private const float RageAfterTaunt = 0.9f;

	private const float CameraTime = 1.5f;

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

	[MenuItem("Tools/Build/Battle/Revise Intro Battle Cutscenes (2)")]
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
		Debug.Log("[IntroBattleCutsceneRevision]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static bool ReviseInternal(Scene scene)
	{
		var sequences = Find(scene, SequencesPath);
		var introGo = sequences.Find("BS_00_PigsRaidCamp");
		var lostGo = sequences.Find("BS_00_PigsRaidCamp_AfterLoss");
		if (introGo == null || lostGo == null)
		{
			Log("run IntroBattleCutsceneBuilder first, unchanged");
			return false;
		}
		var intro = introGo.GetComponent<ActionTree>();
		var lost = lostGo.GetComponent<ActionTree>();
		if (lost.nodes.Any(n => n.type == NodeType.MoveArc))
		{
			Log("already revised, unchanged");
			return false;
		}
		var introById = intro.nodes.ToDictionary(n => n.nodeID);
		var lostById = lost.nodes.ToDictionary(n => n.nodeID);
		if (!introById.ContainsKey(180) || !introById.ContainsKey(181) || !introById.ContainsKey(182) || lostById.Count != 100 || lostById[36].text != "MinionPigB_Move" || lostById[56].text != "MinionPigC_Move")
		{
			Log("trees are not the ones IntroBattleCutsceneBuilder made, unchanged");
			return false;
		}

		var b = new Minion(Find(scene, MidgroundPath + "MinionPigB"));
		var c = new Minion(Find(scene, MidgroundPath + "MinionPigC"));
		var redNew = Find(scene, "BattlegroundSetup/3_Characters/Container_Red/Animation/RedBird_new").gameObject;
		var redContainer = Find(scene, "BattlegroundSetup/3_Characters/Container_Red").gameObject;

		ReviseIntroTree(intro, introById);
		ReviseLostTree(lost, lostById, b, c, redNew, redContainer);

		// Wave 1, before its fight (after the pigs entered); no longer after wave 0.
		var execute = sequences.GetComponent<ExecuteBattleActionTree>();
		var serialized = new SerializedObject(execute);
		serialized.FindProperty("m_WaveIndex").intValue = 1;
		serialized.FindProperty("m_ExecuteBeforeWave").boolValue = true;
		serialized.FindProperty("m_ExecuteWhenWon").boolValue = true;
		serialized.FindProperty("m_BlockBattleExecution").boolValue = true;
		serialized.FindProperty("m_LetPigsWaitForEnter").boolValue = false;
		serialized.ApplyModifiedPropertiesWithoutUndo();
		Log("2_StorySequences: BS_00_PigsRaidCamp plays before wave 1, after the pigs entered");

		var standIns = lostGo.GetComponents<MonoBehaviour>().Where(m => m != null && m.GetType().Name == "CutsceneStandIns").ToList();
		foreach (var standIn in standIns)
		{
			UnityEngine.Object.DestroyImmediate(standIn);
		}
		Log("removed " + standIns.Count + " CutsceneStandIns component(s)");
		return true;
	}

	private class Minion
	{
		public readonly GameObject Container;

		public readonly GameObject Animation;

		public readonly GameObject Rig;

		public Minion(Transform container)
		{
			Container = container.gameObject;
			Animation = container.Find("Animation").gameObject;
			Rig = container.Find("Animation/" + container.name).gameObject;
		}
	}

	private static void ReviseIntroTree(ActionTree tree, Dictionary<int, ActionNode> byId)
	{
		var cameraIn = Clone(byId[9]);
		cameraIn.customFloat = CameraTime;
		var zoomIn = Clone(byId[10]);
		zoomIn.customFloat2 = CameraTime;
		var cameraOut = Clone(byId[180]);
		cameraOut.customFloat = CameraTime;
		var zoomOut = Clone(byId[181]);
		zoomOut.customFloat2 = CameraTime;
		var chain = new List<ActionNode>
		{
			Clone(byId[1]),
			Clone(byId[2]),
			FindCombatant(byId[1], "pig_prince_porky_intro", "Porky"),
			FindCombatant(byId[1], "pig_wiz_pig_intro", "Wiz"),
			// Wiz Pig flies the rest of his way in (the battle walks him in with Move_Loop, then idles him on arrival).
			Bone("Wiz", "Move_Fly", false, null),
			Delay(0.75f),
			Clone(byId[8]),
			cameraIn,
			zoomIn,
			Delay(CameraTime),
			Bone("Porky", "Taunt", false, "Idle"),
			Bone("Wiz", "Taunt", false, "Idle"),
			Delay(RageAfterTaunt),
			Bone("RedBird", "Rage", false, "Idle"),
			Delay(0.2f),
			Bone("YellowBird", "Rage", true, "Idle"),
			Delay(0.3f),
			cameraOut,
			zoomOut,
			Delay(CameraTime),
			Clone(byId[182])
		};
		Finish(tree, chain);
		Log("BS_00_PigsRaidCamp: " + chain.Count + " nodes (camera in, taunts, rage at " + RageAfterTaunt + " s, camera out; no music)");
	}

	private static void ReviseLostTree(ActionTree tree, Dictionary<int, ActionNode> byId, Minion b, Minion c, GameObject redNew, GameObject redContainer)
	{
		var swap = new Dictionary<UnityEngine.Object, UnityEngine.Object>
		{
			{ b.Container, c.Container }, { b.Animation, c.Animation }, { b.Rig, c.Rig },
			{ c.Container, b.Container }, { c.Animation, b.Animation }, { c.Rig, b.Rig }
		};
		Func<int, ActionNode> raid = id =>
		{
			var node = Clone(byId[id]);
			node.refObject = Swap(swap, node.refObject);
			node.refObject2 = Swap(swap, node.refObject2);
			// Each container only has its own run clip.
			if (node.text == "MinionPigB_Move" || node.text == "MinionPigC_Move")
			{
				node.text = node.text == "MinionPigB_Move" ? "MinionPigC_Move" : "MinionPigB_Move";
			}
			// Red is the battle's own combatant now (no swap to the scene rig).
			if (node.refObject == redNew)
			{
				node.refObject = null;
				node.objectType = 1;
				node.objectName = "RedBird";
			}
			return node;
		};
		Func<int, ActionNode> hitOnRed = id =>
		{
			var node = Clone(byId[id]);
			node.objectType = 1;
			node.text = "RedBird";
			return node;
		};
		var anchorRogue = NewNode(NodeType.SetPosition);
		anchorRogue.refObject = c.Container;
		anchorRogue.customInt = 3;
		anchorRogue.text = "RedBird";
		// Container = Red + (gap - run end) on the battle plane, which is tilted like the midground (-45 degrees about x).
		anchorRogue.customVec2 = c.Container.transform.rotation * (new Vector3(RogueGapX, 0f, 0f) - RogueRunEnd);

		var chain = new List<ActionNode>
		{
			Clone(byId[1]),
			Clone(byId[2]),
			FindCombatant(byId[1], "pig_prince_porky_intro", "Porky"),
			FindCombatant(byId[1], "pig_wiz_pig_intro", "Wiz"),
			Clone(byId[3]),
			Clone(byId[5]),
			Clone(byId[6]),
			Delay(CameraTime),
			// Both pigs laugh.
			Bone("Porky", "Taunt", false, "Idle"),
			Bone("Wiz", "Taunt", true, "Idle"),
			// Porky turns right (pig rigs face left at +1) to send the minions in.
			Scale("Porky", -1f),
			Delay(0.2f),
			Bone("Porky", "Action_Support", true, "Idle"),
			WizSwoop(),
			Delay(0.4f),
			Scale("Porky", 1f),
			// Only Red gets back up.
			Bone("RedBird", "KnockedOutEnd", true, "Idle"),
			raid(24), raid(25), raid(26),
			// Red_Turn: Red looks back as MinionPigA runs past.
			Scale("RedBird", -1f),
			raid(28),
			Scale("RedBird", 1f),
			Hide("Wiz"),
			raid(29), raid(30), raid(31), raid(32), raid(33), raid(34), raid(35),
			anchorRogue,
			raid(36), raid(37), raid(38), raid(39), raid(42)
		};
		var waitForRogue = raid(44);
		// MinionPigC's run is 0.5 s longer than MinionPigB's, and Red no longer walks up to it.
		waitForRogue.customFloat = 1.5f;
		chain.Add(waitForRogue);
		chain.AddRange(new[] { raid(45), raid(46), raid(47), raid(48), raid(49), raid(53), raid(55), raid(56), raid(57), raid(58), raid(59), raid(61), hitOnRed(62), raid(63), raid(64), raid(65) });
		// Chuck's lightning strike (and the minion it killed) is gone; a beat before Red strikes again.
		chain.Add(Delay(0.6f));
		chain.AddRange(new[] { raid(77), raid(78), raid(79), raid(80), raid(81), raid(82), raid(84), raid(85), raid(86), hitOnRed(87), raid(88), raid(89), raid(91), raid(93), raid(94), raid(95), raid(96), raid(97), raid(98), raid(99) });
		Finish(tree, chain);
		Log("BS_00_PigsRaidCamp_AfterLoss: " + chain.Count + " nodes (Red's rogue is MinionPigC, " + RogueGapX + " in front of Red; Wiz swoops over Chuck; no music)");
	}

	// Wiz Pig leaves to the left, lowest over Chuck, back at his own height offscreen.
	private static ActionNode WizSwoop()
	{
		var node = NewNode(NodeType.MoveArc);
		node.objectType = 1;
		node.objectName = "Wiz";
		node.secondaryText = "YellowBird";
		node.text = "Move_Fly";
		node.customFloat = 2.4f;
		node.customFloat2 = 150f;
		node.customVec1 = new Vector3(-1800f, 0f, 0f);
		return node;
	}

	private static UnityEngine.Object Swap(Dictionary<UnityEngine.Object, UnityEngine.Object> swap, UnityEngine.Object value)
	{
		UnityEngine.Object swapped;
		return value != null && swap.TryGetValue(value, out swapped) ? swapped : value;
	}

	private static ActionNode FindCombatant(ActionNode findBird, string nameId, string runtimeName)
	{
		var node = Clone(findBird);
		node.text = nameId;
		node.secondaryText = runtimeName;
		return node;
	}

	private static ActionNode Bone(string runtimeName, string clip, bool wait, string queued)
	{
		var node = NewNode(NodeType.PlayBoneAnimation);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.text = clip;
		node.QueueIdle = queued != null;
		node.secondaryText = queued ?? string.Empty;
		node.customInt = wait ? 1 : 0;
		return node;
	}

	private static ActionNode Scale(string runtimeName, float x)
	{
		var node = NewNode(NodeType.SetScale);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.customVec1 = new Vector3(x, 1f, 1f);
		return node;
	}

	private static ActionNode Hide(string runtimeName)
	{
		var node = NewNode(NodeType.SetActive);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.customBool = false;
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
