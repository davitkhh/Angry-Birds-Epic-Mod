using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Makes the merchant stall's sign (poles with cloth) grow out of the ground like a level node's post, just before the
// orange "Shop" label pops in on it (see ShopSignPopInBuilder). Hidden before the McCool robbery cutscene; shown
// without the pop on later loads. Run from the menu, or touch Temp/StallSignPopIn.request with the editor open.
public static class StallSignPopInBuilder
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/StallSignPopIn.request";

	private const string LogFile = "Temp/StallSignPopIn.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const string MerchantPath = "WorldMap/2_Scenery/Zone_02/3_Hotspots/Hotspot_008_02_Merchant";

	private const string LevelNodePopPath = "Assets/AnimationClip/Battleground_SetActive.anim";

	private const string ClipPath = "Assets/AnimationClip/StallSign_SetActive.anim";

	private const string ClipName = "StallSign_SetActive";

	private const string UnlockSound = "World_StageUnlocked";

	// Same gap as between the level node's node and post pops.
	private const float LabelDelay = 0.25f;

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
		Build();
	}

	[MenuItem("Tools/World Map/Add Stall Sign Pop-In")]
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
		Debug.Log("[StallSignPopInBuilder]\n" + s_Log);
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

		var merchant = Find(scene, MerchantPath);
		var stallSign = merchant.Find("MerchantStall/Sign");
		var shopLabel = merchant.Find("MerchantStall/ShopSign");
		var tree = Find(scene, SequencePath).GetComponent<ActionTree>();
		if (tree.nodes.Any(n => n.refObject == stallSign.gameObject))
		{
			Log("aborted: the cutscene already shows the stall sign");
			return;
		}
		var showLabel = tree.nodes.FirstOrDefault(n => n.type == NodeType.SetActive && n.refObject == shopLabel.gameObject && n.customBool);
		if (showLabel == null)
		{
			Log("aborted: no SetActive ShopSign node; run Tools/World Map/Add Shop Sign Pop-In first");
			return;
		}
		var before = tree.nodes.Where(n => n.nodesOut != null && n.nodesOut.Contains(showLabel.nodeID)).ToList();
		if (before.Count != 1)
		{
			Log("aborted: expected exactly one node leading into node " + showLabel.nodeID);
			return;
		}

		var clip = CreatePopClip(stallSign.localScale);
		var animation = stallSign.GetComponent<Animation>();
		if (animation == null)
		{
			animation = stallSign.gameObject.AddComponent<Animation>();
		}
		animation.playAutomatically = false;
		animation.clip = clip;
		AnimationUtility.SetAnimationClips(animation, new[] { clip });
		EditorUtility.SetDirty(animation);

		stallSign.gameObject.SetActive(false);
		var shop = new SerializedObject(merchant.GetComponent<HotSpotWorldMapViewShopNode>());
		var activate = shop.FindProperty("m_activateObjects");
		if (!Enumerable.Range(0, activate.arraySize).Any(i => activate.GetArrayElementAtIndex(i).objectReferenceValue == stallSign.gameObject))
		{
			activate.InsertArrayElementAtIndex(activate.arraySize);
			activate.GetArrayElementAtIndex(activate.arraySize - 1).objectReferenceValue = stallSign.gameObject;
		}
		shop.ApplyModifiedPropertiesWithoutUndo();
		Log("stall Sign hidden until the merchant hotspot activates");

		var nodes = tree.nodes.ToList();

		// The level node plays its unlock sound once, as the pop starts: move the label's sound to the sign pop.
		var labelPop = showLabel.nodesOut != null && showLabel.nodesOut.Length == 1 ? tree.GetNodeByID(showLabel.nodesOut[0]) : null;
		var labelSound = labelPop != null && labelPop.nodesOut != null && labelPop.nodesOut.Length == 1 ? tree.GetNodeByID(labelPop.nodesOut[0]) : null;
		if (labelSound != null && labelSound.type == NodeType.PlaySound && labelSound.text == UnlockSound)
		{
			labelPop.nodesOut = labelSound.nodesOut;
			labelPop.nodesOutIndex = labelSound.nodesOutIndex;
			foreach (var id in labelSound.nodesOut ?? new int[0])
			{
				var target = nodes.First(n => n.nodeID == id);
				target.nodesIn = (target.nodesIn ?? new int[0]).Select(x => x == labelSound.nodeID ? labelPop.nodeID : x).ToArray();
			}
			nodes.Remove(labelSound);
			Log("moved the unlock sound (node " + labelSound.nodeID + ") to the sign pop");
		}

		var nextId = nodes.Max(n => n.nodeID) + 1;
		var rect = showLabel.editorRect;
		var added = new[]
		{
			NewNode(nextId, NodeType.SetActive, rect, n => { n.refObject = stallSign.gameObject; n.customBool = true; }),
			NewNode(nextId + 1, NodeType.PlayAnimation, rect, n => { n.refObject = stallSign.gameObject; n.text = ClipName; }),
			NewNode(nextId + 2, NodeType.PlaySound, rect, n => n.text = UnlockSound),
			NewNode(nextId + 3, NodeType.Delay, rect, n => n.customFloat = LabelDelay)
		};
		for (var i = 0; i < added.Length; i++)
		{
			added[i].editorRect = new Rect(rect.x + i * 260f, rect.y - 260f, rect.width, rect.height);
		}
		var from = before[0];
		from.nodesOut = from.nodesOut.Select(x => x == showLabel.nodeID ? added[0].nodeID : x).ToArray();
		added[0].nodesIn = new[] { from.nodeID };
		for (var i = 1; i < added.Length; i++)
		{
			Link(added[i - 1], added[i]);
		}
		Link(added[added.Length - 1], showLabel);
		nodes.AddRange(added);
		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
		Log("inserted nodes " + added[0].nodeID + "-" + added[added.Length - 1].nodeID + " before node " + showLabel.nodeID);

		EditorSceneManager.MarkSceneDirty(scene);
		if (hadUnsavedChanges)
		{
			Log("not saved: the scene already had unsaved changes, save it in the editor");
			return;
		}
		EditorSceneManager.SaveScene(scene);
		Log("saved " + ScenePath);
	}

	// The level node's post pop (Battleground_SetActive's Body/Post scale curve), moved to start at 0 and scaled to
	// the sign's rest scale. The sign mesh's pivot is at the foot of its poles, so it grows out of the ground.
	private static AnimationClip CreatePopClip(Vector3 restScale)
	{
		var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
		if (existing != null)
		{
			return existing;
		}
		var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(LevelNodePopPath);
		var clip = new AnimationClip { name = ClipName, legacy = true, wrapMode = WrapMode.Once };
		foreach (var axis in new[] { "x", "y", "z" })
		{
			var curve = AnimationUtility.GetEditorCurve(source, EditorCurveBinding.FloatCurve("Body/Post", typeof(Transform), "m_LocalScale." + axis));
			var start = curve.keys[0].time;
			var factor = axis == "x" ? restScale.x : axis == "y" ? restScale.y : restScale.z;
			var keys = curve.keys.Select(k => new Keyframe(k.time - start, k.value * factor, k.inTangent * factor, k.outTangent * factor)).ToArray();
			clip.SetCurve(string.Empty, typeof(Transform), "localScale." + axis, new AnimationCurve(keys));
		}
		AssetDatabase.CreateAsset(clip, ClipPath);
		AssetDatabase.SaveAssets();
		Log("created " + ClipPath + " (length " + clip.length + "s)");
		return clip;
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
			editorRect = anchor
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
