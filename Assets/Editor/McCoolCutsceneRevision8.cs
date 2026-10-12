using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Eighth revision of the McCool robbery cutscene: McCool faces the way he walks on each leg of his path to the stall
// (left down the path's dip, right as it curls up into the stall front). McCool is a bird model: scale +1 faces right.
// Re-run after dragging the Marker_McCoolPath_* markers to refresh the facings. Menu item, or Temp/McCoolCutsceneRevision8.request.
public static class McCoolCutsceneRevision8
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision8.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision8.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (8)")]
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
		Debug.Log("[McCoolCutsceneRevision8]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static void ReviseInternal(Scene scene)
	{
		var sequence = Find(scene, SequencePath);
		var tree = sequence.GetComponent<ActionTree>();
		var order = ChainOrder(tree);
		var nodes = tree.nodes.ToList();
		var nextId = nodes.Max(n => n.nodeID) + 1;
		var pathMarks = Enumerable.Range(1, 9).Select(i => sequence.Find("Marker_McCoolPath_" + i)).Where(m => m != null).ToList();
		var start = order.FindIndex(n => n.type == NodeType.MoveTo && n.objectName == "McCool" && pathMarks.Contains(n.refObject2 as Transform));
		if (start < 0)
		{
			Log("McCool's path legs not found, unchanged");
			return;
		}
		var legs = new List<ActionNode>();
		for (var i = start; i < order.Count && (order[i].objectName == "McCool" && (order[i].type == NodeType.MoveTo || order[i].type == NodeType.SetScale)); i++)
		{
			if (order[i].type == NodeType.MoveTo)
			{
				legs.Add(order[i]);
			}
		}
		foreach (var leg in legs)
		{
			var from = (leg.refObject1 as Transform).position + leg.customVec1;
			var to = (leg.refObject2 as Transform).position + leg.customVec2;
			var facing = to.x >= from.x ? Vector3.one : new Vector3(-1f, 1f, 1f);
			var index = order.IndexOf(leg);
			var previous = order[index - 1];
			if (previous.type == NodeType.SetScale && previous.objectName == "McCool")
			{
				previous.customVec1 = facing;
				Log("node " + previous.nodeID + " sets McCool " + facing + " for leg " + leg.nodeID);
			}
			else
			{
				var turn = new ActionNode
				{
					nodeID = nextId++,
					type = NodeType.SetScale,
					enabled = true,
					text = string.Empty,
					objectType = 1,
					objectName = "McCool",
					secondaryText = string.Empty,
					customVec1 = facing,
					param = new string[0],
					nodesIn = new int[0],
					nodesOut = new int[0],
					nodesOutIndex = new int[0],
					editorRect = new Rect(leg.editorRect.x, leg.editorRect.y - 260f, leg.editorRect.width, leg.editorRect.height)
				};
				InsertBefore(nodes, leg, turn);
				Log("node " + turn.nodeID + " sets McCool " + facing + " before leg " + leg.nodeID + " (" + from.x.ToString("0") + " -> " + to.x.ToString("0") + ")");
			}
		}
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
