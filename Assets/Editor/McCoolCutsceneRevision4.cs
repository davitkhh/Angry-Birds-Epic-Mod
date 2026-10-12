using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Fourth revision of the McCool robbery cutscene: Red and Yellow turn to face the rogue when they notice him, face
// McCool when he turns to the shop, and face their default way again at the end. Both are left of the birds, and
// birds face right by default, so "face" is scale (-1,1,1). Menu item, or Temp/McCoolCutsceneRevision4.request.
public static class McCoolCutsceneRevision4
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision4.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision4.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private static readonly string[] Birds = { "RedBird", "YellowBird" };

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (4)")]
	public static void Revise()
	{
		s_Log.Length = 0;
		try
		{
			var scene = SceneManager.GetSceneByPath(ScenePath);
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
			}
			var hadUnsavedChanges = scene.isDirty;
			ReviseInternal(scene);
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
		Debug.Log("[McCoolCutsceneRevision4]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static void ReviseInternal(Scene scene)
	{
		var tree = Find(scene, SequencePath).GetComponent<ActionTree>();
		var order = ChainOrder(tree);
		var nodes = tree.nodes.ToList();
		if (order.Any(n => n.type == NodeType.SetScale && Birds.Contains(n.objectName)))
		{
			Log("the birds already have facing nodes, unchanged");
			return;
		}
		var nextId = nodes.Max(n => n.nodeID) + 1;
		var facingLeft = new Vector3(-1f, 1f, 1f);

		// When they notice the rogue (their Surprised reaction).
		var notice = order.FirstOrDefault(n => n.type == NodeType.PlayBoneAnimation && n.objectName == Birds[0] && n.text == "Surprised");
		// When McCool turns to the shop (his second facing node).
		var mcCoolTurn = order.Where(n => n.type == NodeType.SetScale && n.objectName == "McCool").Skip(1).FirstOrDefault();
		// Cleanup, where they go back to Idle.
		var cleanup = order.LastOrDefault(n => n.type == NodeType.PlayBoneAnimation && n.objectName == Birds[0] && n.text == "Idle");
		if (notice == null || mcCoolTurn == null || cleanup == null)
		{
			Log("birds' Surprised, McCool's turn or the birds' final Idle node not found; unchanged");
			return;
		}

		var faceRogue = Birds.Select(b => NewNode(nextId++, b, facingLeft, notice.editorRect)).ToArray();
		InsertBefore(nodes, notice, faceRogue);
		var faceMcCool = Birds.Select(b => NewNode(nextId++, b, facingLeft, mcCoolTurn.editorRect)).ToArray();
		InsertAfter(nodes, mcCoolTurn, faceMcCool);
		var faceDefault = Birds.Select(b => NewNode(nextId++, b, Vector3.one, cleanup.editorRect)).ToArray();
		InsertBefore(nodes, cleanup, faceDefault);
		Log("birds face the rogue before node " + notice.nodeID + " (nodes " + faceRogue[0].nodeID + "-" + faceRogue[1].nodeID + "), face McCool after node " + mcCoolTurn.nodeID + " (nodes " + faceMcCool[0].nodeID + "-" + faceMcCool[1].nodeID + "), face right again before node " + cleanup.nodeID + " (nodes " + faceDefault[0].nodeID + "-" + faceDefault[1].nodeID + ")");

		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
	}

	private static ActionNode NewNode(int id, string bird, Vector3 scale, Rect anchor)
	{
		return new ActionNode
		{
			nodeID = id,
			type = NodeType.SetScale,
			enabled = true,
			text = string.Empty,
			objectType = 1,
			objectName = bird,
			secondaryText = string.Empty,
			customVec1 = scale,
			param = new string[0],
			nodesIn = new int[0],
			nodesOut = new int[0],
			nodesOutIndex = new int[0],
			editorRect = new Rect(anchor.x + 260f, anchor.y + 260f, anchor.width, anchor.height)
		};
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

	private static void InsertAfter(List<ActionNode> nodes, ActionNode after, ActionNode[] added)
	{
		var oldOut = after.nodesOut ?? new int[0];
		var oldOutIndex = after.nodesOutIndex ?? new int[0];
		var previous = after;
		foreach (var node in added)
		{
			Link(previous, node);
			previous = node;
		}
		previous.nodesOut = oldOut;
		previous.nodesOutIndex = oldOutIndex;
		foreach (var id in oldOut)
		{
			var target = nodes.First(n => n.nodeID == id);
			target.nodesIn = (target.nodesIn ?? new int[0]).Select(x => x == after.nodeID ? previous.nodeID : x).ToArray();
		}
		nodes.AddRange(added);
	}

	private static void InsertBefore(List<ActionNode> nodes, ActionNode before, ActionNode[] added)
	{
		foreach (var from in nodes.Where(n => n.nodesOut != null && n.nodesOut.Contains(before.nodeID)))
		{
			from.nodesOut = from.nodesOut.Select(x => x == before.nodeID ? added[0].nodeID : x).ToArray();
		}
		added[0].nodesIn = before.nodesIn ?? new int[0];
		for (var i = 1; i < added.Length; i++)
		{
			Link(added[i - 1], added[i]);
		}
		Link(added[added.Length - 1], before);
		nodes.AddRange(added);
	}

	private static void Link(ActionNode from, ActionNode to)
	{
		from.nodesOut = new[] { to.nodeID };
		from.nodesOutIndex = new[] { 0 };
		to.nodesIn = new[] { from.nodeID };
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
