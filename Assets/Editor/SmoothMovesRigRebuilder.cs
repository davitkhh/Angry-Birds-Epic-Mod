using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Full SmoothMoves rebuild of one rig's prefabs (what the BoneAnimation inspector's "Force Build This" does), without the
// broken references it leaves behind: a build recreates every bone, so references from the rig's own components into the
// bones (CharacterAssetController's MainHandBone, OffHandBone, BodyCenter, BodyRoot...) are re-pointed by bone name.
// Request: write lines to Temp/SmoothMovesRig.request; result in Temp/SmoothMovesRig.log. Lines run in order:
//   depth <data asset path> <bone name> <from> <to>   every keyframe of that bone (all clips) at depth <from> gets <to>
//   build <prefab path>                               rebuilds the prefab from its animation data (data built once per request)
// Edits go through the loaded data asset, so an open SmoothMoves editor sees them. Back up the data, prefabs and
// MonoBehaviour/<Rig>_Resources first; only build rigs whose data is meant to change (the build rewrites the data asset).
public static class SmoothMovesRigRebuilder
{
	private const string RequestFile = "Temp/SmoothMovesRig.request";

	private const string LogFile = "Temp/SmoothMovesRig.log";

	private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

	private const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

	[InitializeOnLoadMethod]
	private static void Init()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		var lines = File.ReadAllLines(RequestFile).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
		File.Delete(RequestFile);
		var log = new StringBuilder();
		var builtData = new HashSet<Object>();
		try
		{
			foreach (var line in lines)
			{
				var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts[0] == "depth" && parts.Length == 5)
				{
					SetDepth(parts[1], parts[2], int.Parse(parts[3]), int.Parse(parts[4]), log);
				}
				else if (parts[0] == "build" && parts.Length == 2)
				{
					Build(parts[1], builtData, log);
				}
				else
				{
					log.AppendLine("unknown request line: " + line);
				}
			}
		}
		catch (Exception e)
		{
			log.AppendLine("EXCEPTION " + e);
		}
		EditorUtility.ClearProgressBar();
		File.WriteAllText(LogFile, log.ToString());
	}

	private static void SetDepth(string dataPath, string boneName, int from, int to, StringBuilder log)
	{
		var data = AssetDatabase.LoadMainAssetAtPath(dataPath);
		if (data == null)
		{
			log.AppendLine("no asset " + dataPath);
			return;
		}
		var so = new SerializedObject(data);
		var boneIndex = -1;
		var boneList = so.FindProperty("boneDataList");
		for (var i = 0; i < boneList.arraySize; i++)
		{
			var bone = boneList.GetArrayElementAtIndex(i);
			if (bone.FindPropertyRelative("boneName").stringValue == boneName)
			{
				// A bone's index is its position in the list.
				boneIndex = i;
			}
		}
		if (boneIndex < 0)
		{
			log.AppendLine("no bone " + boneName + " in " + dataPath);
			return;
		}
		var changed = 0;
		var clips = so.FindProperty("animationClips");
		for (var c = 0; c < clips.arraySize; c++)
		{
			var clip = clips.GetArrayElementAtIndex(c);
			var bones = clip.FindPropertyRelative("bones");
			for (var b = 0; b < bones.arraySize; b++)
			{
				var bone = bones.GetArrayElementAtIndex(b);
				if (bone.FindPropertyRelative("boneDataIndex").intValue != boneIndex)
				{
					continue;
				}
				var keyframes = bone.FindPropertyRelative("keyframes");
				for (var k = 0; k < keyframes.arraySize; k++)
				{
					var depth = keyframes.GetArrayElementAtIndex(k).FindPropertyRelative("depth");
					var value = depth.propertyType == SerializedPropertyType.Float ? Mathf.RoundToInt(depth.floatValue) : depth.intValue;
					if (value != from)
					{
						continue;
					}
					if (depth.propertyType == SerializedPropertyType.Float)
					{
						depth.floatValue = to;
					}
					else
					{
						depth.intValue = to;
					}
					changed++;
					log.AppendLine("  " + clip.FindPropertyRelative("animationName").stringValue + " frame " + keyframes.GetArrayElementAtIndex(k).FindPropertyRelative("frame").intValue + ": " + from + " -> " + to);
				}
			}
		}
		var needsRebuilt = so.FindProperty("needsRebuilt");
		if (needsRebuilt != null && needsRebuilt.propertyType == SerializedPropertyType.Boolean && changed > 0)
		{
			needsRebuilt.boolValue = true;
		}
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(data);
		AssetDatabase.SaveAssets();
		log.AppendLine(boneName + " (bone " + boneIndex + "): " + changed + " keyframes at depth " + from + " set to " + to);
	}

	private static void Build(string prefabPath, HashSet<Object> builtData, StringBuilder log)
	{
		var root = PrefabUtility.LoadPrefabContents(prefabPath);
		try
		{
			var bones = root.GetComponentsInChildren<Component>(true).First(c => c != null && c.GetType().Name == "BoneAnimation");
			var data = bones.GetType().GetField("animationData", Fields).GetValue(bones) as Object;
			if (data == null)
			{
				log.AppendLine(prefabPath + ": no animation data, can't build");
				return;
			}
			var references = RecordBoneReferences(root, bones.transform);
			var atlasesBefore = ReadAtlases(bones);
			var buildHelper = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("SmoothMoves.BuildHelper")).First(t => t != null);
			if (builtData.Add(data))
			{
				var args = new object[] { data, 0f, 1f, 0 };
				buildHelper.GetMethod("UpdateBoneAnimationData", Statics).Invoke(null, args);
				log.AppendLine("built data " + AssetDatabase.GetAssetPath(data) + " (" + args[3] + ")");
			}
			buildHelper.GetMethod("UpdateBoneAnimation", Statics).Invoke(null, new object[] { bones });
			// The build can leave animationData null, and then the prefab can't be built again (PigPirate, 2026-11-11).
			var dataField = bones.GetType().GetField("animationData", Fields);
			if (dataField.GetValue(bones) as Object == null)
			{
				dataField.SetValue(bones, data);
				log.AppendLine("  animationData re-assigned");
			}
			log.AppendLine(prefabPath + ": rebuilt");
			KeepAtlases(root, bones, atlasesBefore, log);
			RestoreBoneReferences(references, bones.transform, log);
			PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
			log.AppendLine(prefabPath + ": saved");
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}
	}

	private static List<Object> ReadAtlases(Component bones)
	{
		var list = new SerializedObject(bones).FindProperty("textureAtlases");
		var result = new List<Object>();
		for (var i = 0; i < list.arraySize; i++)
		{
			result.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
		}
		return result;
	}

	// A build sets the atlas list (and every trigger frame's atlases) to the high-res atlases the data uses. The prefab's
	// own list wins: a _low prefab gets its X_low counterparts back (trigger frames and renderer materials too), and
	// swap-only atlases added by hand (pirate variants, Forge) are appended again.
	private static void KeepAtlases(GameObject root, Component bones, List<Object> before, StringBuilder log)
	{
		before = before.Where(a => a != null).ToList();
		var swap = new Dictionary<Object, Object>();
		foreach (var atlas in ReadAtlases(bones).Where(a => a != null && !before.Contains(a)))
		{
			var counterpart = before.FirstOrDefault(a => a.name == atlas.name + "_low" || a.name + "_low" == atlas.name);
			if (counterpart != null)
			{
				swap[atlas] = counterpart;
			}
			else
			{
				log.AppendLine("  new atlas from the data: " + atlas.name);
			}
		}
		var so = new SerializedObject(bones);
		var remapped = 0;
		var triggers = so.FindProperty("triggerFrames");
		for (var t = 0; t < triggers.arraySize; t++)
		{
			var frameBones = triggers.GetArrayElementAtIndex(t).FindPropertyRelative("triggerFrameBones");
			for (var b = 0; b < frameBones.arraySize; b++)
			{
				foreach (var field in new[] { "atlas", "originalAtlas" })
				{
					var property = frameBones.GetArrayElementAtIndex(b).FindPropertyRelative(field);
					Object counterpart;
					if (property != null && property.objectReferenceValue != null && swap.TryGetValue(property.objectReferenceValue, out counterpart))
					{
						property.objectReferenceValue = counterpart;
						remapped++;
					}
				}
			}
		}
		var list = so.FindProperty("textureAtlases");
		var atlases = new List<Object>();
		for (var i = 0; i < list.arraySize; i++)
		{
			Object counterpart;
			var atlas = list.GetArrayElementAtIndex(i).objectReferenceValue;
			atlases.Add(atlas != null && swap.TryGetValue(atlas, out counterpart) ? counterpart : atlas);
		}
		var appended = before.Where(a => !atlases.Contains(a)).ToList();
		atlases.AddRange(appended);
		list.arraySize = atlases.Count;
		for (var i = 0; i < atlases.Count; i++)
		{
			list.GetArrayElementAtIndex(i).objectReferenceValue = atlases[i];
		}
		so.ApplyModifiedPropertiesWithoutUndo();
		var materials = swap.ToDictionary(p => new SerializedObject(p.Key).FindProperty("material").objectReferenceValue, p => new SerializedObject(p.Value).FindProperty("material").objectReferenceValue);
		var materialsSwapped = 0;
		foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
		{
			var shared = renderer.sharedMaterials;
			for (var i = 0; i < shared.Length; i++)
			{
				Object material;
				if (shared[i] != null && materials.TryGetValue(shared[i], out material) && material != null)
				{
					shared[i] = (Material)material;
					materialsSwapped++;
				}
			}
			renderer.sharedMaterials = shared;
		}
		if (swap.Count > 0 || appended.Count > 0)
		{
			log.AppendLine("  atlases kept: " + swap.Count + " swapped back (" + remapped + " trigger refs, " + materialsSwapped + " materials), appended " + string.Join(", ", appended.Select(a => a.name).ToArray()));
		}
	}

	private class BoneReference
	{
		public Component Owner;

		public string Path;

		public string BoneName;

		public bool IsGameObject;
	}

	// References from components outside the bone hierarchy (they survive the build) to objects inside it.
	private static List<BoneReference> RecordBoneReferences(GameObject root, Transform boneRoot)
	{
		var result = new List<BoneReference>();
		foreach (var component in root.GetComponentsInChildren<Component>(true))
		{
			if (component == null || component is Transform || component.GetType().Name == "BoneAnimation" || (component.transform != boneRoot && component.transform.IsChildOf(boneRoot)))
			{
				continue;
			}
			var so = new SerializedObject(component);
			var property = so.GetIterator();
			while (property.Next(true))
			{
				if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null)
				{
					continue;
				}
				var target = property.objectReferenceValue as Transform;
				var targetObject = property.objectReferenceValue as GameObject;
				var transform = target != null ? target : targetObject != null ? targetObject.transform : null;
				if (transform != null && transform != boneRoot && transform.IsChildOf(boneRoot))
				{
					result.Add(new BoneReference { Owner = component, Path = property.propertyPath, BoneName = transform.name, IsGameObject = targetObject != null });
				}
			}
		}
		return result;
	}

	private static void RestoreBoneReferences(List<BoneReference> references, Transform boneRoot, StringBuilder log)
	{
		foreach (var reference in references)
		{
			if (reference.Owner == null)
			{
				log.AppendLine("  skipped " + reference.Path + " -> " + reference.BoneName + ": its component was replaced by the build");
				continue;
			}
			var so = new SerializedObject(reference.Owner);
			var property = so.FindProperty(reference.Path);
			if (property.objectReferenceValue != null)
			{
				log.AppendLine("  kept " + reference.Owner.GetType().Name + "." + reference.Path + " -> " + reference.BoneName);
				continue;
			}
			var bone = boneRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == reference.BoneName);
			if (bone == null)
			{
				log.AppendLine("  LOST " + reference.Owner.GetType().Name + "." + reference.Path + ": no bone named " + reference.BoneName);
				continue;
			}
			property.objectReferenceValue = reference.IsGameObject ? (Object)bone.gameObject : bone;
			so.ApplyModifiedPropertiesWithoutUndo();
			log.AppendLine("  re-pointed " + reference.Owner.GetType().Name + "." + reference.Path + " -> " + reference.BoneName);
		}
	}
}
