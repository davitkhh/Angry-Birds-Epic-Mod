using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// "Build Rig" button for SmoothMoves rigs: the same full build as SmoothMovesRigRebuilder's request file (bone references
// re-pointed, atlas lists kept), for <Rig>.prefab and <Rig>_low.prefab under Resources/Prefabs/4_Characters.
// Select the rig's prefab (high or _low) or its BoneAnimationData asset, then Tools > SmoothMoves > Build Rig (Ctrl+Alt+B),
// or right-click it in the Project window > SmoothMoves > Build Rig.
// Before building it saves the project and copies the data asset, both prefabs and MonoBehaviour/<Rig>_Resources to
// Library/RigBuildBackups/<Rig>_<time> (newest 5 per rig kept). The log goes to the Console and Temp/SmoothMovesRig.log.
public static class SmoothMovesRigBuildButton
{
	private const string PrefabRoot = "Assets/Resources/Prefabs/4_Characters";

	private const string BackupRoot = "Library/RigBuildBackups";

	private const int BackupsKept = 5;

	// Gear twin bones: in game a twin with art in a clip hides its gear bone there, whatever the visible flags say.
	private static readonly string[] TwinBones = { "HeadgearBody", "HeadgearBody_Back", "MainHandBody", "OffHandBody" };

	[MenuItem("Tools/SmoothMoves/Build Rig %&b", false, 0)]
	[MenuItem("Assets/SmoothMoves/Build Rig", false, 2000)]
	private static void BuildSelected()
	{
		if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
		{
			EditorUtility.DisplayDialog("Build Rig", "Leave play mode and wait for Unity to finish compiling/importing, then build.", "OK");
			return;
		}
		string rig;
		Object data;
		List<string> prefabs;
		string problem;
		if (!Resolve(Selection.activeObject, out rig, out data, out prefabs, out problem))
		{
			EditorUtility.DisplayDialog("Build Rig", problem, "OK");
			return;
		}
		var message = new StringBuilder();
		message.AppendLine("Full SmoothMoves build of " + rig + " from " + AssetDatabase.GetAssetPath(data) + ":");
		foreach (var prefab in prefabs)
		{
			message.AppendLine("  " + prefab);
		}
		message.AppendLine();
		message.AppendLine("Every clip is rebuilt. The project is saved and backed up to " + BackupRoot + " first.");
		var warnings = TwinWarnings(data);
		if (warnings.Count > 0)
		{
			message.AppendLine();
			message.AppendLine("Heads up: these twin bones have art but are hidden. In game they still draw, and their gear bone is hidden in that clip:");
			foreach (var warning in warnings)
			{
				message.AppendLine("  " + warning);
			}
		}
		if (!EditorUtility.DisplayDialog("Build Rig", message.ToString(), "Build", "Cancel"))
		{
			return;
		}

		AssetDatabase.SaveAssets();
		var log = new StringBuilder();
		var ok = true;
		try
		{
			log.AppendLine("backup: " + Backup(rig, data, prefabs));
			var builtData = new HashSet<Object>();
			for (var i = 0; i < prefabs.Count; i++)
			{
				EditorUtility.DisplayProgressBar("Build Rig", prefabs[i], (float)i / prefabs.Count);
				SmoothMovesRigRebuilder.Build(prefabs[i], builtData, log);
			}
			ok = prefabs.All(p => log.ToString().Contains(p + ": saved"));
		}
		catch (Exception e)
		{
			ok = false;
			log.AppendLine("EXCEPTION " + e);
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}
		var text = log.ToString();
		File.WriteAllText("Temp/SmoothMovesRig.log", text);
		if (ok)
		{
			Debug.Log("Build Rig: " + rig + " built\n" + text);
		}
		else
		{
			Debug.LogError("Build Rig: " + rig + " FAILED\n" + text);
		}
		var summary = string.Join("\n", text.Split('\n').Where(l => !l.Contains("skipped m_Bones") && !l.Contains("re-pointed")).ToArray()).Trim();
		EditorUtility.DisplayDialog("Build Rig", (ok ? rig + " built." : rig + " did NOT build cleanly; see the Console.") + "\n\n" + summary, "OK");
	}

	[MenuItem("Tools/SmoothMoves/Build Rig %&b", true)]
	[MenuItem("Assets/SmoothMoves/Build Rig", true)]
	private static bool CanBuildSelected()
	{
		string rig;
		Object data;
		List<string> prefabs;
		string problem;
		return Resolve(Selection.activeObject, out rig, out data, out prefabs, out problem);
	}

	// Selection (prefab, _low prefab or BoneAnimationData) -> rig name, its data asset and the prefabs to build.
	private static bool Resolve(Object selected, out string rig, out Object data, out List<string> prefabs, out string problem)
	{
		rig = null;
		data = null;
		prefabs = new List<string>();
		problem = "Select a rig prefab (e.g. MinionPig) or its SmoothMoves animation data asset.";
		if (selected == null)
		{
			return false;
		}
		var path = AssetDatabase.GetAssetPath(selected);
		if (string.IsNullOrEmpty(path))
		{
			return false;
		}
		if (path.EndsWith(".prefab"))
		{
			rig = Path.GetFileNameWithoutExtension(path);
			if (rig.EndsWith("_low"))
			{
				rig = rig.Substring(0, rig.Length - 4);
			}
		}
		else if (selected.GetType().Name == "BoneAnimationData")
		{
			rig = selected.name;
		}
		else
		{
			return false;
		}
		// Custom copies (MyPrefabs/<Rig>.prefab) can share the rig's name: the real rig sits next to its _low sibling.
		var name = rig;
		var candidates = AssetDatabase.FindAssets(rig + " t:Prefab", new[] { PrefabRoot }).Select(AssetDatabase.GUIDToAssetPath).ToList();
		var high = candidates.Where(p => Path.GetFileNameWithoutExtension(p) == name)
			.OrderByDescending(p => File.Exists(Path.Combine(Path.GetDirectoryName(p), name + "_low.prefab")))
			.ThenBy(p => p.Contains("/MyPrefabs/")).FirstOrDefault();
		var low = high == null ? null : Path.Combine(Path.GetDirectoryName(high), rig + "_low.prefab").Replace('\\', '/');
		if (high != null)
		{
			prefabs.Add(high);
			if (File.Exists(low))
			{
				prefabs.Add(low);
			}
		}
		if (prefabs.Count == 0)
		{
			problem = "No " + rig + ".prefab under " + PrefabRoot + ".";
			return false;
		}
		data = selected.GetType().Name == "BoneAnimationData" ? selected : null;
		foreach (var prefab in prefabs)
		{
			if (data != null)
			{
				break;
			}
			var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
			var bones = root == null ? null : root.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c != null && c.GetType().Name == "BoneAnimation");
			var field = bones == null ? null : bones.GetType().GetField("animationData", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			data = field == null ? null : field.GetValue(bones) as Object;
		}
		if (data == null)
		{
			problem = rig + "'s prefabs have no SmoothMoves animation data assigned.";
			return false;
		}
		return true;
	}

	private static List<string> TwinWarnings(Object data)
	{
		var warnings = new List<string>();
		var so = new SerializedObject(data);
		var boneList = so.FindProperty("boneDataList");
		var names = new List<string>();
		for (var i = 0; i < boneList.arraySize; i++)
		{
			names.Add(boneList.GetArrayElementAtIndex(i).FindPropertyRelative("boneName").stringValue);
		}
		var clips = so.FindProperty("animationClips");
		for (var c = 0; c < clips.arraySize; c++)
		{
			var clip = clips.GetArrayElementAtIndex(c);
			var bones = clip.FindPropertyRelative("bones");
			for (var b = 0; b < bones.arraySize; b++)
			{
				var bone = bones.GetArrayElementAtIndex(b);
				var index = bone.FindPropertyRelative("boneDataIndex").intValue;
				if (index < 0 || index >= names.Count || !TwinBones.Contains(names[index]))
				{
					continue;
				}
				var visible = bone.FindPropertyRelative("visible");
				if (visible == null || visible.boolValue)
				{
					continue;
				}
				var keyframes = bone.FindPropertyRelative("keyframes");
				for (var k = 0; k < keyframes.arraySize; k++)
				{
					var texture = keyframes.GetArrayElementAtIndex(k).FindPropertyRelative("textureGUID");
					if (texture != null && !string.IsNullOrEmpty(texture.stringValue))
					{
						warnings.Add(clip.FindPropertyRelative("animationName").stringValue + ": " + names[index]);
						break;
					}
				}
			}
		}
		return warnings;
	}

	private static string Backup(string rig, Object data, List<string> prefabs)
	{
		var folder = Path.Combine(BackupRoot, rig + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
		Directory.CreateDirectory(folder);
		var files = new List<string>(prefabs) { AssetDatabase.GetAssetPath(data) };
		foreach (var file in files)
		{
			File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true);
		}
		var resources = Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(data)), rig + "_Resources");
		if (Directory.Exists(resources))
		{
			var target = Path.Combine(folder, rig + "_Resources");
			Directory.CreateDirectory(target);
			foreach (var file in Directory.GetFiles(resources))
			{
				File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
			}
		}
		foreach (var old in Directory.GetDirectories(BackupRoot, rig + "_????-??-??_??-??-??").OrderByDescending(d => d).Skip(BackupsKept))
		{
			Directory.Delete(old, true);
		}
		return folder;
	}
}
