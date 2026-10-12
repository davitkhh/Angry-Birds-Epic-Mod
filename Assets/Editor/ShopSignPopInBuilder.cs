using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Makes the merchant's orange "Shop" sign pop in, like a level node unlocking, right after McCool takes his place
// at the stall in the McCool robbery cutscene. Hidden before that; shown without the pop on later loads (merchant
// activate objects). Run from the menu, or touch Temp/ShopSignPopIn.request with the editor open.
public static class ShopSignPopInBuilder
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/ShopSignPopIn.request";

	private const string LogFile = "Temp/ShopSignPopIn.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private const string MerchantPath = "WorldMap/2_Scenery/Zone_02/3_Hotspots/Hotspot_008_02_Merchant";

	private const string LevelNodePopPath = "Assets/AnimationClip/Battleground_SetActive.anim";

	private const string ClipPath = "Assets/AnimationClip/ShopSign_SetActive.anim";

	private const string ClipName = "ShopSign_SetActive";

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

	[MenuItem("Tools/World Map/Add Shop Sign Pop-In")]
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
		Debug.Log("[ShopSignPopInBuilder]\n" + s_Log);
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
		var merchantPig = merchant.Find("MerchantPig");
		var sign = merchant.Find("MerchantStall/ShopSign");
		var tree = Find(scene, SequencePath).GetComponent<ActionTree>();
		if (tree.nodes.Any(n => n.refObject == sign.gameObject))
		{
			Log("aborted: the cutscene already shows the shop sign");
			return;
		}
		// Insert after McCool hands over to the stall's MerchantPig (and his cutscene copy is destroyed).
		var handOver = tree.nodes.FirstOrDefault(n => n.type == NodeType.SetActive && n.refObject == merchantPig.gameObject && n.customBool);
		if (handOver == null)
		{
			Log("aborted: no SetActive MerchantPig node in the cutscene");
			return;
		}
		var after = handOver;
		var next = after.nodesOut != null && after.nodesOut.Length == 1 ? tree.GetNodeByID(after.nodesOut[0]) : null;
		if (next != null && next.type == NodeType.Destroy && next.objectName == "McCool")
		{
			after = next;
		}

		var clip = CreatePopClip(sign.localScale);

		var animation = sign.GetComponent<Animation>();
		if (animation == null)
		{
			animation = sign.gameObject.AddComponent<Animation>();
		}
		animation.playAutomatically = false;
		animation.clip = clip;
		AnimationUtility.SetAnimationClips(animation, new[] { clip });
		EditorUtility.SetDirty(animation);

		// Hidden until the cutscene pops it in; the merchant's activate objects show it (no pop) on later loads.
		sign.gameObject.SetActive(false);
		var shop = new SerializedObject(merchant.GetComponent<HotSpotWorldMapViewShopNode>());
		var activate = shop.FindProperty("m_activateObjects");
		if (!Enumerable.Range(0, activate.arraySize).Any(i => activate.GetArrayElementAtIndex(i).objectReferenceValue == sign.gameObject))
		{
			activate.InsertArrayElementAtIndex(activate.arraySize);
			activate.GetArrayElementAtIndex(activate.arraySize - 1).objectReferenceValue = sign.gameObject;
		}
		shop.ApplyModifiedPropertiesWithoutUndo();
		Log("ShopSign hidden until the merchant hotspot activates");

		var nodes = tree.nodes.ToList();
		var nextId = nodes.Max(n => n.nodeID) + 1;
		var rect = after.editorRect;
		var added = new[]
		{
			NewNode(nextId, NodeType.Delay, rect, 1, n => n.customFloat = 0.3f),
			NewNode(nextId + 1, NodeType.SetActive, rect, 2, n => { n.refObject = sign.gameObject; n.customBool = true; }),
			NewNode(nextId + 2, NodeType.PlayAnimation, rect, 3, n => { n.refObject = sign.gameObject; n.text = ClipName; }),
			NewNode(nextId + 3, NodeType.PlaySound, rect, 4, n => n.text = "World_StageUnlocked")
		};
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
		tree.nodes = nodes.ToArray();
		EditorUtility.SetDirty(tree);
		Log("inserted nodes " + added[0].nodeID + "-" + last.nodeID + " after node " + after.nodeID);

		EditorSceneManager.MarkSceneDirty(scene);
		if (hadUnsavedChanges)
		{
			Log("not saved: the scene already had unsaved changes, save it in the editor");
			return;
		}
		EditorSceneManager.SaveScene(scene);
		Log("saved " + ScenePath);
	}

	// The level node pop (Battleground_SetActive's Body/Node scale curve), scaled to the sign's own scale.
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
			var binding = EditorCurveBinding.FloatCurve("Body/Node", typeof(Transform), "m_LocalScale." + axis);
			var curve = AnimationUtility.GetEditorCurve(source, binding);
			var factor = axis == "x" ? restScale.x : axis == "y" ? restScale.y : restScale.z;
			var keys = curve.keys.Select(k => new Keyframe(k.time, k.value * factor, k.inTangent * factor, k.outTangent * factor)).ToArray();
			clip.SetCurve(string.Empty, typeof(Transform), "localScale." + axis, new AnimationCurve(keys));
		}
		AssetDatabase.CreateAsset(clip, ClipPath);
		AssetDatabase.SaveAssets();
		Log("created " + ClipPath + " (length " + clip.length + "s)");
		return clip;
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
