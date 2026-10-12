using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Third revision of the McCool robbery cutscene: the Lolly Popper runs straight onto the path start before walking
// the paths (the path walker's own approach is slow), Porky's goal bubble from his story state is hidden during the
// cutscene, and the rogue sneaks out around the left side of the merchant stall instead of through it.
// Nodes are found by what they do, so hand edits survive. Menu item, or Temp/McCoolCutsceneRevision3.request.
public static class McCoolCutsceneRevision3
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision3.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision3.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const string Hotspots = "WorldMap/2_Scenery/Zone_02/3_Hotspots/";

	private const string PorkyBubble = "Bubble_Goal(Clone)";

	// Clearance around the stall's body for the rogue's route.
	private const float StallMargin = 20f;

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (3)")]
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
		Debug.Log("[McCoolCutsceneRevision3]\n" + s_Log);
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
		var hotspot008 = Find(scene, Hotspots + "Hotspot_008_Battleground");

		// 1. The path walker approaches its first hotspot at a fifth of its path speed; run the Lolly Popper onto it first.
		var lollyWalk = order.FirstOrDefault(n => n.type == NodeType.MoveAlongPath && n.objectName == "LollyPopper");
		var lollySpot = sequence.Find("Marker_LollyPopper");
		if (lollyWalk != null)
		{
			var previous = order[order.IndexOf(lollyWalk) - 1];
			if (previous.type == NodeType.MoveTo && previous.objectName == "LollyPopper")
			{
				Log("Lolly Popper already runs onto the path start");
			}
			else
			{
				var run = NewNode(nextId++, NodeType.MoveTo, lollyWalk.editorRect, n =>
				{
					n.objectType = 1;
					n.objectName = "LollyPopper";
					n.refObject1 = lollySpot;
					n.refObject2 = hotspot008;
					n.customInt = (int)CHMotionTween.TimingTypes.UnitsPerSecond;
					n.customFloat = 450f;
					n.customInt2 = 1;
				});
				InsertBefore(nodes, lollyWalk, run);
				Log("node " + run.nodeID + ": Lolly Popper runs onto hotspot_008 before walking the paths (node " + lollyWalk.nodeID + ")");
			}
		}

		// 2. Porky's goal bubble belongs to his story state (paused during the cutscene); hide it until the state resumes.
		var porkyIdle = order.FirstOrDefault(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "PrincePorky" && n.text == "Idle");
		var resume = order.FirstOrDefault(n => n.type == NodeType.ControlActionTree && n.customBool);
		if (order.Any(n => n.type == NodeType.FindObject && n.text == PorkyBubble))
		{
			Log("Porky's goal bubble is already hidden");
		}
		else if (porkyIdle == null || resume == null)
		{
			Log("Porky's Idle node or the state resume node not found; bubble unchanged");
		}
		else
		{
			var find = NewNode(nextId++, NodeType.FindObject, porkyIdle.editorRect, n => { n.objectType = 1; n.objectName = "PrincePorky"; n.text = PorkyBubble; n.secondaryText = "PorkyGoalBubble"; });
			var hide = NewNode(nextId++, NodeType.SetActive, porkyIdle.editorRect, n => { n.objectType = 1; n.objectName = "PorkyGoalBubble"; n.customBool = false; });
			InsertAfter(nodes, porkyIdle, find, hide);
			var show = NewNode(nextId++, NodeType.SetActive, resume.editorRect, n => { n.objectType = 1; n.objectName = "PorkyGoalBubble"; n.customBool = true; });
			InsertBefore(nodes, resume, show);
			Log("Porky's goal bubble hidden after node " + porkyIdle.nodeID + " (nodes " + find.nodeID + "-" + hide.nodeID + "), shown again before node " + resume.nodeID + " (node " + show.nodeID + ")");
		}

		// 3. The rogue sneaks around the left side of the stall: behind it to its left edge, down past its front, then right.
		var hideout = sequence.Find("Marker_RogueHideout");
		var approach = sequence.Find("Marker_RogueApproach");
		var popOut = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.refObject1 == hideout && n.refObject2 == approach);
		if (popOut == null)
		{
			Log("rogue's hideout -> approach move not found (already rerouted?), unchanged");
		}
		else
		{
			var stall = Find(scene, Hotspots + "Hotspot_008_02_Merchant/MerchantStall/Body").GetComponent<Renderer>().bounds;
			var left = stall.min.x - StallMargin;
			var front = stall.min.y - StallMargin;
			var behindLeft = Marker(sequence, "Marker_RogueStallLeft", new Vector3(left, hideout.position.y, hideout.position.z));
			var frontLeft = Marker(sequence, "Marker_RogueStallFrontLeft", new Vector3(left, front, approach.position.z));
			approach.position = new Vector3(approach.position.x, front, approach.position.z);
			var apex = sequence.Find("Marker_RogueJumpApex");
			var mcCoolSpot = sequence.Find("Marker_McCool");
			var fall = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.refObject1 == apex && n.refObject2 == mcCoolSpot);
			if (apex != null && fall != null)
			{
				apex.position = (approach.position + mcCoolSpot.position + fall.customVec2) * 0.5f + Vector3.up * 60f;
			}
			Log("stall body x " + stall.min.x + ".." + stall.max.x + ", y " + stall.min.y + ".." + stall.max.y + "; route " + hideout.position + " -> " + behindLeft.position + " -> " + frontLeft.position + " -> " + approach.position);

			// Pig rigs face left by default: left while sneaking out, right once heading for McCool.
			var rogueScale = order.FirstOrDefault(n => n.type == NodeType.SetScale && n.objectName == "Rogue");
			if (rogueScale != null)
			{
				rogueScale.customVec1 = Vector3.one;
			}
			var speed = popOut.customFloat;
			popOut.refObject2 = behindLeft;
			var down = NewNode(nextId++, NodeType.MoveTo, popOut.editorRect, n =>
			{
				n.objectType = 1;
				n.objectName = "Rogue";
				n.refObject1 = behindLeft;
				n.refObject2 = frontLeft;
				n.customInt = (int)CHMotionTween.TimingTypes.UnitsPerSecond;
				n.customFloat = speed;
				n.customInt2 = 1;
			});
			var turn = NewNode(nextId++, NodeType.SetScale, popOut.editorRect, n => { n.objectType = 1; n.objectName = "Rogue"; n.customVec1 = new Vector3(-1f, 1f, 1f); });
			var across = NewNode(nextId++, NodeType.MoveTo, popOut.editorRect, n =>
			{
				n.objectType = 1;
				n.objectName = "Rogue";
				n.refObject1 = frontLeft;
				n.refObject2 = approach;
				n.customInt = (int)CHMotionTween.TimingTypes.UnitsPerSecond;
				n.customFloat = speed;
				n.customInt2 = 1;
			});
			InsertAfter(nodes, popOut, down, turn, across);
			Log("rogue route: node " + popOut.nodeID + " to Marker_RogueStallLeft, " + down.nodeID + " to Marker_RogueStallFrontLeft, " + turn.nodeID + " turns right, " + across.nodeID + " to Marker_RogueApproach");
		}

		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
		EditorUtility.SetDirty(sequence);
	}

	private static Transform Marker(Transform sequence, string markerName, Vector3 position)
	{
		var marker = sequence.Find(markerName);
		if (marker == null)
		{
			marker = new GameObject(markerName).transform;
			marker.SetParent(sequence, false);
		}
		marker.position = position;
		return marker;
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

	private static void InsertAfter(List<ActionNode> nodes, ActionNode after, params ActionNode[] added)
	{
		var oldOut = after.nodesOut ?? new int[0];
		var oldOutIndex = after.nodesOutIndex ?? new int[0];
		var previous = after;
		foreach (var node in added)
		{
			previous.nodesOut = new[] { node.nodeID };
			previous.nodesOutIndex = new[] { 0 };
			node.nodesIn = new[] { previous.nodeID };
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
