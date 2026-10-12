using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Second revision of the McCool robbery cutscene: the Lolly Popper walks the map paths to the first castle, the rogue
// starts his walk cycle after his rig has set up, and the rogue jumps (arc through Marker_RogueJumpApex) onto McCool's
// bag instead of walking into it. Nodes are found by what they do, so hand edits survive.
// Menu item, or touch Temp/McCoolCutsceneRevision2.request with the editor open.
public static class McCoolCutsceneRevision2
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision2.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision2.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const string Hotspots = "WorldMap/2_Scenery/Zone_02/3_Hotspots/";

	private const string JumpClip = "Action_RageJump";

	private const float JumpHeight = 60f;

	private const float JumpHalfSeconds = 0.25f;

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (2)")]
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
		Debug.Log("[McCoolCutsceneRevision2]\n" + s_Log);
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

		// The Lolly Popper walks the map paths (008 -> ... -> first castle) instead of running straight there.
		var castle = Find(scene, Hotspots + "Hotspot_014_Castle");
		var hotspot008 = Find(scene, Hotspots + "Hotspot_008_Battleground");
		var lollyRun = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "LollyPopper" && n.refObject2 == castle);
		if (lollyRun != null)
		{
			lollyRun.type = NodeType.MoveAlongPath;
			lollyRun.refObject1 = hotspot008.GetComponent<HotSpotWorldMapViewBase>();
			lollyRun.refObject2 = castle.GetComponent<HotSpotWorldMapViewBase>();
			lollyRun.customVec1 = Vector3.zero;
			lollyRun.customVec2 = Vector3.zero;
			lollyRun.text = "Move_Loop";
			lollyRun.customFloat = 500f;
			lollyRun.customBool = true;
			lollyRun.customInt = 0;
			lollyRun.customInt2 = 0;
			Log("node " + lollyRun.nodeID + ": Lolly Popper walks the paths from hotspot_008 to the first castle");
		}
		else
		{
			Log("Lolly Popper's run to the castle not found (already a path walk?), unchanged");
		}

		// A freshly spawned character's rig resets to Idle as it sets up, swallowing a walk cycle started the same frame.
		var rogueSpawn = order.FirstOrDefault(n => n.type == NodeType.InstantiateCharacter && n.objectName == "Rogue");
		var rogueWalk = rogueSpawn == null ? null : order.Skip(order.IndexOf(rogueSpawn)).FirstOrDefault(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "Rogue" && n.text == "Move_Loop");
		if (rogueWalk != null)
		{
			var previous = order[order.IndexOf(rogueWalk) - 1];
			if (previous.type == NodeType.Delay)
			{
				Log("rogue walk cycle already delayed");
			}
			else
			{
				var delay = NewNode(nextId++, NodeType.Delay, rogueWalk.editorRect, 0, n => n.customFloat = 0.1f);
				InsertBefore(nodes, rogueWalk, delay);
				Log("node " + delay.nodeID + ": 0.1 s delay before the rogue's walk cycle (node " + rogueWalk.nodeID + ")");
			}
		}

		// The rogue jumps onto McCool's bag: jump clip plus an arc through Marker_RogueJumpApex (drag it to reshape).
		var approach = sequence.Find("Marker_RogueApproach");
		var mcCoolSpot = sequence.Find("Marker_McCool");
		var dash = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.refObject1 == approach && n.refObject2 == mcCoolSpot);
		if (dash != null)
		{
			var landing = mcCoolSpot.position + dash.customVec2;
			var apex = sequence.Find("Marker_RogueJumpApex");
			if (apex == null)
			{
				apex = new GameObject("Marker_RogueJumpApex").transform;
				apex.SetParent(sequence, false);
				apex.position = (approach.position + landing) * 0.5f + Vector3.up * JumpHeight;
			}
			var jump = NewNode(nextId++, NodeType.PlayBoneAnimation, dash.editorRect, 0, n => { n.objectType = 1; n.objectName = "Rogue"; n.text = JumpClip; });
			InsertBefore(nodes, dash, jump);
			// Rising half of the arc.
			dash.refObject2 = apex;
			var landingOffset = dash.customVec2;
			dash.customVec2 = Vector3.zero;
			dash.customInt = (int)CHMotionTween.TimingTypes.Duration;
			dash.customFloat = JumpHalfSeconds;
			dash.customInt2 = 1;
			// Falling half, onto the bag.
			var fall = NewNode(nextId++, NodeType.MoveTo, dash.editorRect, 1, n =>
			{
				n.objectType = 1;
				n.objectName = "Rogue";
				n.refObject1 = apex;
				n.refObject2 = mcCoolSpot;
				n.customVec2 = landingOffset;
				n.customInt = (int)CHMotionTween.TimingTypes.Duration;
				n.customFloat = JumpHalfSeconds;
				n.customInt2 = 1;
			});
			InsertAfter(nodes, dash, fall);
			Log("rogue jumps: " + JumpClip + " (node " + jump.nodeID + "), arc up to Marker_RogueJumpApex " + apex.position + " (node " + dash.nodeID + ") and down onto the bag (node " + fall.nodeID + ")");
		}
		else
		{
			Log("rogue dash into McCool not found (already a jump?), unchanged");
		}

		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
		EditorUtility.SetDirty(sequence);
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
			editorRect = new Rect(anchor.x + offset * 260f, anchor.y + 260f, anchor.width, anchor.height)
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
