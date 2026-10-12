using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Read-only: dumps world map hotspot links for objects whose name contains Temp/WorldMapProbe.request's text.
public static class WorldMapProbe
{
	private const string RequestFile = "Temp/WorldMapProbe.request";

	private const string LogFile = "Temp/WorldMapProbe.log";

	[InitializeOnLoadMethod]
	private static void WatchForRequests()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isPlaying)
		{
			return;
		}
		var filters = File.ReadAllText(RequestFile).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
		File.Delete(RequestFile);
		var log = new StringBuilder();
		for (var i = 0; i < SceneManager.sceneCount; i++)
		{
			var scene = SceneManager.GetSceneAt(i);
			log.AppendLine("scene " + scene.path + " loaded=" + scene.isLoaded + " dirty=" + scene.isDirty);
			if (!scene.isLoaded)
			{
				continue;
			}
			foreach (var t in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
			{
				if (!filters.Any(f => t.name.Contains(f)))
				{
					continue;
				}
				var line = new StringBuilder(t.name + " active=" + t.gameObject.activeSelf + " world=" + t.position + " path=" + GetPath(t));
				var view = t.GetComponent<HotSpotWorldMapViewBase>();
				if (view != null)
				{
					line.Append(" | nameId=" + view.m_nameId + " prev=" + (view.m_previousHotSpot ? view.m_previousHotSpot.name : "-") + " litePath=" + (view.m_litePath ? view.m_litePath.name : "-") + " out=[" + string.Join(",", (view.m_outgoingHotSpots ?? new System.Collections.Generic.List<HotSpotWorldMapViewBase>()).Select(o => o ? o.name : "null").ToArray()) + "]");
				}
				var lite = t.GetComponent<RageSplineLite>();
				if (lite != null)
				{
					line.Append(" | lite start=" + lite.GetPositionWorldSpace(0f) + " end=" + lite.GetPositionWorldSpace(1f) + " points=" + lite.GetPointCount());
				}
				log.AppendLine(line.ToString());
			}
		}
		File.WriteAllText(LogFile, log.ToString());
	}

	private static string GetPath(Transform t)
	{
		return t.parent == null ? t.name : GetPath(t.parent) + "/" + t.name;
	}
}
