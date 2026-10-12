using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sets GameObjects active/inactive in the open world map scene. Temp/WorldMapSetActive.request holds one
// "Root/Child/Path=1" (or =0) per line. Saves only if the scene had no unsaved changes before.
public static class WorldMapSetActive
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/WorldMapSetActive.request";

	private const string LogFile = "Temp/WorldMapSetActive.log";

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
		var requests = File.ReadAllLines(RequestFile).Where(l => l.Contains("=")).ToArray();
		File.Delete(RequestFile);
		var log = new StringBuilder();
		var scene = SceneManager.GetSceneByPath(ScenePath);
		if (!scene.isLoaded)
		{
			scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
		}
		var hadUnsavedChanges = scene.isDirty;
		foreach (var request in requests)
		{
			var path = request.Substring(0, request.LastIndexOf('=')).Trim();
			var active = request.Substring(request.LastIndexOf('=') + 1).Trim() == "1";
			var parts = path.Split('/');
			var root = scene.GetRootGameObjects().FirstOrDefault(r => r.name == parts[0]);
			var target = root == null ? null : parts.Length == 1 ? root.transform : root.transform.Find(path.Substring(parts[0].Length + 1));
			if (target == null)
			{
				log.AppendLine("not found: " + path);
				continue;
			}
			target.gameObject.SetActive(active);
			log.AppendLine((active ? "activated " : "deactivated ") + path);
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
		File.WriteAllText(LogFile, log.ToString());
	}
}
