using System.IO;
using UnityEditor;
using UnityEngine;

// Play mode testing aid: Temp/AreaIntroReplay.request (contents: an intro key, e.g. cobalt_plateaus, or empty) replays that
// world map area intro (title, pan and theme) on the open world map.
public static class AreaIntroReplay
{
	private const string RequestFile = "Temp/AreaIntroReplay.request";

	[InitializeOnLoadMethod]
	private static void Watch()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!EditorApplication.isPlaying || !File.Exists(RequestFile))
		{
			return;
		}
		var key = File.ReadAllText(RequestFile).Trim();
		File.Delete(RequestFile);
		var state = DIContainerInfrastructure.LocationStateMgr as WorldMapStateMgr;
		if (state == null)
		{
			Debug.LogWarning("[AreaIntroReplay] not on the world map");
			return;
		}
		state.StartCoroutine(WorldMapAreaIntro.Replay(string.IsNullOrEmpty(key) ? "cobalt_plateaus" : key));
	}

	[MenuItem("Tools/World Map/Replay Cobalt Plateaus Intro")]
	private static void Menu()
	{
		File.WriteAllText(RequestFile, "cobalt_plateaus");
	}
}
