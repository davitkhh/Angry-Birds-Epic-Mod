using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sets ActionNode fields in a world map ActionTree. Temp/ActionNodeEdit.request: first line is the tree's GameObject
// path, then one "nodeId field=value" per line (int, float, bool or string fields; "?" as value only reports it).
// Saves only if the scene had no unsaved changes before.
public static class ActionNodeEdit
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/ActionNodeEdit.request";

	private const string LogFile = "Temp/ActionNodeEdit.log";

	[InitializeOnLoadMethod]
	private static void WatchForRequests()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		var lines = File.ReadAllLines(RequestFile).Where(l => l.Trim().Length > 0).ToArray();
		File.Delete(RequestFile);
		var log = new StringBuilder();
		try
		{
			var scene = SceneManager.GetSceneByPath(ScenePath);
			var hadUnsavedChanges = scene.isDirty;
			var parts = lines[0].Trim().Split('/');
			var root = scene.GetRootGameObjects().First(r => r.name == parts[0]);
			var tree = root.transform.Find(lines[0].Trim().Substring(parts[0].Length + 1)).GetComponent<ActionTree>();
			var changed = false;
			foreach (var line in lines.Skip(1))
			{
				var space = line.IndexOf(' ');
				var id = int.Parse(line.Substring(0, space));
				var assignment = line.Substring(space + 1);
				var field = assignment.Substring(0, assignment.IndexOf('='));
				var value = assignment.Substring(assignment.IndexOf('=') + 1);
				var node = tree.GetNodeByID(id);
				var info = typeof(ActionNode).GetField(field, BindingFlags.Public | BindingFlags.Instance);
				var before = info.GetValue(node);
				if (value != "?")
				{
					info.SetValue(node, Convert.ChangeType(value, info.FieldType, System.Globalization.CultureInfo.InvariantCulture));
					changed = true;
				}
				log.AppendLine("node " + id + " (" + node.type + " " + node.objectName + ") " + field + ": " + before + (value != "?" ? " -> " + info.GetValue(node) : string.Empty));
			}
			if (changed)
			{
				EditorUtility.SetDirty(tree);
				EditorSceneManager.MarkSceneDirty(scene);
				if (hadUnsavedChanges)
				{
					log.AppendLine("not saved: the scene already had unsaved changes, save it in the editor");
				}
				else
				{
					EditorSceneManager.SaveScene(scene);
					log.AppendLine("saved " + ScenePath);
				}
			}
		}
		catch (Exception e)
		{
			log.AppendLine("error: " + e);
		}
		File.WriteAllText(LogFile, log.ToString());
	}
}
