using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Twelfth revision of the McCool robbery cutscene: the rogue lunges at McCool's bag like his battle attack instead of
// the high Action_RageJump. Battle melee plays Action_Attack_2 (the club's animation index) and dashes to the target
// at the clip's "Movement" trigger (frame 7 of 18 at 24 fps, 0.29 s) in PacingBalancing's 0.2 s, so the "Impact"
// frame (0.5 s) lands as he arrives. The dash is one MovePath node (points editable by hand in its param list) arcing
// over the ledge from Marker_RogueLedgeBase onto Marker_McCool; the two MoveTo legs through Marker_RogueJumpApex are
// removed. The rogue finishes the clip holding the bag before he runs off. Menu item, or
// Temp/McCoolCutsceneRevision12.request.
public static class McCoolCutsceneRevision12
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision12.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision12.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const string AttackClip = "Action_Attack_2";

	// Action_Attack_2: Movement trigger at frame 7, 18 frames, 24 fps.
	private const float WindUp = 7f / 24f;

	private const float DashTime = 0.2f;

	private const float ClipLength = 18f / 24f;

	// How far the arc rises above the higher of take-off and landing, enough to clear the ledge top.
	private const float ArcHeight = 60f;

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (12)")]
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
		Debug.Log("[McCoolCutsceneRevision12]\n" + s_Log);
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
		if (order.Any(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "Rogue" && n.text == AttackClip))
		{
			Log("the rogue already lunges with " + AttackClip + ", unchanged");
			return false;
		}
		var baseMarker = sequence.Find("Marker_RogueLedgeBase");
		var apex = sequence.Find("Marker_RogueJumpApex");
		var landing = sequence.Find("Marker_McCool");
		var jump = order.FirstOrDefault(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "Rogue" && n.text == "Action_RageJump");
		var up = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.refObject1 == baseMarker && n.refObject2 == apex);
		var down = order.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "Rogue" && n.refObject1 == apex && n.refObject2 == landing);
		var grab = order.FirstOrDefault(n => n.type == NodeType.SetHandItem && n.objectName == "Rogue" && n.customBool);
		if (jump == null || up == null || down == null || grab == null || baseMarker == null || landing == null)
		{
			Log("rogue's jump, jump legs, bag grab or markers not found; unchanged");
			return false;
		}
		var nextId = nodes.Max(n => n.nodeID) + 1;
		jump.text = AttackClip;
		var windUp = NewNode(nextId++, NodeType.Delay, jump.editorRect, n => n.customFloat = WindUp);
		var start = baseMarker.position;
		var end = landing.position;
		var top = Mathf.Max(start.y, end.y) + ArcHeight;
		var points = new[]
		{
			start,
			new Vector3(Mathf.Lerp(start.x, end.x, 0.25f), Mathf.Lerp(start.y, top, 0.75f), Mathf.Lerp(start.z, end.z, 0.25f)),
			new Vector3(Mathf.Lerp(start.x, end.x, 0.55f), top, Mathf.Lerp(start.z, end.z, 0.55f)),
			new Vector3(Mathf.Lerp(start.x, end.x, 0.85f), Mathf.Lerp(end.y, top, 0.5f), Mathf.Lerp(start.z, end.z, 0.85f)),
			end
		};
		var lunge = NewNode(nextId++, NodeType.MovePath, up.editorRect, n =>
		{
			n.objectType = 1;
			n.objectName = "Rogue";
			n.param = points.Select(Format).ToArray();
			n.customFloat2 = DashTime;
			n.customInt = 1;
		});
		// jump -> wind-up -> lunge -> whatever followed the landing leg.
		var after = down.nodesOut ?? new int[0];
		jump.nodesOut = new[] { windUp.nodeID };
		jump.nodesOutIndex = new[] { 0 };
		windUp.nodesIn = new[] { jump.nodeID };
		windUp.nodesOut = new[] { lunge.nodeID };
		windUp.nodesOutIndex = new[] { 0 };
		lunge.nodesIn = new[] { windUp.nodeID };
		lunge.nodesOut = after;
		lunge.nodesOutIndex = down.nodesOutIndex ?? new int[0];
		foreach (var id in after)
		{
			var target = nodes.First(n => n.nodeID == id);
			target.nodesIn = (target.nodesIn ?? new int[0]).Select(x => x == down.nodeID ? lunge.nodeID : x).ToArray();
		}
		nodes.Remove(up);
		nodes.Remove(down);
		nodes.Add(windUp);
		nodes.Add(lunge);
		// Let the attack clip play out with the bag in hand before he runs off.
		var recover = NewNode(nextId++, NodeType.Delay, grab.editorRect, n => n.customFloat = ClipLength - WindUp - DashTime);
		InsertAfter(nodes, grab, recover);
		Log("node " + jump.nodeID + " plays " + AttackClip + "; wind-up " + WindUp.ToString("0.###") + " s (node " + windUp.nodeID + "); lunge MovePath node " + lunge.nodeID + " in " + DashTime + " s through " + string.Join(" ", lunge.param) + "; removed jump legs " + up.nodeID + ", " + down.nodeID + "; recovery " + recover.customFloat.ToString("0.###") + " s after bag grab node " + grab.nodeID + " (node " + recover.nodeID + ")");
		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
		return true;
	}

	private static string Format(Vector3 v)
	{
		return v.x.ToString("0.#", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.#", CultureInfo.InvariantCulture) + "," + v.z.ToString("0.#", CultureInfo.InvariantCulture);
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
