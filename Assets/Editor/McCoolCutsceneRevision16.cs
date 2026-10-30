using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SmoothMoves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sixteenth revision of the McCool robbery cutscene: the robbery can be switched off in code
// (StoryToggles.PorkyStealsMcCoolBag). A Branch node after Porky stops watching the Lolly Popper skips from Porky's order
// to McCool turning to his shop (birds' FaceTarget McCool moved into the robbery part, just before that turn). A second
// Branch after the idea bubble gives the no-robbery run its own bird turn: 0.7 s after McCool turns, the birds face him
// (and the shop), then the rest of the 1.6 s idea wait. McCool keeps showing the bag, and the shop MerchantPig gets a
// StallHandItem so it holds the bag whenever the toggle is off. Menu item, Temp/McCoolCutsceneRevision16.request, or
// -executeMethod McCoolCutsceneRevision16.Revise in batch mode.
public static class McCoolCutsceneRevision16
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision16.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision16.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const string StallPigPath = "WorldMap/2_Scenery/Zone_02/3_Hotspots/Hotspot_008_02_Merchant/MerchantPig";

	private const float BirdsTurnDelay = 0.7f;

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

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (16)")]
	public static void Revise()
	{
		s_Log.Length = 0;
		try
		{
			var scene = SceneManager.GetSceneByPath(ScenePath);
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, Application.isBatchMode ? OpenSceneMode.Single : OpenSceneMode.Additive);
			}
			var hadUnsavedChanges = scene.isDirty;
			var changed = ReviseTree(scene);
			changed |= AddStallBag(scene);
			if (changed)
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
		Directory.CreateDirectory("Temp");
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[McCoolCutsceneRevision16]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static bool ReviseTree(Scene scene)
	{
		var sequence = Find(scene, SequencePath);
		var tree = sequence.GetComponent<ActionTree>();
		var nodes = tree.nodes.ToList();
		if (nodes.Any(n => n.type == NodeType.Branch))
		{
			Log("the cutscene already has Branch nodes, tree unchanged");
			return false;
		}
		var order = ChainOrder(tree);
		var order66 = order.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "PrincePorky" && n.text == "Action_Support");
		var mourn = order.FirstOrDefault(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "McCool" && n.text == "Mourn");
		if (order66 < 0 || mourn == null)
		{
			Log("Porky's order or McCool's Mourn not found; tree unchanged");
			return false;
		}
		// Porky stops tracking the Lolly Popper just before he turns to give his order.
		var stopTracking = order.Take(order66).LastOrDefault(n => n.type == NodeType.FaceTarget && n.objectName == "PrincePorky" && string.IsNullOrEmpty(n.secondaryText));
		var turn = order.SkipWhile(n => n != mourn).FirstOrDefault(n => n.type == NodeType.SetScale && n.objectName == "McCool");
		var turnIndex = order.IndexOf(turn);
		var birdsTrack = order.Skip(turnIndex + 1).TakeWhile(n => n.type == NodeType.FaceTarget).Where(n => n.secondaryText == "McCool").ToList();
		var bubble = order.Skip(turnIndex).FirstOrDefault(n => n.type == NodeType.Instantiate && n.objectName == "IdeaBubble");
		var ideaWait = bubble == null ? null : order.SkipWhile(n => n != bubble).Skip(1).FirstOrDefault();
		if (stopTracking == null || turn == null || birdsTrack.Count == 0 || bubble == null || ideaWait == null || ideaWait.type != NodeType.Delay)
		{
			Log("Porky's stop-tracking node, McCool's turn, the birds' tracking, the idea bubble or its wait not found; tree unchanged");
			return false;
		}
		var nextId = nodes.Max(n => n.nodeID) + 1;

		// The birds start tracking McCool at the end of the robbery part instead of after his turn (same frame).
		foreach (var bird in birdsTrack)
		{
			Unlink(nodes, bird);
		}
		foreach (var bird in birdsTrack)
		{
			InsertBefore(nodes, turn, bird);
		}

		var skipRobbery = NewNode(nextId++, NodeType.Branch, stopTracking.editorRect, n =>
		{
			n.secondaryText = StoryToggles.PorkyStealsMcCoolBagName;
			n.customInt = turn.nodeID;
		});
		InsertAfter(nodes, stopTracking, skipRobbery);

		// No robbery: 0.7 s after McCool turns, the birds turn to him (and the shop), then the rest of the idea wait.
		var calmChain = new List<ActionNode>();
		calmChain.Add(NewNode(nextId++, NodeType.Delay, ideaWait.editorRect, n => n.customFloat = BirdsTurnDelay));
		foreach (var bird in birdsTrack)
		{
			var copy = bird;
			calmChain.Add(NewNode(nextId++, NodeType.FaceTarget, ideaWait.editorRect, n =>
			{
				n.objectType = copy.objectType;
				n.objectName = copy.objectName;
				n.secondaryText = copy.secondaryText;
				n.customBool = copy.customBool;
			}));
		}
		calmChain.Add(NewNode(nextId++, NodeType.Delay, ideaWait.editorRect, n => n.customFloat = Mathf.Max(0f, ideaWait.customFloat - BirdsTurnDelay)));
		for (var i = 0; i < calmChain.Count; i++)
		{
			calmChain[i].editorRect = new Rect(ideaWait.editorRect.x + 260f * i, ideaWait.editorRect.y + 520f, ideaWait.editorRect.width, ideaWait.editorRect.height);
			calmChain[i].nodesIn = i == 0 ? new int[0] : new[] { calmChain[i - 1].nodeID };
			calmChain[i].nodesOut = i == calmChain.Count - 1 ? (ideaWait.nodesOut ?? new int[0]) : new[] { calmChain[i + 1].nodeID };
			calmChain[i].nodesOutIndex = calmChain[i].nodesOut.Select(x => 0).ToArray();
			nodes.Add(calmChain[i]);
		}
		foreach (var id in ideaWait.nodesOut ?? new int[0])
		{
			var target = nodes.First(n => n.nodeID == id);
			target.nodesIn = (target.nodesIn ?? new int[0]).Concat(new[] { calmChain[calmChain.Count - 1].nodeID }).ToArray();
		}
		var calmBranch = NewNode(nextId++, NodeType.Branch, bubble.editorRect, n =>
		{
			n.secondaryText = StoryToggles.PorkyStealsMcCoolBagName;
			n.customInt = calmChain[0].nodeID;
		});
		InsertAfter(nodes, bubble, calmBranch);

		Log("birds' McCool tracking (" + string.Join(", ", birdsTrack.Select(b => b.nodeID.ToString()).ToArray()) + ") moved before McCool's turn (node " + turn.nodeID + ")");
		Log("Branch node " + skipRobbery.nodeID + " after node " + stopTracking.nodeID + ": toggle off jumps to node " + turn.nodeID);
		Log("Branch node " + calmBranch.nodeID + " after idea bubble node " + bubble.nodeID + ": toggle off runs " + string.Join(" -> ", calmChain.Select(n => n.nodeID + " " + n.type).ToArray()) + " -> " + string.Join(",", (ideaWait.nodesOut ?? new int[0]).Select(x => x.ToString()).ToArray()));
		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
		return true;
	}

	private static bool AddStallBag(Scene scene)
	{
		var pig = Find(scene, StallPigPath);
		if (pig.GetComponent<StallHandItem>() != null)
		{
			Log("MerchantPig already has a StallHandItem");
			return false;
		}
		var item = pig.gameObject.AddComponent<StallHandItem>();
		item.m_BoneAnimation = pig.GetComponent<BoneAnimation>();
		EditorUtility.SetDirty(pig.gameObject);
		Log("MerchantPig: StallHandItem (" + item.m_TextureName + " on " + item.m_BoneName + ", hidden while " + item.m_HiddenWhileToggleOn + " is on), rig " + (item.m_BoneAnimation != null));
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

	// Takes a node out of the chain (its predecessors lead to its successors), keeping the node itself.
	private static void Unlink(List<ActionNode> nodes, ActionNode node)
	{
		var outs = node.nodesOut ?? new int[0];
		var ins = node.nodesIn ?? new int[0];
		foreach (var from in nodes.Where(n => n.nodesOut != null && n.nodesOut.Contains(node.nodeID)))
		{
			from.nodesOut = from.nodesOut.SelectMany(x => x == node.nodeID ? outs : new[] { x }).ToArray();
			from.nodesOutIndex = from.nodesOut.Select(x => 0).ToArray();
		}
		foreach (var to in nodes.Where(n => n.nodesIn != null && n.nodesIn.Contains(node.nodeID)))
		{
			to.nodesIn = to.nodesIn.SelectMany(x => x == node.nodeID ? ins : new[] { x }).ToArray();
		}
		node.nodesIn = new int[0];
		node.nodesOut = new int[0];
		node.nodesOutIndex = new int[0];
		nodes.Remove(node);
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
