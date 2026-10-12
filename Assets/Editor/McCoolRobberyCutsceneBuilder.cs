using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One-shot builder for the McCool robbery cutscene that plays after clearing hotspot_008 (WS_03_McCoolShopOpen_Sequence).
// After building, edit the cutscene by hand in the ActionTree editor; positions come from the Marker_* children
// of the sequence object (drag them in the scene). Refuses to rebuild once the tree has been built, so hand edits are kept.
// Run from the menu, or touch Temp/McCoolRobberyBuild.request with the editor open.
public static class McCoolRobberyCutsceneBuilder
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolRobberyBuild.request";

	private const string LogFile = "Temp/McCoolRobberyBuild.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const string Hotspots = "WorldMap/2_Scenery/Zone_02/3_Hotspots/";

	// CharacterControllerWorldMap prefab used by every InstantiateCharacter node in the world map.
	private const string CharacterPrefabGuid = "11291cc1e4da9144aaeee158e59275c2";

	private const string IdeaBubblePath = "Assets/Prefab/Bubble_Idea.prefab";

	private static readonly StringBuilder s_Log = new StringBuilder();

	private static List<ActionNode> s_Nodes;

	private static int s_Row;

	private static int s_Column;

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
		Build();
	}

	[MenuItem("Tools/World Map/Build McCool Robbery Cutscene")]
	public static void Build()
	{
		s_Log.Length = 0;
		try
		{
			BuildInternal();
		}
		catch (Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[McCoolRobberyCutsceneBuilder]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static void BuildInternal()
	{
		var scene = SceneManager.GetSceneByPath(ScenePath);
		if (!scene.isLoaded)
		{
			scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
		}
		var hadUnsavedChanges = scene.isDirty;

		var sequence = Find(scene, SequencePath);
		var tree = sequence.GetComponent<ActionTree>();
		if (tree.nodes != null && tree.nodes.Any(n => n.type == NodeType.SetHandItem))
		{
			Log("aborted: the cutscene is already built; edit it in the ActionTree editor");
			return;
		}

		var hotspot008 = Find(scene, Hotspots + "Hotspot_008_Battleground");
		var hotspot009 = Find(scene, Hotspots + "Hotspot_009_Battleground");
		var castle = Find(scene, Hotspots + "Hotspot_014_Castle");
		var gate = Find(scene, "WorldMap/2_Scenery/Zone_03/3_Hotspots/Hotspot_024_01_FriendshipGate");
		var merchant = Find(scene, Hotspots + "Hotspot_008_02_Merchant");
		var merchantPig = merchant.Find("MerchantPig");
		var cameraSystem = Find(scene, "WorldMap/1_CameraSystem");
		var cameraController = cameraSystem.Find("1_CameraController");
		var camera = cameraController.GetComponentInChildren<Camera>(true);
		var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(CharacterPrefabGuid));
		var ideaBubble = AssetDatabase.LoadAssetAtPath<GameObject>(IdeaBubblePath);
		if (characterPrefab == null || ideaBubble == null || camera == null || merchantPig == null)
		{
			Log("aborted: missing character prefab, idea bubble, camera or MerchantPig");
			return;
		}

		// Where everyone stands; drag these in the scene to restage the cutscene.
		var mcCoolSpot = Marker(sequence, "Marker_McCool", new Vector3(-1025f, -1520f, -35f));
		var lollySpot = Marker(sequence, "Marker_LollyPopper", new Vector3(-862f, -1492f, -37f));
		var rogueHideout = Marker(sequence, "Marker_RogueHideout", new Vector3(-1075f, -1800f, -45f));
		var rogueApproach = Marker(sequence, "Marker_RogueApproach", new Vector3(-1050f, -1650f, -40f));

		s_Nodes = new List<ActionNode>();
		s_Row = 0;
		s_Column = 0;

		// Setup: letterbox, camera, cast.
		Add(NodeType.EnableStorySequence, n => n.customBool = true);
		Add(NodeType.SetPosition, n =>
		{
			n.refObject = cameraController.gameObject;
			n.refObject2 = cameraSystem;
			n.customVec2 = new Vector3(-950f, -1420f, 0f);
		});
		Add(NodeType.ZoomCamera, n =>
		{
			n.refObject = camera;
			n.customFloat = -40f;
			n.customFloat2 = 1.5f;
		});
		Add(NodeType.GetWorldMapCharacter, n => { n.text = "RedBird"; n.objectName = "RedBird"; });
		Add(NodeType.GetWorldMapCharacter, n => { n.text = "YellowBird"; n.objectName = "YellowBird"; });
		// Spawned by WS_03_YellowRescued_State (active until hotspot_009 is beaten) as a child of hotspot 009.
		Add(NodeType.FindSceneObject, n => { n.refObject = hotspot009; n.text = "pig_prince_porky_firstencounter"; n.objectName = "PrincePorky"; });
		SpawnCharacter(characterPrefab, "bird_merchant_mcpig", "McCool", mcCoolSpot);
		Scale("McCool", new Vector3(-1f, 1f, 1f));
		SpawnCharacter(characterPrefab, "pig_minion_corporal_boss", "LollyPopper", lollySpot);
		Add(NodeType.Delay, n => n.customFloat = 0.1f);
		HandItem("McCool", true, string.Empty);

		// 1. Birds and McCool cheer, the Lolly Popper cries, Yellow-rescued fanfare.
		NextRow();
		Add(NodeType.PlaySound, n => n.text = "music_battle_win");
		Anim("RedBird", "Cheer", 0, true);
		Anim("YellowBird", "Cheer", 0, true);
		Anim("McCool", "Cheer", 0, true);
		Anim("LollyPopper", "Mourn", 0, false);
		Add(NodeType.Delay, n => n.customFloat = 2.5f);

		// 2. The Lolly Popper runs to the first castle; Porky notices and gets angry.
		NextRow();
		Anim("LollyPopper", "Move_Loop", 0, false);
		MoveTo("LollyPopper", lollySpot, Vector3.zero, castle, new Vector3(0f, -50f, 30f), 220f, false);
		Add(NodeType.Delay, n => n.customFloat = 0.4f);
		Anim("PrincePorky", "Surprised", 1, false);
		Anim("PrincePorky", "Rage", 1, true);

		// 3. Porky gives an order; a rogue dashes out of hiding. The birds notice him first.
		NextRow();
		Anim("PrincePorky", "Action_Support", 1, true);
		SpawnCharacter(characterPrefab, "pig_minion_rogue", "Rogue", rogueHideout);
		Scale("Rogue", new Vector3(-1f, 1f, 1f));
		Anim("Rogue", "Move_Loop", 0, false);
		MoveTo("Rogue", rogueHideout, Vector3.zero, rogueApproach, Vector3.zero, 520f, true);
		Anim("RedBird", "Surprised", 0, true);
		Anim("YellowBird", "Surprised", 0, true);
		MoveTo("Rogue", rogueApproach, Vector3.zero, mcCoolSpot, new Vector3(5f, 15f, -5f), 520f, true);

		// The rogue nabs the Phat Loot bag mid-dash and keeps running; McCool notices right after.
		NextRow();
		HandItem("McCool", false, string.Empty);
		HandItem("Rogue", true, "McCool");
		MoveTo("Rogue", mcCoolSpot, new Vector3(5f, 15f, -5f), gate, new Vector3(0f, -40f, -30f), 480f, false);
		Anim("McCool", "Surprised", 1, false);

		// 4. The rogue runs to the first friendship gate. McCool cries, the birds get angry, Porky laughs.
		NextRow();
		Anim("McCool", "Mourn", 0, false);
		Anim("RedBird", "Rage", 0, true);
		Anim("YellowBird", "Rage", 0, true);
		Anim("PrincePorky", "Taunt", 1, true);
		Add(NodeType.Delay, n => n.customFloat = 1f);

		// 5. McCool turns to the shop, gets an idea, and runs to his merchant spot.
		NextRow();
		Scale("McCool", Vector3.one);
		Anim("McCool", "Idle", 0, false);
		Add(NodeType.Instantiate, n =>
		{
			n.refObject = ideaBubble;
			n.objectType = 1;
			n.text = "McCool";
			n.objectName = "IdeaBubble";
			n.customVec1 = new Vector3(15f, 45f, -125f);
		});
		Add(NodeType.Delay, n => n.customFloat = 1.6f);
		Destroy("IdeaBubble");
		Anim("McCool", "Move_Loop", 0, false);
		MoveTo("McCool", mcCoolSpot, Vector3.zero, merchantPig, Vector3.zero, 160f, true);
		// Hand over to the stall's MerchantPig (hidden until now, see the merchant's activate objects).
		Add(NodeType.SetActive, n => { n.refObject = merchantPig.gameObject; n.customBool = true; });
		Destroy("McCool");

		// Cleanup.
		NextRow();
		Anim("RedBird", "Idle", 0, false);
		Anim("YellowBird", "Idle", 0, false);
		Destroy("Rogue");
		Destroy("LollyPopper");
		Add(NodeType.ZoomCamera, n =>
		{
			n.refObject = camera;
			n.customFloat = 40f;
			n.customFloat2 = 1f;
			n.customInt2 = (int)NodeWaitCondition.WaitUntilCompleted;
		});
		Add(NodeType.EnableStorySequence, n => n.customBool = false);

		tree.nodes = s_Nodes.ToArray();
		tree.startNode = 1;
		EditorUtility.SetDirty(tree);
		Log("built " + s_Nodes.Count + " nodes into " + SequencePath);

		// Play it when hotspot_008 is cleared, before its follow-ups (merchant, 009) unlock.
		var execute = hotspot008.GetComponent<ExecuteActionTree>();
		if (execute == null)
		{
			execute = hotspot008.gameObject.AddComponent<ExecuteActionTree>();
		}
		execute.m_ActionTree = tree;
		execute.m_executeBeforeUnlock = true;
		EditorUtility.SetDirty(execute);
		Log("hotspot_008 plays it on completion");

		// McCool only stands at the stall once the merchant unlocks (after this cutscene, or on load past 008).
		merchantPig.gameObject.SetActive(false);
		var shop = new SerializedObject(merchant.GetComponent<HotSpotWorldMapViewShopNode>());
		var activate = shop.FindProperty("m_activateObjects");
		var listed = Enumerable.Range(0, activate.arraySize).Any(i => activate.GetArrayElementAtIndex(i).objectReferenceValue == merchantPig.gameObject);
		if (!listed)
		{
			activate.InsertArrayElementAtIndex(activate.arraySize);
			activate.GetArrayElementAtIndex(activate.arraySize - 1).objectReferenceValue = merchantPig.gameObject;
		}
		shop.ApplyModifiedPropertiesWithoutUndo();
		Log("MerchantPig hidden until the merchant hotspot activates");

		EditorSceneManager.MarkSceneDirty(scene);
		if (hadUnsavedChanges)
		{
			Log("not saved: the scene already had unsaved changes, save it in the editor");
			return;
		}
		EditorSceneManager.SaveScene(scene);
		Log("saved " + ScenePath);
	}

	private static ActionNode Add(NodeType type, Action<ActionNode> init)
	{
		var node = new ActionNode
		{
			nodeID = s_Nodes.Count + 1,
			type = type,
			enabled = true,
			text = string.Empty,
			objectName = string.Empty,
			secondaryText = string.Empty,
			param = new string[0],
			nodesIn = new int[0],
			nodesOut = new int[0],
			nodesOutIndex = new int[0],
			editorRect = new Rect(40f + s_Column * 260f, 40f + s_Row * 300f, 230f, 230f)
		};
		init(node);
		if (s_Nodes.Count > 0)
		{
			var previous = s_Nodes[s_Nodes.Count - 1];
			previous.nodesOut = new[] { node.nodeID };
			previous.nodesOutIndex = new[] { 0 };
			node.nodesIn = new[] { previous.nodeID };
		}
		s_Nodes.Add(node);
		s_Column++;
		return node;
	}

	private static void NextRow()
	{
		s_Row++;
		s_Column = 0;
	}

	private static void SpawnCharacter(GameObject prefab, string modelId, string objectName, Transform at)
	{
		Add(NodeType.InstantiateCharacter, n =>
		{
			n.refObject = prefab;
			n.refObject2 = at;
			n.text = modelId;
			n.objectName = objectName;
		});
	}

	private static void Scale(string objectName, Vector3 scale)
	{
		Add(NodeType.SetScale, n => { n.objectType = 1; n.objectName = objectName; n.customVec1 = scale; });
	}

	// wait: 0 = continue instantly, 1 = wait for the clip to finish.
	private static void Anim(string objectName, string clip, int wait, bool queueIdle)
	{
		Add(NodeType.PlayBoneAnimation, n =>
		{
			n.objectType = 1;
			n.objectName = objectName;
			n.text = clip;
			n.customInt = wait;
			n.QueueIdle = queueIdle;
			n.secondaryText = queueIdle ? "Idle" : string.Empty;
		});
	}

	private static void MoveTo(string objectName, Transform from, Vector3 fromOffset, Transform to, Vector3 toOffset, float unitsPerSecond, bool wait)
	{
		Add(NodeType.MoveTo, n =>
		{
			n.objectType = 1;
			n.objectName = objectName;
			n.refObject1 = from;
			n.customVec1 = fromOffset;
			n.refObject2 = to;
			n.customVec2 = toOffset;
			n.customInt = (int)CHMotionTween.TimingTypes.UnitsPerSecond;
			n.customFloat = unitsPerSecond;
			n.customInt2 = wait ? 1 : 0;
		});
	}

	private static void HandItem(string objectName, bool show, string itemFrom)
	{
		Add(NodeType.SetHandItem, n =>
		{
			n.objectType = 1;
			n.objectName = objectName;
			n.customInt = 0;
			n.customBool = show;
			n.secondaryText = itemFrom;
		});
	}

	private static void Destroy(string objectName)
	{
		Add(NodeType.Destroy, n => { n.objectType = 1; n.objectName = objectName; });
	}

	private static Transform Marker(Transform sequence, string markerName, Vector3 worldPosition)
	{
		var marker = sequence.Find(markerName);
		if (marker == null)
		{
			marker = new GameObject(markerName).transform;
			marker.SetParent(sequence, false);
			marker.position = worldPosition;
			Log("marker " + markerName + " at " + worldPosition);
		}
		return marker;
	}

	private static Transform Find(Scene scene, string path)
	{
		var parts = path.Split('/');
		foreach (var root in scene.GetRootGameObjects())
		{
			if (root.name == parts[0])
			{
				var found = parts.Length == 1 ? root.transform : root.transform.Find(path.Substring(parts[0].Length + 1));
				if (found != null)
				{
					return found;
				}
			}
		}
		throw new Exception("not found in scene: " + path);
	}
}
