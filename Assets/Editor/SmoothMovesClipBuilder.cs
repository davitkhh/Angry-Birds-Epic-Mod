using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Builds one SmoothMoves clip of a bird rig into that rig's prefabs without a full SmoothMoves rebuild (which recreates
// every bone and breaks CharacterAssetController refs, and can't build the _low prefabs at all).
// Request: write "<Rig> <Clip>" (e.g. "RedBird Jump"; the clip name may contain spaces) to Temp/SmoothMovesClip.request; result
// in Temp/SmoothMovesClip.log. An optional second line "scene <scene path>" patches the rigs in that scene instead of the prefabs:
// every BoneAnimation there that uses <Rig>'s animation data (e.g. the world map's baked golden pigs).
// The clip is built on a throwaway copy of Resources/Prefabs/4_Characters/**/<Rig>.prefab in a preview scene, saved to
// Assets/AnimationClip/<Rig>/<Clip>.anim (overwritten in place when it exists, so its guid stays), then added to or replaced in
// <Rig>.prefab and <Rig>_low.prefab (or the scene rigs) together with its lite clip entry, fps state and trigger frames.
// The build also rewrites MonoBehaviour/<Rig>.asset and creates MonoBehaviour/<Rig>_Resources: back the asset up before and
// restore it afterwards, and delete the _Resources folder.
public static class SmoothMovesClipBuilder
{
	private const string RequestFile = "Temp/SmoothMovesClip.request";

	private const string LogFile = "Temp/SmoothMovesClip.log";

	private const string PrefabRoot = "Assets/Resources/Prefabs/4_Characters";

	private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

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
		try
		{
			var request = lines.Length > 0 ? lines[0].Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries) : new string[0];
			var scenePath = lines.Length > 1 && lines[1].StartsWith("scene ") ? lines[1].Substring(6).Trim() : null;
			if (request.Length != 2)
			{
				log.AppendLine("request must be '<Rig> <Clip>' with an optional second line 'scene <scene path>'");
			}
			else
			{
				Build(request[0], request[1].Trim(), scenePath, log);
			}
		}
		catch (Exception e)
		{
			log.AppendLine("EXCEPTION " + e);
		}
		EditorUtility.ClearProgressBar();
		File.WriteAllText(LogFile, log.ToString());
	}

	private static void Build(string rig, string clipName, string scenePath, StringBuilder log)
	{
		var prefabPath = AssetDatabase.FindAssets(rig + " t:Prefab", new[] { PrefabRoot }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == rig);
		var clipPath = "Assets/AnimationClip/" + rig + "/" + clipName + ".anim";
		var prefab = prefabPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) : null;
		if (prefab == null)
		{
			log.AppendLine("no prefab " + rig + ".prefab under " + PrefabRoot);
			return;
		}
		var scene = EditorSceneManager.NewPreviewScene();
		try
		{
			var instance = (GameObject)Object.Instantiate(prefab);
			UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, scene);
			var built = FindBoneAnimation(instance);
			var data = built.GetType().GetField("animationData", Fields).GetValue(built) as Object;
			var buildHelper = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("SmoothMoves.BuildHelper")).First(t => t != null);
			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
			var args = new object[] { data, 0f, 1f, 0 };
			buildHelper.GetMethod("UpdateBoneAnimationData", flags).Invoke(null, args);
			buildHelper.GetMethod("UpdateBoneAnimation", flags).Invoke(null, new object[] { built });
			log.AppendLine("built " + args[3] + " clips from " + AssetDatabase.GetAssetPath(data));

			var builtClip = built.GetComponent<Animation>().GetClip(clipName);
			if (builtClip == null)
			{
				log.AppendLine("the data has no clip " + clipName);
				return;
			}
			var clipAsset = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
			if (clipAsset != null)
			{
				EditorUtility.CopySerialized(builtClip, clipAsset);
				clipAsset.name = clipName;
				EditorUtility.SetDirty(clipAsset);
				log.AppendLine("clip overwritten " + clipPath);
			}
			else
			{
				Directory.CreateDirectory(Path.GetDirectoryName(clipPath));
				AssetDatabase.Refresh();
				clipAsset = Object.Instantiate(builtClip);
				clipAsset.name = clipName;
				AssetDatabase.CreateAsset(clipAsset, clipPath);
				log.AppendLine("clip created " + clipPath);
			}
			AssetDatabase.SaveAssets();
			log.AppendLine("clip length=" + clipAsset.length + " curves=" + AnimationUtility.GetCurveBindings(clipAsset).Length + " guid=" + AssetDatabase.AssetPathToGUID(clipPath));

			var builtClips = GetList(built, "mAnimationClips");
			var index = Names(builtClips).IndexOf(clipName);
			var builtStates = GetList(built, "mAnimationStates");
			var builtTriggers = GetList(built, "triggerFrames").Cast<object>().Where(t => ClipIndex(t) == index).ToList();
			log.AppendLine("built index " + index + ", trigger frames " + builtTriggers.Count);

			var builtState = builtStates != null && index < builtStates.Count ? builtStates[index] : null;
			if (scenePath != null)
			{
				PatchScene(scenePath, data, clipName, clipAsset, index, builtClips[index], builtState, builtTriggers, log);
				return;
			}
			foreach (var path in new[] { prefabPath, prefabPath.Replace(".prefab", "_low.prefab") })
			{
				if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
				{
					PatchPrefab(path, clipName, clipAsset, index, builtClips[index], builtState, builtTriggers, log);
				}
			}
		}
		finally
		{
			EditorSceneManager.ClosePreviewScene(scene);
		}
	}

	private static void PatchScene(string scenePath, Object data, string clipName, AnimationClip clip, int index, object liteClip, object state, List<object> triggers, StringBuilder log)
	{
		if (EditorApplication.isPlaying)
		{
			log.AppendLine("leave play mode before patching a scene");
			return;
		}
		var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
		if (!scene.isLoaded)
		{
			scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
		}
		var hadUnsavedChanges = scene.isDirty;
		var targets = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true)).Where(c => c != null && c.GetType().Name == "BoneAnimation" && c.GetType().GetField("animationData", Fields).GetValue(c) as Object == data).ToList();
		var patched = 0;
		foreach (var target in targets)
		{
			var name = target.transform.parent != null ? target.transform.parent.name + "/" + target.name : target.name;
			if (PatchRig(target, name, clipName, clip, index, liteClip, state, triggers, log))
			{
				EditorUtility.SetDirty(target);
				patched++;
			}
		}
		log.AppendLine(scenePath + ": patched " + patched + " of " + targets.Count + " rigs");
		if (patched == 0)
		{
			return;
		}
		EditorSceneManager.MarkSceneDirty(scene);
		if (hadUnsavedChanges)
		{
			log.AppendLine("not saved: the scene already had unsaved changes, save it in the editor");
		}
		else
		{
			EditorSceneManager.SaveScene(scene);
			log.AppendLine("saved " + scenePath);
		}
	}

	private static void PatchPrefab(string path, string clipName, AnimationClip clip, int index, object liteClip, object state, List<object> triggers, StringBuilder log)
	{
		var root = PrefabUtility.LoadPrefabContents(path);
		try
		{
			if (PatchRig(FindBoneAnimation(root), path, clipName, clip, index, liteClip, state, triggers, log))
			{
				PrefabUtility.SaveAsPrefabAsset(root, path);
			}
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}
	}

	private static bool PatchRig(Component target, string label, string clipName, AnimationClip clip, int index, object liteClip, object state, List<object> triggers, StringBuilder log)
	{
		var names = Names(GetList(target, "mAnimationClips"));
		var existing = names.IndexOf(clipName);
		if (existing >= 0 && existing != index || existing < 0 && names.Count != index)
		{
			log.AppendLine(label + ": clip order differs from the data (" + clipName + " at " + existing + " of " + names.Count + ", built at " + index + "); skipped");
			return false;
		}
		if (existing >= 0)
		{
			SetAt(target, "mAnimationClips", index, liteClip);
			if (state != null)
			{
				SetAt(target, "mAnimationStates", index, state);
			}
		}
		else
		{
			Append(target, "mAnimationClips", liteClip);
			if (state != null)
			{
				Append(target, "mAnimationStates", state);
			}
		}
		var kept = GetList(target, "triggerFrames").Cast<object>().Where(t => ClipIndex(t) != index).ToList();
		kept.AddRange(triggers);
		SetList(target, "triggerFrames", kept);

		// Reference the saved clip asset (Animation.AddClip would store an unsaved copy)
		var animation = new SerializedObject(target.GetComponent<Animation>());
		var clips = animation.FindProperty("m_Animations");
		var found = false;
		for (var i = 0; i < clips.arraySize; i++)
		{
			var element = clips.GetArrayElementAtIndex(i);
			var current = element.objectReferenceValue as AnimationClip;
			if (current == clip || current != null && current.name == clipName)
			{
				element.objectReferenceValue = clip;
				found = true;
			}
		}
		if (!found)
		{
			clips.InsertArrayElementAtIndex(clips.arraySize);
			clips.GetArrayElementAtIndex(clips.arraySize - 1).objectReferenceValue = clip;
		}
		animation.ApplyModifiedPropertiesWithoutUndo();
		log.AppendLine(label + (existing >= 0 ? " replaced " : " added ") + clipName);
		return true;
	}

	private static Component FindBoneAnimation(GameObject root)
	{
		return root.GetComponentsInChildren<Component>(true).First(c => c != null && c.GetType().Name == "BoneAnimation");
	}

	private static int ClipIndex(object trigger)
	{
		return (int)trigger.GetType().GetField("clipIndex", Fields).GetValue(trigger);
	}

	private static IList GetList(Component c, string field)
	{
		var value = c.GetType().GetField(field, Fields).GetValue(c);
		var array = value as Array;
		return array != null ? array.Cast<object>().ToList() : value as IList;
	}

	private static List<string> Names(IList clips)
	{
		return clips.Cast<object>().Select(o => (string)o.GetType().GetField("animationName", Fields).GetValue(o)).ToList();
	}

	private static void SetAt(Component c, string field, int index, object item)
	{
		var value = c.GetType().GetField(field, Fields).GetValue(c);
		var array = value as Array;
		if (array != null)
		{
			array.SetValue(item, index);
		}
		else
		{
			((IList)value)[index] = item;
		}
	}

	private static void Append(Component c, string field, object item)
	{
		var info = c.GetType().GetField(field, Fields);
		var value = info.GetValue(c);
		var array = value as Array;
		if (array != null)
		{
			var grown = Array.CreateInstance(array.GetType().GetElementType(), array.Length + 1);
			Array.Copy(array, grown, array.Length);
			grown.SetValue(item, array.Length);
			info.SetValue(c, grown);
		}
		else
		{
			((IList)value).Add(item);
		}
	}

	private static void SetList(Component c, string field, List<object> items)
	{
		var info = c.GetType().GetField(field, Fields);
		var value = info.GetValue(c);
		var array = value as Array;
		if (array != null)
		{
			var replaced = Array.CreateInstance(array.GetType().GetElementType(), items.Count);
			for (var i = 0; i < items.Count; i++)
			{
				replaced.SetValue(items[i], i);
			}
			info.SetValue(c, replaced);
		}
		else
		{
			var list = (IList)value;
			list.Clear();
			foreach (var item in items)
			{
				list.Add(item);
			}
		}
	}
}
