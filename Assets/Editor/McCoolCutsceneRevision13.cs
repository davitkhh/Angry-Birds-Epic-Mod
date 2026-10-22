using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Thirteenth revision of the McCool robbery cutscene: McCool stands on the grass patch left of hotspot 008's post
// (Marker_McCool moved; second spot, 2026-10-19), where he stays in view for the whole sequence, and the rogue's lunge (revision 12's MovePath)
// is re-arced from Marker_RogueLedgeBase onto the new spot. Re-run after moving Marker_McCool by hand, with
// MoveMarker off, to only re-arc the lunge. Menu item, or Temp/McCoolCutsceneRevision13.request.
public static class McCoolCutsceneRevision13
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/McCoolCutsceneRevision13.request";

	private const string LogFile = "Temp/McCoolCutsceneRevision13.log";

	private const string SequencePath = "WorldMap/0_StorySequences/WS_03_McCoolShopOpen_Sequence";

	private static readonly Vector2 McCoolSpot = new Vector2(-1028f, -1458f);

	// How far the root's arc rises above the higher of take-off and landing. Action_Attack_2's own hop (Body +80 at
	// frame 10, 32 world units at the map's 0.4x) comes on top (revision 15).
	private const float ArcHeight = 20f;

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
		// Request text "rearc": keep Marker_McCool where it is.
		var moveMarker = File.ReadAllText(RequestFile).Trim() != "rearc";
		File.Delete(RequestFile);
		Revise(moveMarker);
	}

	[MenuItem("Tools/World Map/Revise McCool Robbery Cutscene (13)")]
	public static void ReviseMenu()
	{
		Revise(true);
	}

	[MenuItem("Tools/World Map/Re-arc McCool Cutscene Rogue Lunge")]
	public static void RearcMenu()
	{
		Revise(false);
	}

	public static void Revise(bool moveMarker)
	{
		s_Log.Length = 0;
		try
		{
			var scene = SceneManager.GetSceneByPath(ScenePath);
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
			}
			var hadUnsavedChanges = scene.isDirty;
			if (ReviseInternal(scene, moveMarker))
			{
				EditorSceneManager.MarkSceneDirty(scene);
				if (hadUnsavedChanges)
				{
					Log("not saved: the scene already had unsaved changes, save it in the editor");
				}
				else
				{
					EditorSceneManager.SaveScene(scene);
					Log("saved " + ScenePath);
				}
			}
		}
		catch (Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[McCoolCutsceneRevision13]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static bool ReviseInternal(Scene scene, bool moveMarker)
	{
		var sequence = Find(scene, SequencePath);
		var tree = sequence.GetComponent<ActionTree>();
		var baseMarker = sequence.Find("Marker_RogueLedgeBase");
		var landing = sequence.Find("Marker_McCool");
		var lunge = tree.nodes.FirstOrDefault(n => n.type == NodeType.MovePath && n.objectName == "Rogue");
		if (baseMarker == null || landing == null || lunge == null)
		{
			Log("Marker_RogueLedgeBase, Marker_McCool or the rogue's lunge (run revision 12 first) not found; unchanged");
			return false;
		}
		if (moveMarker)
		{
			var old = landing.position;
			landing.position = new Vector3(McCoolSpot.x, McCoolSpot.y, old.z);
			EditorUtility.SetDirty(landing);
			Log("Marker_McCool " + old + " -> " + landing.position);
		}
		var start = baseMarker.position;
		var end = landing.position;
		var top = Mathf.Max(start.y, end.y) + ArcHeight;
		var points = new[]
		{
			start,
			// Rise early: the ledge edge is just past the take-off.
			new Vector3(Mathf.Lerp(start.x, end.x, 0.2f), Mathf.Lerp(start.y, top, 0.8f), Mathf.Lerp(start.z, end.z, 0.2f)),
			new Vector3(Mathf.Lerp(start.x, end.x, 0.55f), top, Mathf.Lerp(start.z, end.z, 0.55f)),
			new Vector3(Mathf.Lerp(start.x, end.x, 0.85f), Mathf.Lerp(end.y, top, 0.5f), Mathf.Lerp(start.z, end.z, 0.85f)),
			end
		};
		lunge.param = points.Select(Format).ToArray();
		Log("lunge node " + lunge.nodeID + " now " + string.Join(" ", lunge.param));
		EditorUtility.SetDirty(tree);
		return true;
	}

	private static string Format(Vector3 v)
	{
		return v.x.ToString("0.#", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.#", CultureInfo.InvariantCulture) + "," + v.z.ToString("0.#", CultureInfo.InvariantCulture);
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
