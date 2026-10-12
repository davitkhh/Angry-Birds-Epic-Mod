using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Seventh revision of the McCool robbery cutscene: instead of running up the slope path, the rogue runs from the
// stall front to the foot of the ledge (Marker_RogueLedgeBase, just west of the tree stump resource node's x) and
// jumps up onto the ledge and McCool's bag in one arc. Removes revision 6's slope legs and Marker_RoguePath_* markers.
// Menu item, or touch Temp/McCoolCutsceneRevision7.request.
public static class McCoolCutsceneRevision7
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision7.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision7.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const string LedgeMarker = "WorldMap/2_Scenery/Zone_02/3_Hotspots/Hotspot_008_03_ResourceNode";

	// How far west of the ledge the rogue takes off, and how high the arc goes above the higher end.
	private const float TakeOffGap = 20f;

	private const float ArcHeight = 70f;

	private const float RunSpeed = 900f;

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (7)")]
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
		Debug.Log("[McCoolCutsceneRevision7]\n" + s_Log);
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
		var approach = sequence.Find("Marker_RogueApproach");
		var apex = sequence.Find("Marker_RogueJumpApex");
		var mcCoolSpot = sequence.Find("Marker_McCool");
		var pathMarks = Enumerable.Range(1, 9).Select(i => sequence.Find("Marker_RoguePath_" + i)).Where(m => m != null).ToList();
		var legs = order.Where(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && pathMarks.Contains(n.refObject2 as Transform)).ToList();
		var rise = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.refObject2 == apex);
		var fall = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.refObject1 == apex && n.refObject2 == mcCoolSpot);
		if (legs.Count == 0 || rise == null || fall == null)
		{
			Log("rogue's slope legs or jump not found (already jumping onto the ledge?), unchanged");
			return;
		}

		// Take off at the foot of the ledge, level with McCool.
		var ledgeX = Find(scene, LedgeMarker).position.x;
		var landing = mcCoolSpot.position + fall.customVec2;
		var baseMarker = sequence.Find("Marker_RogueLedgeBase");
		if (baseMarker == null)
		{
			baseMarker = new GameObject("Marker_RogueLedgeBase").transform;
			baseMarker.SetParent(sequence, false);
		}
		baseMarker.position = new Vector3(ledgeX - TakeOffGap, landing.y - 15f, approach.position.z);

		// One fast run from the stall front to the take-off point, replacing the slope legs.
		var run = legs[0];
		run.refObject1 = approach;
		run.refObject2 = baseMarker;
		run.customFloat = RunSpeed;
		foreach (var leg in legs.Skip(1))
		{
			Remove(nodes, leg);
		}
		rise.refObject1 = baseMarker;
		apex.position = (baseMarker.position + landing) * 0.5f + Vector3.up * ArcHeight;
		foreach (var mark in pathMarks)
		{
			UnityEngine.Object.DestroyImmediate(mark.gameObject);
		}
		Log("rogue runs " + approach.position + " -> Marker_RogueLedgeBase " + baseMarker.position + " at " + RunSpeed + " (node " + run.nodeID + "), removed nodes " + string.Join(", ", legs.Skip(1).Select(l => l.nodeID.ToString()).ToArray()) + "; jumps up via " + apex.position + " onto " + landing + " (ledge at x " + ledgeX + ")");

		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
		EditorUtility.SetDirty(sequence);
	}

	// Unlinks a single-in/single-out node and joins its neighbours.
	private static void Remove(List<ActionNode> nodes, ActionNode node)
	{
		var into = nodes.Where(n => n.nodesOut != null && n.nodesOut.Contains(node.nodeID)).ToList();
		var outOf = node.nodesOut ?? new int[0];
		foreach (var from in into)
		{
			from.nodesOut = from.nodesOut.SelectMany(x => x == node.nodeID ? outOf : new[] { x }).ToArray();
			from.nodesOutIndex = new int[from.nodesOut.Length];
		}
		foreach (var id in outOf)
		{
			var to = nodes.First(n => n.nodeID == id);
			to.nodesIn = (to.nodesIn ?? new int[0]).SelectMany(x => x == node.nodeID ? into.Select(f => f.nodeID).ToArray() : new[] { x }).ToArray();
		}
		nodes.Remove(node);
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
