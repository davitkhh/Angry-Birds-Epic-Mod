using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Ninth revision of the McCool robbery cutscene: after his last step to the stall McCool turns around to face the way
// the stall's MerchantPig faces, pauses briefly, then hands over to it. Menu item, or Temp/McCoolCutsceneRevision9.request.
public static class McCoolCutsceneRevision9
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision9.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision9.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const float TurnPause = 0.25f;

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (9)")]
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
		Debug.Log("[McCoolCutsceneRevision9]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static void ReviseInternal(Scene scene)
	{
		var tree = Find(scene, SequencePath).GetComponent<ActionTree>();
		var nodes = tree.nodes.ToList();
		var merchantPig = Find(scene, "WorldMap/2_Scenery/Zone_02/3_Hotspots/Hotspot_008_02_Merchant/MerchantPig");
		var arrive = nodes.FirstOrDefault(n => n.type == NodeType.MoveTo && n.objectName == "McCool" && n.refObject2 == merchantPig);
		if (arrive == null)
		{
			Log("McCool's last step to the stall not found, unchanged");
			return;
		}
		var next = arrive.nodesOut != null && arrive.nodesOut.Length == 1 ? tree.GetNodeByID(arrive.nodesOut[0]) : null;
		if (next != null && next.type == NodeType.SetScale && next.objectName == "McCool")
		{
			Log("McCool already turns on arrival (node " + next.nodeID + ")");
			return;
		}
		// The stall pig's x scale is negative; McCool's container faces the same way at -1.
		var facing = new Vector3(Mathf.Sign(merchantPig.localScale.x), 1f, 1f);
		var nextId = nodes.Max(n => n.nodeID) + 1;
		var turn = NewNode(nextId++, NodeType.SetScale, arrive.editorRect, n => { n.objectType = 1; n.objectName = "McCool"; n.customVec1 = facing; });
		var idle = NewNode(nextId++, NodeType.PlayBoneAnimation, arrive.editorRect, n => { n.objectType = 1; n.objectName = "McCool"; n.text = "Idle"; });
		var pause = NewNode(nextId++, NodeType.Delay, arrive.editorRect, n => n.customFloat = TurnPause);
		InsertAfter(nodes, arrive, turn, idle, pause);
		Log("McCool turns to " + facing + " on arrival (nodes " + turn.nodeID + "-" + pause.nodeID + " after node " + arrive.nodeID + "), " + TurnPause + " s before the hand-over");
		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
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
