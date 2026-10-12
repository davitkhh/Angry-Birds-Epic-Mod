using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sets local positions in the world map scene. Temp/WorldMapMove.request: one "Root/Child/Path x y z" per line (use
// "_" to keep an axis). Saves only if the scene had no unsaved changes before.
public static class WorldMapMove
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	[InitializeOnLoadMethod]
	private static void Watch()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!File.Exists("Temp/WorldMapMove.request") || EditorApplication.isCompiling || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		var lines = File.ReadAllLines("Temp/WorldMapMove.request").Where(l => l.Trim().Length > 0).ToArray();
		File.Delete("Temp/WorldMapMove.request");
		var log = new System.Text.StringBuilder();
		try
		{
			var scene = SceneManager.GetSceneByPath(ScenePath);
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
			}
			var hadUnsavedChanges = scene.isDirty;
			foreach (var line in lines)
			{
				var parts = line.Trim().Split(' ');
				var path = parts[0];
				var first = path.Split('/')[0];
				var root = scene.GetRootGameObjects().First(r => r.name == first);
				var target = root.transform.Find(path.Substring(first.Length + 1));
				var p = target.localPosition;
				var before = p;
				for (var i = 0; i < 3; i++)
				{
					if (parts[i + 1] != "_")
					{
						p[i] = float.Parse(parts[i + 1], System.Globalization.CultureInfo.InvariantCulture);
					}
				}
				target.localPosition = p;
				EditorUtility.SetDirty(target);
				log.AppendLine(path + " local " + before + " -> " + p + " (world " + target.position + ")");
			}
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
		catch (System.Exception e)
		{
			log.AppendLine("error: " + e);
		}
		File.WriteAllText("Temp/WorldMapMove.log", log.ToString());
	}
}
