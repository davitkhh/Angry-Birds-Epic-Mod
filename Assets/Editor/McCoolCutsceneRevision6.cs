using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sixth revision of the McCool robbery cutscene: a ledge runs north-south at the x of the tree stump resource node,
// with Path_008_01 (hotspot_008 -> merchant) as the slope down it. McCool walks down that path to the stall, and the
// rogue comes up it from the stall front, jumping only the last stretch onto McCool's bag. Waypoints are sampled from
// the path's walk spline into Marker_McCoolPath_* / Marker_RoguePath_* (drag to adjust).
// Menu item, or touch Temp/McCoolCutsceneRevision6.request.
public static class McCoolCutsceneRevision6
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision6.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision6.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const string SlopePath = "WorldMap/2_Scenery/Zone_02/6_Paths/Path_008_01";

	private const int Waypoints = 3;

	// The rogue jumps from this far (along x/y) before McCool.
	private const float JumpDistance = 70f;

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (6)")]
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
		Debug.Log("[McCoolCutsceneRevision6]\n" + s_Log);
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
		var slope = Find(scene, SlopePath).GetComponent<RageSplineLite>();
		var mcCoolSpot = sequence.Find("Marker_McCool");
		var mcCoolT = NearestT(slope, mcCoolSpot.position);
		Log("Path_008_01 runs " + slope.GetPositionWorldSpace(0f) + " -> " + slope.GetPositionWorldSpace(1f) + "; McCool stands at t=" + mcCoolT.ToString("0.00"));

		// McCool: down the slope to the stall instead of straight there.
		var merchantPig = Find(scene, "WorldMap/2_Scenery/Zone_02/3_Hotspots/Hotspot_008_02_Merchant/MerchantPig");
		var mcCoolWalk = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "McCool" && n.refObject1 == mcCoolSpot && n.refObject2 == merchantPig);
		if (mcCoolWalk == null)
		{
			Log("McCool's walk to the stall not found (already on the path?), unchanged");
		}
		else
		{
			var marks = new List<Transform>();
			for (var i = 1; i <= Waypoints; i++)
			{
				var t = Mathf.Lerp(mcCoolT, 1f, i / (float)(Waypoints + 1));
				marks.Add(Marker(sequence, "Marker_McCoolPath_" + i, OnPath(slope, t, mcCoolSpot.position.z)));
			}
			var legs = Chain("McCool", mcCoolSpot, marks, merchantPig, mcCoolWalk, ref nextId);
			InsertAfter(nodes, mcCoolWalk, legs);
			Log("McCool walks the path: nodes " + mcCoolWalk.nodeID + ", " + string.Join(", ", legs.Select(l => l.nodeID.ToString()).ToArray()) + " via " + string.Join(" ", marks.Select(m => m.position.ToString()).ToArray()));
		}

		// Rogue: from the stall front up the slope, jumping only the last stretch onto the bag.
		var approach = sequence.Find("Marker_RogueApproach");
		var apex = sequence.Find("Marker_RogueJumpApex");
		var rise = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.refObject1 == approach && n.refObject2 == apex);
		var fall = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.refObject1 == apex && n.refObject2 == mcCoolSpot);
		var jumpAnim = rise == null ? null : order[order.IndexOf(rise) - 1];
		if (rise == null || fall == null || jumpAnim == null || jumpAnim.type != NodeType.PlayBoneAnimation)
		{
			Log("rogue's jump (anim + rise + fall) not found (already on the path?), unchanged");
		}
		else
		{
			var z = approach.position.z;
			// The stall-front end of the slope.
			approach.position = OnPath(slope, 1f, z);
			var jumpT = mcCoolT;
			while (jumpT < 1f && Vector2.Distance(slope.GetPositionWorldSpace(jumpT), mcCoolSpot.position) < JumpDistance)
			{
				jumpT += 0.01f;
			}
			var marks = new List<Transform>();
			for (var i = 1; i <= Waypoints; i++)
			{
				var t = Mathf.Lerp(1f, jumpT, i / (float)Waypoints);
				marks.Add(Marker(sequence, "Marker_RoguePath_" + i, OnPath(slope, t, z)));
			}
			// Walk up the slope (still in his walk cycle) before the jump animation.
			var speed = order.Where(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.customInt == (int)CHMotionTween.TimingTypes.UnitsPerSecond).Select(n => n.customFloat).DefaultIfEmpty(520f).First();
			var previous = approach;
			var walk = new List<ActionNode>();
			foreach (var mark in marks)
			{
				var from = previous;
				walk.Add(NewNode(nextId++, NodeType.MoveTo, jumpAnim.editorRect, n =>
				{
					n.objectType = 1;
					n.objectName = "Rogue";
					n.refObject1 = from;
					n.refObject2 = mark;
					n.customInt = (int)CHMotionTween.TimingTypes.UnitsPerSecond;
					n.customFloat = speed;
					n.customInt2 = 1;
				}));
				previous = mark;
			}
			InsertBefore(nodes, jumpAnim, walk.ToArray());
			rise.refObject1 = marks[marks.Count - 1];
			apex.position = (marks[marks.Count - 1].position + mcCoolSpot.position + fall.customVec2) * 0.5f + Vector3.up * 60f;
			Log("rogue walks up the slope: nodes " + string.Join(", ", walk.Select(w => w.nodeID.ToString()).ToArray()) + " from " + approach.position + " to " + marks[marks.Count - 1].position + ", then jumps via " + apex.position);
		}

		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
		EditorUtility.SetDirty(sequence);
	}

	// McCool: first leg reuses his existing walk node, the rest are copies of it through the markers.
	private static ActionNode[] Chain(string objectName, Transform start, List<Transform> marks, Transform end, ActionNode first, ref int nextId)
	{
		first.refObject2 = marks[0];
		var result = new List<ActionNode>();
		for (var i = 0; i < marks.Count; i++)
		{
			var from = marks[i];
			var to = i + 1 < marks.Count ? marks[i + 1] : end;
			var speed = first.customFloat;
			result.Add(NewNode(nextId++, NodeType.MoveTo, first.editorRect, n =>
			{
				n.objectType = 1;
				n.objectName = objectName;
				n.refObject1 = from;
				n.refObject2 = to;
				n.customInt = (int)CHMotionTween.TimingTypes.UnitsPerSecond;
				n.customFloat = speed;
				n.customInt2 = 1;
			}));
		}
		return result.ToArray();
	}

	private static float NearestT(RageSplineLite spline, Vector3 position)
	{
		var best = 0f;
		var bestDistance = float.MaxValue;
		for (var i = 0; i <= 400; i++)
		{
			var t = i / 400f;
			var distance = Vector2.Distance(spline.GetPositionWorldSpace(t), position);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = t;
			}
		}
		return best;
	}

	private static Vector3 OnPath(RageSplineLite spline, float t, float z)
	{
		var p = spline.GetPositionWorldSpace(t);
		return new Vector3(p.x, p.y, z);
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
