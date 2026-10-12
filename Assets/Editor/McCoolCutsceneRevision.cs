using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Revision of the McCool robbery cutscene (WS_03_McCoolShopOpen_Sequence): McCool's facing, McCool level with the
// Lolly Popper, the rogue hiding behind the merchant stall and walking the map paths to the first friendship gate,
// and Porky's state taunt loop paused for the cutscene. Nodes are found by what they do, so hand edits survive.
// Menu item, or touch Temp/McCoolCutsceneRevision.request with the editor open.
public static class McCoolCutsceneRevision
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const string PorkyStatePath = "WorldMap/0_StorySequences/WS_03_YellowRescued_State";

	private const string Hotspots = "WorldMap/2_Scenery/Zone_02/3_Hotspots/";

	private const string GatePath = "WorldMap/2_Scenery/Zone_03/3_Hotspots/Hotspot_024_01_FriendshipGate";

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene")]
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
		Debug.Log("[McCoolCutsceneRevision]\n" + s_Log);
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

		// McCool's rig faces the other way from the pig rigs: face the Lolly Popper first, then the stall (the same
		// way the stall's MerchantPig faces, so there is no turn when he hands over).
		var mcCoolScales = order.Where(n => n.type == NodeType.SetScale && n.objectName == "McCool").ToList();
		if (mcCoolScales.Count >= 2)
		{
			mcCoolScales[0].customVec1 = Vector3.one;
			mcCoolScales[1].customVec1 = new Vector3(-1f, 1f, 1f);
			Log("McCool faces (1,1,1) at the start and (-1,1,1) towards the shop (nodes " + mcCoolScales[0].nodeID + ", " + mcCoolScales[1].nodeID + ")");
		}
		else
		{
			Log("McCool SetScale nodes not found, facing unchanged");
		}

		// McCool level with the Lolly Popper instead of in the birds' party slots.
		var mcCoolSpot = sequence.Find("Marker_McCool");
		var lollySpot = sequence.Find("Marker_LollyPopper");
		mcCoolSpot.position = new Vector3(mcCoolSpot.position.x, lollySpot.position.y, lollySpot.position.z);
		Log("Marker_McCool -> " + mcCoolSpot.position);

		// The rogue hides behind the merchant stall (further from the camera) and pops out to its right.
		var stallBody = Find(scene, Hotspots + "Hotspot_008_02_Merchant/MerchantStall/Body");
		var hideout = sequence.Find("Marker_RogueHideout");
		var approach = sequence.Find("Marker_RogueApproach");
		hideout.position = new Vector3(stallBody.position.x, stallBody.position.y - 20f, stallBody.position.z + 6f);
		approach.position = new Vector3(stallBody.position.x + 85f, stallBody.position.y - 50f, -40f);
		Log("Marker_RogueHideout -> " + hideout.position + ", Marker_RogueApproach -> " + approach.position);

		// After the grab he walks the map paths (008 -> ... -> first friendship gate) instead of running straight there.
		var gate = Find(scene, GatePath);
		var hotspot008 = Find(scene, Hotspots + "Hotspot_008_Battleground");
		var run = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.refObject2 == gate);
		if (run != null)
		{
			run.type = NodeType.MoveAlongPath;
			run.refObject1 = hotspot008.GetComponent<HotSpotWorldMapViewBase>();
			run.refObject2 = gate.GetComponent<HotSpotWorldMapViewBase>();
			run.customVec1 = Vector3.zero;
			run.customVec2 = Vector3.zero;
			run.text = "Move_Loop";
			run.customFloat = 500f;
			run.customBool = true;
			run.customInt = 0;
			run.customInt2 = 0;
			Log("node " + run.nodeID + ": rogue walks the paths from hotspot_008 to the first friendship gate");
		}
		else if (order.Any(n => n.type == NodeType.MoveAlongPath && n.objectName == "Rogue"))
		{
			Log("rogue already walks the paths");
		}
		else
		{
			Log("rogue run to the gate not found, unchanged");
		}

		// Porky's state tree loops his Taunt (tongue sounds) while he stands at 009: pause it for the cutscene.
		if (!nodes.Any(n => n.type == NodeType.ControlActionTree))
		{
			var porkyState = Find(scene, PorkyStatePath).GetComponent<ActionTree>();
			var loopStart = porkyState.nodes.FirstOrDefault(n => n.type == NodeType.Delay && n.nodesOut != null && n.nodesOut.Any(id =>
			{
				var next = porkyState.GetNodeByID(id);
				return next != null && next.type == NodeType.PlayBoneAnimation && next.text == "Taunt";
			}));
			var findPorky = order.FirstOrDefault(n => n.type == NodeType.FindSceneObject && n.objectName == "PrincePorky");
			var end = order.LastOrDefault(n => n.type == NodeType.EnableStorySequence && !n.customBool);
			if (loopStart == null || findPorky == null || end == null)
			{
				Log("Porky's state loop, FindSceneObject PrincePorky or the closing EnableStorySequence not found; taunts unchanged");
			}
			else
			{
				var nextId = nodes.Max(n => n.nodeID) + 1;
				var stop = NewNode(nextId++, NodeType.ControlActionTree, findPorky.editorRect, 1, n => { n.refObject = porkyState; n.customBool = false; });
				var idle = NewNode(nextId++, NodeType.PlayBoneAnimation, findPorky.editorRect, 2, n => { n.objectType = 1; n.objectName = "PrincePorky"; n.text = "Idle"; });
				InsertAfter(nodes, findPorky, stop, idle);
				var resume = NewNode(nextId++, NodeType.ControlActionTree, end.editorRect, 1, n => { n.refObject = porkyState; n.customBool = true; n.customInt = loopStart.nodeID; });
				InsertBefore(nodes, end, resume);
				Log("Porky's taunt loop stops after node " + findPorky.nodeID + " (nodes " + stop.nodeID + "-" + idle.nodeID + ") and resumes at its node " + loopStart.nodeID + " before node " + end.nodeID + " (node " + resume.nodeID + ")");
			}
		}
		else
		{
			Log("Porky's taunt loop is already controlled");
		}

		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
		EditorUtility.SetDirty(sequence);
	}

	// Nodes in playback order from the start node, following the first out link.
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
		Link(after, added[0]);
		for (var i = 1; i < added.Length; i++)
		{
			Link(added[i - 1], added[i]);
		}
		var last = added[added.Length - 1];
		last.nodesOut = oldOut;
		last.nodesOutIndex = oldOutIndex;
		foreach (var id in oldOut)
		{
			var target = nodes.First(n => n.nodeID == id);
			target.nodesIn = (target.nodesIn ?? new int[0]).Select(x => x == after.nodeID ? last.nodeID : x).ToArray();
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

	private static ActionNode NewNode(int id, NodeType type, Rect anchor, int offset, Action<ActionNode> init)
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
			editorRect = new Rect(anchor.x + offset * 260f, anchor.y - 260f, anchor.width, anchor.height)
		};
		init(node);
		return node;
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
