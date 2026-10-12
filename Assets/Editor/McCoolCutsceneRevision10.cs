using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tenth revision of the McCool robbery cutscene: Prince Porky keeps facing the Lolly Popper (FaceTarget) from when he
// notices him running past, then faces left (towards the stall and the hidden rogue) as he gives his order. Porky is
// a pig rig: scale +1 faces left. Menu item, or Temp/McCoolCutsceneRevision10.request.
public static class McCoolCutsceneRevision10
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision10.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision10.log";

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (10)")]
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
		Debug.Log("[McCoolCutsceneRevision10]\n" + s_Log);
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
		if (order.Any(n => n.type == NodeType.FaceTarget && n.objectName == "PrincePorky"))
		{
			Log("Porky already tracks the Lolly Popper, unchanged");
			return;
		}
		var notice = order.FirstOrDefault(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "PrincePorky" && n.text == "Surprised");
		var orderAnim = order.FirstOrDefault(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "PrincePorky" && n.text == "Action_Support");
		if (notice == null || orderAnim == null)
		{
			Log("Porky's Surprised or Action_Support node not found, unchanged");
			return;
		}
		var nextId = nodes.Max(n => n.nodeID) + 1;
		var watch = NewNode(nextId++, NodeType.FaceTarget, notice.editorRect, n => { n.objectType = 1; n.objectName = "PrincePorky"; n.secondaryText = "LollyPopper"; n.customBool = false; });
		InsertBefore(nodes, notice, watch);
		var stop = NewNode(nextId++, NodeType.FaceTarget, orderAnim.editorRect, n => { n.objectType = 1; n.objectName = "PrincePorky"; n.secondaryText = string.Empty; n.customBool = false; });
		var faceLeft = NewNode(nextId++, NodeType.SetScale, orderAnim.editorRect, n => { n.objectType = 1; n.objectName = "PrincePorky"; n.customVec1 = Vector3.one; });
		InsertBefore(nodes, orderAnim, stop, faceLeft);
		Log("Porky watches the Lolly Popper from before node " + notice.nodeID + " (node " + watch.nodeID + "), stops and faces left before his order, node " + orderAnim.nodeID + " (nodes " + stop.nodeID + "-" + faceLeft.nodeID + ")");
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

	private static void InsertBefore(List<ActionNode> nodes, ActionNode before, params ActionNode[] added)
	{
		foreach (var from in nodes.Where(n => n.nodesOut != null && n.nodesOut.Contains(before.nodeID)))
		{
			from.nodesOut = from.nodesOut.Select(x => x == before.nodeID ? added[0].nodeID : x).ToArray();
		}
		added[0].nodesIn = before.nodesIn ?? new int[0];
		for (var i = 1; i < added.Length; i++)
		{
			added[i - 1].nodesOut = new[] { added[i].nodeID };
			added[i - 1].nodesOutIndex = new[] { 0 };
			added[i].nodesIn = new[] { added[i - 1].nodeID };
		}
		var last = added[added.Length - 1];
		last.nodesOut = new[] { before.nodeID };
		last.nodesOutIndex = new[] { 0 };
		before.nodesIn = new[] { last.nodeID };
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
			editorRect = new Rect(anchor.x, anchor.y - 260f, anchor.width, anchor.height)
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
