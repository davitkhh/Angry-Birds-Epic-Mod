using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tenth pass on the intro battle's raid (BS_00_PigsRaidCamp_AfterLoss), after IntroBattleCutsceneRevision9. The raid
// (everything after Red gets up) is rebuilt from one timeline of timed events, merged into a chain with delays:
// - Red's rogue (MinionPigB) runs in first (MinionPigA, which ran past Red, takes its old slot); Red no longer looks
//   back. The fight keeps its beats; the old mid-fight fade moves to the end.
// - Chuck is carried off: the bird catcher (MinionPigC, no main hand now) reaches him, steps behind him, turns right and
//   gives a support cheer; a new minion guard (MinionPigG, no main hand) comes in on his right. Chuck drops his hand
//   items (DropHandItems), is lifted, and both pigs carry him off right with Move_Carry at CarrySpeed.
// - A new bare pig (MinionPigN) comes in from the left, picks up Chuck's items (Affected) and runs off right.
// - After the fight Red is surprised, runs to the middle, waits, turns left and is surprised again under a
//   Bubble_StolenEggs bubble.
// The new pigs are battle MinionPig rigs under their own containers in 3_Midground, kept offscreen until they spawn;
// their entry points and times keep them clear of the swarm's other runs. One-shot: refuses once MinionPigG exists.
// Menu item, or Temp/IntroBattleCutsceneRevision10.request.
public static class IntroBattleCutsceneRevision10
{
	private const string ScenePath = "Assets/Scenes/3_Battlegrounds/1_Scenery/1_Beach/Battleground_BirdCamp_01.unity";

	private const string RequestFile = "Temp/IntroBattleCutsceneRevision10.request";

	private const string LogFile = "Temp/IntroBattleCutsceneRevision10.log";

	private const string SequencesPath = "Battleground_BirdCamp_01/2_StorySequences";

	private const string MidgroundPath = "Battleground_BirdCamp_01/1_Scenery/2_Landscape/3_Midground/";

	private const string BattleMinionPrefab = "Assets/Resources/Prefabs/4_Characters/8_Base/MinionPig.prefab";

	private const string BubbleSource = "Assets/Resources/Prefabs/1_WorldMap/1_Interface/Elements/Bubble_MissingFeature.prefab";

	private const string BubblePath = "Assets/Resources/Prefabs/0_Generic/1_Interface/Hud_Elements/Bubble_StolenEggs.prefab";

	private const float SwarmSpeed = 343f;

	// World map rogues walk at 500 and carry at 400 (Move_Carry); the same ratio at the swarm's pace.
	private const float CarrySpeed = SwarmSpeed * 400f / 500f;

	// Chuck's knocked out rig slides 90 rig units (about 60 world units) left of his battle spot.
	private const float ChuckBodyX = -60f;

	private static readonly Vector3 StickRunEnd = new Vector3(-929.3036f, 0f, 91.38677f);

	private static readonly Vector3 RogueRunEnd = new Vector3(-851.5499f, 0f, 386.5454f);

	// Red's run from his spot to the middle of the battlefield (about 300 units) takes this long, so the beats after it
	// land on time.
	private const float RedRunTime = 0.9f;

	private static readonly StringBuilder s_Log = new StringBuilder();

	private static Quaternion s_Plane;

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

	[MenuItem("Tools/Battle/Revise Intro Battle Cutscenes (10)")]
	public static void Revise()
	{
		s_Log.Length = 0;
		try
		{
			var scene = SceneManager.GetSceneByPath(ScenePath);
			var opened = false;
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
				opened = true;
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
			if (opened && !scene.isDirty)
			{
				EditorSceneManager.CloseScene(scene, true);
			}
		}
		catch (Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[IntroBattleCutsceneRevision10]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private class TimedNode
	{
		public float Time;

		public ActionNode Node;
	}

	private static bool ReviseInternal(Scene scene)
	{
		var sequences = Find(scene, SequencesPath);
		var lost = sequences.Find("BS_00_PigsRaidCamp_AfterLoss").GetComponent<ActionTree>();
		var midground = Find(scene, "Battleground_BirdCamp_01/1_Scenery/2_Landscape/3_Midground");
		if (midground.Find("MinionPigG") != null)
		{
			Log("already revised, unchanged");
			return false;
		}
		var a = midground.Find("MinionPigA").gameObject;
		var aRig = midground.Find("MinionPigA/Animation/MinionPigA").gameObject;
		var eRig = midground.Find("MinionPigA/Animation2/MinionPigE").gameObject;
		var b = midground.Find("MinionPigB").gameObject;
		var bRig = midground.Find("MinionPigB/Animation/MinionPigB").gameObject;
		var c = midground.Find("MinionPigC").gameObject;
		var cRig = midground.Find("MinionPigC/Animation/MinionPigC").gameObject;
		var d = midground.Find("MinionPigD").gameObject;
		var dRig = midground.Find("MinionPigD/Animation/MinionPigD").gameObject;
		s_Plane = c.transform.rotation;
		var middle = Find(scene, "BattlegroundSetup");

		var nodes = Ordered(lost);
		var getUp = nodes.FindIndex(n => n.type == NodeType.PlayBoneAnimation && n.objectName == "RedBird" && n.text == "KnockedOutEnd");
		var hits = nodes.Where(n => n.type == NodeType.Instantiate && n.objectName.StartsWith("Hit")).ToDictionary(n => n.objectName);
		var lunge = nodes.FirstOrDefault(n => n.type == NodeType.MoveArc && n.refObject == b);
		var zoomAt = nodes.FindLastIndex(n => n.type == NodeType.ZoomCamera);
		var fade = nodes.FirstOrDefault(n => n.type == NodeType.PlayAnimation && n.text == "ScreenEffect_FadeBlack");
		var storyEnd = nodes.LastOrDefault(n => n.type == NodeType.EnableStorySequence);
		var hideWiz = nodes.FirstOrDefault(n => n.type == NodeType.SetActive && n.objectName == "Wiz");
		if (getUp < 0 || hits.Count != 6 || lunge == null || zoomAt < 1 || nodes[zoomAt - 1].type != NodeType.MoveTo || fade == null || storyEnd == null || hideWiz == null)
		{
			Log("tree is not the expected one, unchanged");
			return false;
		}

		var bubble = MakeBubble();
		var gRig = AddPig(midground, "MinionPigG", b, bRig, new[] { Gear("Headgear", "Headgear_PigGuard"), Gear("Headgear_Back", "Headgear_PigGuard_Back"), Gear("MainHand", string.Empty), Gear("OffHand", "OffHand_PigGuard") });
		var nRig = AddPig(midground, "MinionPigN", b, bRig, new[] { Gear("Headgear", string.Empty), Gear("Headgear_Back", string.Empty), Gear("MainHand", string.Empty), Gear("OffHand", string.Empty) });
		var guard = gRig.transform.parent.parent.gameObject;
		var bare = nRig.transform.parent.parent.gameObject;
		var catcherGear = cRig.GetComponent<SceneRigGear>();
		foreach (var swap in catcherGear.m_Swaps.Where(s => s.BoneName == "MainHand"))
		{
			swap.TextureName = string.Empty;
		}
		EditorUtility.SetDirty(catcherGear);
		Log("MinionPigC: main hand hidden");

		var events = new List<TimedNode>();
		Action<float, ActionNode> at = (time, node) => events.Add(new TimedNode { Time = time, Node = node });

		// Red's rogue runs in first (MinionPigA's old start) and fights Red, with the old fight's beats from its start.
		at(0f, Anchor(b, "RedBird", new Vector3(300f, 0f, 15f) - RogueRunEnd));
		at(0f, Clip(b, "MinionPigB_Move"));
		at(0f, Bone(bRig, "Move_Loop", null, true));
		at(0f, BoneRt("RedBird", "Focus_MainHand", "Idle"));
		at(1f, Clone(hideWiz));
		at(1.58f, Bone(bRig, "Action_Attack_2", "Idle", false));
		at(1.87f, Clone(lunge));
		at(2.08f, Clone(hits["Hit0"]));
		at(2.08f, BoneRt("RedBird", "Hit", "Idle"));
		at(2.58f, BoneRt("RedBird", "Action_Attack_1", "Idle"));
		at(2.88f, Clone(hits["Hit1"]));
		at(2.88f, Bone(bRig, "Hit", "Idle", false));
		at(4.13f, Bone(bRig, "Action_Attack_2", "Idle", false));
		at(4.63f, Clone(hits["Hit2"]));
		at(4.63f, BoneRt("RedBird", "Hit", "Idle"));
		at(5.68f, BoneRt("RedBird", "Action_Attack_1", "Idle"));
		at(5.98f, Clone(hits["Hit3"]));
		at(5.98f, Bone(bRig, "Hit", "Idle", false));
		at(5.98f, Clone(nodes[zoomAt - 1]));
		at(5.98f, Clone(nodes[zoomAt]));
		at(6.78f, Bone(bRig, "Action_Attack_1", "Idle", false));
		at(6.98f, Clone(hits["Hit5"]));
		at(6.98f, BoneRt("RedBird", "Hit", "Idle"));
		at(7.68f, BoneRt("RedBird", "Action_Attack_1", "Idle"));
		at(7.88f, Clone(hits["Hit4"]));
		at(7.88f, Bone(bRig, "Defeated", null, false));

		// The swarm's other runs keep their times; MinionPigA (with MinionPigE) takes the rogue's old start.
		at(0.75f, Bone(dRig, "Move_Loop", null, true));
		at(0.75f, Clip(d, "MinionPigD_Move"));
		at(3.15f, Bone(dRig, "Action_Fail", "Move_Loop", false));
		at(3.15f, Bone(aRig, "Move_Loop", null, true));
		at(3.15f, Clip(a, "MinionPigA_Move"));
		at(3.15f, Bone(eRig, "Move_Loop", null, true));

		// The bird catcher's run (2.5 s, its rig in the container's Animation child) now ends just behind and right of
		// Chuck; it carries on behind him to his left, turns right and cheers.
		at(3.15f, Bone(cRig, "Move_Loop", null, true));
		at(3.15f, Anchor(c, "YellowBird", new Vector3(70f + ChuckBodyX, 0f, 90f) - StickRunEnd));
		at(3.15f, Clip(c, "MinionPigC_Move"));
		at(5.65f, MoveTo(c, "YellowBird", OnPlane(new Vector3(-95f + ChuckBodyX, 0f, 0f) - StickRunEnd), SwarmSpeed, "Move_Loop", true));
		at(6.45f, Face(c, true));
		at(6.5f, Bone(cRig, "Action_Support", "Idle", false));

		// The guard comes in from the front right, below the bird catcher's run, to Chuck's right (about 2.7 s).
		at(3.65f, Anchor(guard, "YellowBird", new Vector3(1000f + ChuckBodyX, 0f, -250f)));
		at(3.65f, MoveTo(guard, "YellowBird", OnPlane(new Vector3(95f + ChuckBodyX, 0f, -5f)), SwarmSpeed, "Move_Loop", true));

		// Chuck drops his staff and book, is lifted and carried off right.
		at(7.3f, Drop("YellowBird", "MainHand,OffHand", "ChuckItem_"));
		at(7.3f, MoveRt("YellowBird", null, new Vector3(0f, 45f, 0f), 300f, null, 0));
		at(7.5f, MoveTo(c, null, new Vector3(1600f, 0f, 0f), CarrySpeed, "Move_Carry", false));
		at(7.5f, MoveTo(guard, null, new Vector3(1600f, 0f, 0f), CarrySpeed, "Move_Carry", false));
		at(7.5f, MoveRt("YellowBird", null, new Vector3(1600f, 0f, 0f), CarrySpeed, null, 0));

		// The bare pig comes in from the front left once the swarm has passed (about 3.8 s), picks up Chuck's items
		// and runs off right.
		at(7.55f, Anchor(bare, "YellowBird", new Vector3(-1300f + ChuckBodyX, 0f, -60f)));
		at(7.55f, MoveTo(bare, "YellowBird", OnPlane(new Vector3(-25f + ChuckBodyX, 0f, -20f)), SwarmSpeed, "Move_Loop", true));
		at(11.35f, Bone(nRig, "Affected", "Idle", false));
		at(11.6f, Carry("ChuckItem_MainHand", FindChild(nRig.transform, "MainHand")));
		at(11.6f, Carry("ChuckItem_OffHand", FindChild(nRig.transform, "OffHand")));
		at(11.95f, MoveTo(bare, null, new Vector3(1800f, 0f, 0f), SwarmSpeed, "Move_Loop", true));

		// Red: surprised, runs to the middle, stops, turns left and finds the eggs gone.
		at(8.5f, BoneRt("RedBird", "Surprised", "Idle"));
		at(9.4f, MoveRt("RedBird", middle, Vector3.zero, SwarmSpeed, "Move_Loop", 2, true, RedRunTime));
		at(9.4f + RedRunTime + 0.5f, FaceRt("RedBird", false));
		at(9.4f + RedRunTime + 0.8f, BoneRt("RedBird", "Surprised", "Idle"));
		at(9.4f + RedRunTime + 0.8f, Bubble("RedBird", bubble));

		at(13.4f, Clone(fade));
		at(13.4f + 4.9f, Clone(storyEnd));

		var chain = nodes.Take(getUp + 1).ToList();
		var now = 0f;
		foreach (var timed in events.Select((e, i) => new { e, i }).OrderBy(x => x.e.Time).ThenBy(x => x.i).Select(x => x.e))
		{
			if (timed.Time > now + 0.001f)
			{
				chain.Add(Delay(timed.Time - now));
				now = timed.Time;
			}
			if (timed.Node.type == NodeType.PlayBoneAnimation || timed.Node.type == NodeType.MoveArc || timed.Node.type == NodeType.MoveToObject)
			{
				timed.Node.customInt = 0;
			}
			chain.Add(timed.Node);
		}
		Finish(lost, chain);
		Log("BS_00_PigsRaidCamp_AfterLoss: raid rebuilt from " + events.Count + " timed events (" + chain.Count + " nodes), carry speed " + CarrySpeed);
		return true;
	}

	private static Vector3 OnPlane(Vector3 offset)
	{
		return s_Plane * offset;
	}

	private static GameObject MakeBubble()
	{
		var existing = AssetDatabase.LoadAssetAtPath<GameObject>(BubblePath);
		if (existing != null)
		{
			return existing;
		}
		var contents = PrefabUtility.LoadPrefabContents(BubbleSource);
		contents.name = "Bubble_StolenEggs";
		var icon = contents.transform.Find("Animation/Icon").GetComponent<UISprite>();
		icon.spriteName = "BirdEgg";
		icon.width = 72;
		icon.height = 72;
		var saved = PrefabUtility.SaveAsPrefabAsset(contents, BubblePath);
		PrefabUtility.UnloadPrefabContents(contents);
		Log("created " + BubblePath + " (missing-item bubble with a bird egg)");
		return saved;
	}

	// A battle MinionPig rig under a new container (container/Animation/rig, like the swarm's) in the midground,
	// parked offscreen until its spawn.
	private static GameObject AddPig(Transform midground, string name, GameObject like, GameObject likeRig, SceneRigGear.Swap[] gear)
	{
		var container = new GameObject(name);
		container.transform.SetParent(midground, false);
		container.transform.localRotation = like.transform.localRotation;
		container.transform.localScale = like.transform.localScale;
		container.transform.localPosition = new Vector3(-4000f, like.transform.localPosition.y, like.transform.localPosition.z);
		var animation = new GameObject("Animation");
		animation.transform.SetParent(container.transform, false);
		var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattleMinionPrefab);
		var rig = (GameObject)UnityEngine.Object.Instantiate(prefab, animation.transform);
		rig.name = name;
		rig.transform.localPosition = Vector3.zero;
		rig.transform.localRotation = likeRig.transform.localRotation;
		rig.transform.localScale = likeRig.transform.localScale;
		UnityHelper.SetLayerRecusively(container, likeRig.layer);
		var swaps = rig.AddComponent<SceneRigGear>();
		swaps.m_Swaps = gear.ToList();
		EditorUtility.SetDirty(swaps);
		Log(name + ": battle MinionPig with " + string.Join(", ", gear.Select(x => x.TextureName == string.Empty ? "no " + x.BoneName : x.TextureName).ToArray()));
		return rig;
	}

	private static GameObject FindChild(Transform root, string name)
	{
		return root.GetComponentsInChildren<Transform>(true).First(t => t.name == name).gameObject;
	}

	private static SceneRigGear.Swap Gear(string bone, string texture)
	{
		return new SceneRigGear.Swap { BoneName = bone, TextureName = texture };
	}

	// Puts a container where it is planeOffset (battle plane) from a saved object.
	private static ActionNode Anchor(GameObject container, string runtimeName, Vector3 planeOffset)
	{
		var node = NewNode(NodeType.SetPosition);
		node.refObject = container;
		node.customInt = 3;
		node.text = runtimeName;
		node.customVec2 = OnPlane(planeOffset);
		return node;
	}

	private static ActionNode Clip(GameObject container, string clip)
	{
		var node = NewNode(NodeType.PlayAnimation);
		node.refObject = container;
		node.text = clip;
		return node;
	}

	private static ActionNode Bone(GameObject rig, string clip, string queued, bool randomStart)
	{
		var node = NewNode(NodeType.PlayBoneAnimation);
		node.refObject = rig;
		node.text = clip;
		node.QueueIdle = queued != null;
		node.secondaryText = queued ?? string.Empty;
		node.customBool2 = randomStart;
		return node;
	}

	private static ActionNode BoneRt(string runtimeName, string clip, string queued)
	{
		var node = Bone(null, clip, queued, false);
		node.objectType = 1;
		node.objectName = runtimeName;
		return node;
	}

	// Moves a scene pig's container to a saved object (or, without one, from where it is) plus a world offset; its rig
	// turns the way it goes.
	private static ActionNode MoveTo(GameObject container, string runtimeName, Vector3 offset, float speed, string clip, bool randomStart)
	{
		var node = NewNode(NodeType.MoveToObject);
		node.refObject = container;
		node.secondaryText = runtimeName ?? string.Empty;
		node.customVec1 = offset;
		node.customFloat = speed;
		node.text = clip ?? string.Empty;
		node.customBool2 = randomStart;
		node.customInt2 = 1;
		return node;
	}

	// Moves a saved character to target (or from where it is) plus a world offset; face 2 turns it the way it goes
	// (birds). xOnly: only along x. duration > 0: takes that long instead of going at speed.
	private static ActionNode MoveRt(string runtimeName, Transform target, Vector3 offset, float speed, string clip, int face, bool xOnly = false, float duration = 0f)
	{
		var node = NewNode(NodeType.MoveToObject);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.refObject1 = target;
		node.customVec1 = offset;
		node.customFloat = speed;
		node.customFloat2 = duration;
		node.text = clip ?? string.Empty;
		node.customInt2 = face;
		node.customBool = xOnly;
		return node;
	}

	private static ActionNode Face(GameObject container, bool right)
	{
		var node = NewNode(NodeType.FaceDirection);
		node.refObject = container;
		node.customInt = right ? 1 : 0;
		node.customInt2 = 1;
		return node;
	}

	private static ActionNode FaceRt(string runtimeName, bool right)
	{
		var node = NewNode(NodeType.FaceDirection);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.customInt = right ? 1 : 0;
		node.customBool = true;
		return node;
	}

	private static ActionNode Drop(string runtimeName, string bones, string prefix)
	{
		var node = NewNode(NodeType.DropHandItems);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.text = bones;
		node.secondaryText = prefix;
		node.customBool = true;
		return node;
	}

	private static ActionNode Carry(string runtimeName, GameObject hand)
	{
		var node = NewNode(NodeType.SetParent);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.customInt = 0;
		node.refObject2 = hand;
		node.QueueIdle = true;
		return node;
	}

	private static ActionNode Bubble(string runtimeName, GameObject prefab)
	{
		var node = NewNode(NodeType.ShowBubble);
		node.objectType = 1;
		node.objectName = runtimeName;
		node.refObject = prefab;
		node.customFloat = 1.25f;
		return node;
	}

	private static ActionNode Delay(float seconds)
	{
		var node = NewNode(NodeType.Delay);
		node.customFloat = seconds;
		return node;
	}

	private static ActionNode NewNode(NodeType type)
	{
		return new ActionNode
		{
			type = type,
			enabled = true,
			text = string.Empty,
			objectName = string.Empty,
			secondaryText = string.Empty,
			param = new string[0],
			nodesIn = new int[0],
			nodesOut = new int[0],
			nodesOutIndex = new int[0]
		};
	}

	private static ActionNode Clone(ActionNode source)
	{
		var copy = new ActionNode();
		foreach (var field in typeof(ActionNode).GetFields(BindingFlags.Public | BindingFlags.Instance))
		{
			var value = field.GetValue(source);
			var array = value as Array;
			field.SetValue(copy, array != null ? array.Clone() : value);
		}
		return copy;
	}

	private static List<ActionNode> Ordered(ActionTree tree)
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

	// Numbers the nodes 1..n in order, lays them out in rows of ten and links them into one chain.
	private static void Finish(ActionTree tree, List<ActionNode> chain)
	{
		for (var i = 0; i < chain.Count; i++)
		{
			chain[i].nodeID = i + 1;
			chain[i].editorRect = new Rect(20f + 230f * (i % 10), 20f + 220f * (i / 10), 200f, 180f);
		}
		for (var i = 0; i < chain.Count; i++)
		{
			chain[i].nodesIn = i > 0 ? new[] { chain[i - 1].nodeID } : new int[0];
			chain[i].nodesOut = i + 1 < chain.Count ? new[] { chain[i + 1].nodeID } : new int[0];
			chain[i].nodesOutIndex = i + 1 < chain.Count ? new[] { 0 } : new int[0];
		}
		tree.nodes = chain.ToArray();
		tree.startNode = 1;
		EditorUtility.SetDirty(tree);
	}

	private static Transform Find(Scene scene, string path)
	{
		var parts = path.Split('/');
		foreach (var root in scene.GetRootGameObjects())
		{
			if (root.name == parts[0])
			{
				var found = parts.Length == 1 ? root.transform : root.transform.Find(string.Join("/", parts.Skip(1).ToArray()));
				if (found != null)
				{
					return found;
				}
			}
		}
		throw new Exception("not found in " + scene.name + ": " + path);
	}
}
