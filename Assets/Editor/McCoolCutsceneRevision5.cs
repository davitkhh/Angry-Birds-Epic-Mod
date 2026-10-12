using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Fifth revision of the McCool robbery cutscene: Red and Yellow keep facing the rogue (FaceTarget) from when they notice
// him, keep facing McCool once he turns to the shop, and stop tracking at the cleanup (where they face right again).
// Replaces revision 4's fixed facing nodes. Menu item, or touch Temp/McCoolCutsceneRevision5.request.
public static class McCoolCutsceneRevision5
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision5.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision5.log";

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (5)")]
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
		Debug.Log("[McCoolCutsceneRevision5]\n" + s_Log);
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
		if (order.Any(n => n.type == NodeType.FaceTarget))
		{
			Log("the birds already track their targets, unchanged");
			return;
		}
		// Revision 4's facing nodes, in playback order: two at the rogue sighting, two at McCool's turn, two at cleanup.
		var birdScales = order.Where(n => n.type == NodeType.SetScale && Birds.Contains(n.objectName)).ToList();
		if (birdScales.Count < 6)
		{
			Log("expected 6 bird SetScale nodes (revision 4), found " + birdScales.Count + "; unchanged");
			return;
		}
		Track(birdScales.Take(2), "Rogue");
		Track(birdScales.Skip(2).Take(2), "McCool");
		var nextId = nodes.Max(n => n.nodeID) + 1;
		var cleanup = birdScales[4];
		var stops = Birds.Select(b => new ActionNode
		{
			nodeID = nextId++,
			type = NodeType.FaceTarget,
			enabled = true,
			text = string.Empty,
			objectType = 1,
			objectName = b,
			secondaryText = string.Empty,
			customBool = true,
			param = new string[0],
			nodesIn = new int[0],
			nodesOut = new int[0],
			nodesOutIndex = new int[0],
			editorRect = new Rect(cleanup.editorRect.x + 260f, cleanup.editorRect.y - 260f, cleanup.editorRect.width, cleanup.editorRect.height)
		}).ToArray();
		InsertBefore(nodes, cleanup, stops);
		Log("birds track the rogue (nodes " + birdScales[0].nodeID + ", " + birdScales[1].nodeID + "), then McCool (nodes " + birdScales[2].nodeID + ", " + birdScales[3].nodeID + "), stop before node " + cleanup.nodeID + " (nodes " + stops[0].nodeID + "-" + stops[1].nodeID + ")");
		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
	}

	private static void Track(IEnumerable<ActionNode> facingNodes, string target)
	{
		foreach (var node in facingNodes)
		{
			node.type = NodeType.FaceTarget;
			node.secondaryText = target;
			node.customBool = true;
			node.customVec1 = Vector3.zero;
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
