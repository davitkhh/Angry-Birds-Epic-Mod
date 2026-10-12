using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Puts the golden pig cloud after the pirate hotspot by the pig prison (006 -> 006_01 -> cloud) and builds
// Path_GoldenPigCloud, a jump path (m_jumpPath) drawn as a straight line from 006_01 to the cloud (the jump animation
// supplies the arc). The line is only drawn when the path is created; rerunning just redoes the links. After reshaping
// its RageSpline in the editor, use the Lite component's "Create from RageSpline". The cloud unlocks past 006_01
// (m_unlocksPastPrevious): its own requirement (castle 1) opens Path_006_01 and the jump while 006_01 stays story-locked.
// Menu item, or touch Temp/GoldenPigCloudJumpPath.request with the editor open.
public static class GoldenPigCloudJumpPath
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string Request = "Temp/GoldenPigCloudJumpPath.request";

	private const string LogFile = "Temp/GoldenPigCloudJumpPath.log";

	private const string Hotspots = "WorldMap/2_Scenery/Zone_02/3_Hotspots/";

	private const string Paths = "WorldMap/2_Scenery/Zone_02/6_Paths/";

	private const string PathName = "Path_GoldenPigCloud";

	// A RageSpline path with the ground path style, copied for the new path's settings.
	private const string TemplatePath = "Path_008_03";

	private static readonly StringBuilder s_Log = new StringBuilder();

	[InitializeOnLoadMethod]
	private static void WatchForRequests()
	{
		EditorApplication.update -= PollRequest;
		EditorApplication.update += PollRequest;
	}

	private static void PollRequest()
	{
		if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		if (File.Exists(Request))
		{
			File.Delete(Request);
			Build();
		}
	}

	[MenuItem("Tools/World Map/Build Golden Pig Cloud Jump Path")]
	public static void Build()
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
			BuildInternal(scene);
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
		catch (Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[GoldenPigCloudJumpPath]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static void BuildInternal(Scene scene)
	{
		var camp = FindHotspot(scene, "Hotspot_001_Camp");
		var pirate = Find(scene, Hotspots + "Hotspot_006_01_Battleground").GetComponent<HotSpotWorldMapViewBase>();
		var cloud = Find(scene, Hotspots + "Hotspot_GoldenPigCloud").GetComponent<HotSpotWorldMapViewBase>();
		var paths = Find(scene, Paths.TrimEnd('/'));

		var pathObject = paths.Find(PathName);
		if (pathObject == null)
		{
			var template = paths.Find(TemplatePath);
			pathObject = UnityEngine.Object.Instantiate(template.gameObject, paths).transform;
			pathObject.name = PathName;
			pathObject.localPosition = template.localPosition;
			// The copy would share the template's mesh, and RageSpline rebuilds whatever mesh it holds.
			pathObject.GetComponent<MeshFilter>().sharedMesh = new Mesh();
			Log("created " + PathName + " from " + TemplatePath);
			DrawStraightLine(pathObject, pirate, cloud);
		}
		var lite = pathObject.GetComponent<RageSplineLite>();
		Log(PathName + ": " + lite.GetPositionWorldSpace(0f) + " -> " + lite.GetPositionWorldSpace(1f) + " (pirate " + pirate.transform.position + ", cloud " + cloud.transform.position + ")");

		var cloudData = new SerializedObject(cloud);
		cloudData.FindProperty("m_previousHotSpot").objectReferenceValue = pirate;
		cloudData.FindProperty("m_litePath").objectReferenceValue = lite;
		cloudData.FindProperty("m_jumpPath").boolValue = true;
		cloudData.FindProperty("m_unlocksPastPrevious").boolValue = true;
		cloudData.ApplyModifiedPropertiesWithoutUndo();

		var campData = new SerializedObject(camp);
		RemoveFromList(campData.FindProperty("m_outgoingHotSpots"), cloud);
		campData.ApplyModifiedPropertiesWithoutUndo();

		var pirateData = new SerializedObject(pirate);
		var outgoing = pirateData.FindProperty("m_outgoingHotSpots");
		RemoveFromList(outgoing, cloud);
		outgoing.InsertArrayElementAtIndex(outgoing.arraySize);
		outgoing.GetArrayElementAtIndex(outgoing.arraySize - 1).objectReferenceValue = cloud;
		pirateData.ApplyModifiedPropertiesWithoutUndo();
		Log("linked " + camp.name + " -/-> cloud, " + pirate.name + " -> " + cloud.name + " (jump)");
	}

	private static void DrawStraightLine(Transform pathObject, HotSpotWorldMapViewBase pirate, HotSpotWorldMapViewBase cloud)
	{
		var rage = pathObject.GetComponent<RageSpline>();
		var lite = pathObject.GetComponent<RageSplineLite>();
		// A straight line from the pirate hotspot to the cloud; the birds' jump animation supplies the arc.
		var start = pirate.transform.position;
		var end = cloud.transform.position;
		start.z = end.z = pathObject.position.z;
		var third = (end - start) / 3f;
		rage.ClearPoints();
		rage.AddPointWorldSpace(0, start, -third, third, 1f, false);
		rage.AddPointWorldSpace(1, end, -third, third, 1f, false);
		pathObject.gameObject.SetActive(true);
		rage.RefreshMeshInEditor(true, true, true);
		EditorUtility.SetDirty(rage);
		lite.CreateFromSpline();
		EditorUtility.SetDirty(lite);
		// Paths are saved hidden; the cloud shows its path when it unlocks.
		pathObject.gameObject.SetActive(false);
	}

	private static void RemoveFromList(SerializedProperty list, UnityEngine.Object item)
	{
		for (var i = list.arraySize - 1; i >= 0; i--)
		{
			if (list.GetArrayElementAtIndex(i).objectReferenceValue == item)
			{
				list.GetArrayElementAtIndex(i).objectReferenceValue = null;
				list.DeleteArrayElementAtIndex(i);
			}
		}
	}

	private static HotSpotWorldMapViewBase FindHotspot(Scene scene, string name)
	{
		foreach (var root in scene.GetRootGameObjects())
		{
			foreach (var hotspot in root.GetComponentsInChildren<HotSpotWorldMapViewBase>(true))
			{
				if (hotspot.name == name)
				{
					return hotspot;
				}
			}
		}
		throw new Exception("hotspot not found in scene: " + name);
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
