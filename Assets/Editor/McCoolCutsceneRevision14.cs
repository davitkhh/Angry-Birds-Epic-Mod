using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Fourteenth revision of the McCool robbery cutscene: the rogue walks from the bag onto hotspot 008 at his path speed.
// PathMovingService walks to a path's first hotspot at a fifth of the path speed, so the MoveAlongPath alone made him
// crawl there. Like the Lolly Popper (node 68), he now loops Move_Loop and MoveTos from Marker_McCool (where his lunge
// lands) onto Hotspot_008_Battleground at the MoveAlongPath's speed first. Menu item, or
// Temp/McCoolCutsceneRevision14.request.
public static class McCoolCutsceneRevision14
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision14.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision14.log";

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (14)")]
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
		}
		catch (Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[McCoolCutsceneRevision14]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static bool ReviseInternal(Scene scene)
	{
		var sequence = Find(scene, SequencePath);
		var tree = sequence.GetComponent<ActionTree>();
		var order = ChainOrder(tree);
		var nodes = tree.nodes.ToList();
		var landing = sequence.Find("Marker_McCool");
		var getaway = order.FirstOrDefault(n => n.type == NodeType.MoveAlongPath && n.objectName == "Rogue");
		if (landing == null || getaway == null)
		{
			Log("Marker_McCool or the rogue's MoveAlongPath not found; unchanged");
			return false;
		}
		var firstHotspot = getaway.refObject1 as HotSpotWorldMapViewBase;
		if (firstHotspot == null)
		{
			Log("the rogue's MoveAlongPath has no start hotspot; unchanged");
			return false;
		}
		if (order.Any(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.refObject2 == firstHotspot.transform))
		{
			Log("the rogue already walks onto " + firstHotspot.name + ", unchanged");
			return false;
		}
		var nextId = nodes.Max(n => n.nodeID) + 1;
		var walk = NewNode(nextId++, NodeType.PlayBoneAnimation, getaway.editorRect, n =>
		{
			n.objectType = 1;
			n.objectName = "Rogue";
			n.text = "Move_Loop";
		});
		InsertBefore(nodes, getaway, walk);
		// Timing 1: customFloat is a speed. Wait until he is on the hotspot.
		var onto = NewNode(nextId++, NodeType.MoveTo, getaway.editorRect, n =>
		{
			n.objectType = 1;
			n.objectName = "Rogue";
			n.refObject1 = landing;
			n.refObject2 = firstHotspot.transform;
			n.customFloat = getaway.customFloat;
			n.customInt = 1;
			n.customInt2 = 1;
		});
		InsertBefore(nodes, getaway, onto);
		Log("rogue loops Move_Loop (node " + walk.nodeID + ") and walks " + landing.position + " -> " + firstHotspot.name + " " + firstHotspot.transform.position + " at " + getaway.customFloat + " (node " + onto.nodeID + ") before MoveAlongPath node " + getaway.nodeID);
		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
		return true;
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
