using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Eleventh revision of the McCool robbery cutscene: the rogue leaves an air trail while he jumps onto McCool's bag.
// FX_RogueJumpTrail is a world map sized copy of the battle air streak (SkillFX_Generic_BodyAirstream_Subtle). It is
// attached to the rogue before his jump animation, left behind where he lands (detached, so it fades out instead of
// following his walk), and destroyed at the cleanup. Menu item, or Temp/McCoolCutsceneRevision11.request.
public static class McCoolCutsceneRevision11
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision11.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision11.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const string SourceTrail = "Assets/Resources/Prefabs/5_Skills/1_Generic/SkillFX_Generic_BodyAirstream_Subtle.prefab";

	private const string TrailPath = "Assets/Resources/Prefabs/1_WorldMap/2_Props/StorySequences/FX_RogueJumpTrail.prefab";

	// World map characters are drawn at 0.4x, and trail width ignores scale.
	private const float TrailWidth = 30f;

	private const float TrailTime = 0.35f;

	private const string TrailName = "RogueJumpTrail";

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (11)")]
	public static void Revise()
	{
		s_Log.Length = 0;
		try
		{
			var trail = CreateTrail();
			var scene = SceneManager.GetSceneByPath(ScenePath);
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
			}
			var hadUnsavedChanges = scene.isDirty;
			ReviseInternal(scene, trail);
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
		catch (Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[McCoolCutsceneRevision11]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static GameObject CreateTrail()
	{
		var existing = AssetDatabase.LoadAssetAtPath<GameObject>(TrailPath);
		if (existing != null)
		{
			return existing;
		}
		var contents = PrefabUtility.LoadPrefabContents(SourceTrail);
		contents.name = "FX_RogueJumpTrail";
		foreach (var renderer in contents.GetComponentsInChildren<TrailRenderer>(true))
		{
			renderer.widthMultiplier = TrailWidth;
			renderer.time = TrailTime;
		}
		var saved = PrefabUtility.SaveAsPrefabAsset(contents, TrailPath);
		PrefabUtility.UnloadPrefabContents(contents);
		Log("created " + TrailPath + " (width " + TrailWidth + ", time " + TrailTime + " s)");
		return saved;
	}

	private static void ReviseInternal(Scene scene, GameObject trail)
	{
		var sequence = Find(scene, SequencePath);
		var tree = sequence.GetComponent<ActionTree>();
		var order = ChainOrder(tree);
		var nodes = tree.nodes.ToList();
		if (order.Any(n => n.type == NodeType.Instantiate && n.objectName == TrailName))
		{
			Log("the rogue already has a jump trail, unchanged");
			return;
		}
		var apex = sequence.Find("Marker_RogueJumpApex");
		var mcCoolSpot = sequence.Find("Marker_McCool");
		var jump = order.FirstOrDefault(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "Rogue" && n.text == "Action_RageJump");
		var land = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.refObject1 == apex && n.refObject2 == mcCoolSpot);
		var cleanup = order.LastOrDefault(n => n.type == NodeType.Destroy && n.objectName == "Rogue");
		if (jump == null || land == null || cleanup == null)
		{
			Log("rogue's jump animation, landing or cleanup node not found; unchanged");
			return;
		}
		var nextId = nodes.Max(n => n.nodeID) + 1;
		// On the rogue's body, slightly behind him.
		var attach = NewNode(nextId++, NodeType.Instantiate, jump.editorRect, n =>
		{
			n.refObject = trail;
			n.objectType = 1;
			n.text = "Rogue";
			n.objectName = TrailName;
			n.customVec1 = new Vector3(0f, 20f, 3f);
		});
		InsertBefore(nodes, jump, attach);
		// Leave it where he lands so it fades out in place.
		var leave = NewNode(nextId++, NodeType.SetParent, land.editorRect, n =>
		{
			n.objectType = 1;
			n.objectName = TrailName;
			n.customInt = 0;
			n.refObject2 = sequence.gameObject;
		});
		InsertAfter(nodes, land, leave);
		var remove = NewNode(nextId++, NodeType.Destroy, cleanup.editorRect, n => { n.objectType = 1; n.objectName = TrailName; });
		InsertAfter(nodes, cleanup, remove);
		Log("trail attached before node " + jump.nodeID + " (node " + attach.nodeID + "), left behind after landing node " + land.nodeID + " (node " + leave.nodeID + "), destroyed after node " + cleanup.nodeID + " (node " + remove.nodeID + ")");
		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
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

	private static void InsertAfter(List<ActionNode> nodes, ActionNode after, ActionNode added)
	{
		var oldOut = after.nodesOut ?? new int[0];
		added.nodesOut = oldOut;
		added.nodesOutIndex = after.nodesOutIndex ?? new int[0];
		after.nodesOut = new[] { added.nodeID };
		after.nodesOutIndex = new[] { 0 };
		added.nodesIn = new[] { after.nodeID };
		foreach (var id in oldOut)
		{
			var target = nodes.First(n => n.nodeID == id);
			target.nodesIn = (target.nodesIn ?? new int[0]).Select(x => x == after.nodeID ? added.nodeID : x).ToArray();
		}
		nodes.Add(added);
	}

	private static void InsertBefore(List<ActionNode> nodes, ActionNode before, ActionNode added)
	{
		foreach (var from in nodes.Where(n => n.nodesOut != null && n.nodesOut.Contains(before.nodeID)))
		{
			from.nodesOut = from.nodesOut.Select(x => x == before.nodeID ? added.nodeID : x).ToArray();
		}
		added.nodesIn = before.nodesIn ?? new int[0];
		added.nodesOut = new[] { before.nodeID };
		added.nodesOutIndex = new[] { 0 };
		before.nodesIn = new[] { added.nodeID };
		nodes.Add(added);
	}

	private static ActionNode NewNode(int id, NodeType type, Rect anchor, Action<ActionNode> init)
	{
		var node = new ActionNode
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
			editorRect = new Rect(anchor.x + 260f, anchor.y + 260f, anchor.width, anchor.height)
		};
		init(node);
		return node;
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
