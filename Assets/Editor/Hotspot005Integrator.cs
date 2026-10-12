using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Restores the cut hotspot_005_battleground on the world map between 004 and 006.
// Run from the menu, or touch Temp/Hotspot005Integrate.request with the editor open.
public static class Hotspot005Integrator
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/Hotspot005Integrate.request";

	private const string LogFile = "Temp/Hotspot005Integrate.log";

	private const string PathRequestFile = "Temp/Hotspot005WirePath.request";

	private const string HotspotsRoot = "WorldMap/2_Scenery/Zone_01/3_Hotspots";

	private const string NewName = "Hotspot_005_Battleground";

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
		if (File.Exists(RequestFile))
		{
			File.Delete(RequestFile);
			Integrate();
		}
		if (File.Exists(PathRequestFile))
		{
			File.Delete(PathRequestFile);
			WirePath();
		}
	}

	[MenuItem("Tools/World Map/Wire Path_005 Into Hotspot 005")]
	public static void WirePath()
	{
		s_Log.Length = 0;
		try
		{
			WirePathInternal();
		}
		catch (System.Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[Hotspot005Integrator]\n" + s_Log);
	}

	// Paths run from the previous hotspot to their own one; PathMovingService walks them 0 -> 1
	// when moving forward. A path drawn the other way is reversed (shape unchanged).
	private static void WirePathInternal()
	{
		var scene = SceneManager.GetSceneByPath(ScenePath);
		if (!scene.isLoaded)
		{
			scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
		}
		var hadUnsavedChanges = scene.isDirty;

		var hotspots = Find(scene, HotspotsRoot);
		var view004 = hotspots.Find("Hotspot_004_Battleground").GetComponent<HotSpotWorldMapViewBase>();
		var view005 = hotspots.Find(NewName).GetComponent<HotSpotWorldMapViewBase>();
		var path = Find(scene, "WorldMap/2_Scenery/Zone_01/6_Paths/Path_005");
		var lite = path.GetComponent<RageSplineLite>();
		var rage = path.GetComponent<RageSpline>();
		if (lite == null || rage == null)
		{
			Log("aborted: Path_005 needs both RageSpline and RageSplineLite");
			return;
		}

		lite.CreateFromSpline();
		var start = lite.GetPositionWorldSpace(0f);
		var end = lite.GetPositionWorldSpace(1f);
		Log("Path_005 start " + start + ", end " + end + " | 004 " + view004.transform.position + ", 005 " + view005.transform.position);
		if (Distance2D(start, view005.transform.position) + Distance2D(end, view004.transform.position) < Distance2D(start, view004.transform.position) + Distance2D(end, view005.transform.position))
		{
			var points = rage.spline.points;
			System.Array.Reverse(points);
			foreach (var point in points)
			{
				var inCtrl = point.inCtrl;
				point.inCtrl = point.outCtrl;
				point.outCtrl = inCtrl;
			}
			rage.spline.points = points;
			rage.RefreshMeshInEditor(true, true, true);
			EditorUtility.SetDirty(rage);
			lite.CreateFromSpline();
			Log("reversed Path_005: now start " + lite.GetPositionWorldSpace(0f) + ", end " + lite.GetPositionWorldSpace(1f));
		}
		else
		{
			Log("Path_005 already runs 004 -> 005");
		}
		EditorUtility.SetDirty(lite);

		var data005 = new SerializedObject(view005);
		data005.FindProperty("m_litePath").objectReferenceValue = lite;
		data005.ApplyModifiedPropertiesWithoutUndo();
		Log("005 path: " + view005.m_litePath.name);

		EditorSceneManager.MarkSceneDirty(scene);
		if (hadUnsavedChanges)
		{
			Log("not saved: the scene already had unsaved changes, save it in the editor");
			return;
		}
		EditorSceneManager.SaveScene(scene);
		Log("saved " + ScenePath);
	}

	private static float Distance2D(Vector3 a, Vector3 b)
	{
		return Vector2.Distance(a, b);
	}

	[MenuItem("Tools/World Map/Integrate Hotspot 005")]
	public static void Integrate()
	{
		s_Log.Length = 0;
		try
		{
			IntegrateInternal();
		}
		catch (System.Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[Hotspot005Integrator]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static void IntegrateInternal()
	{
		var scene = SceneManager.GetSceneByPath(ScenePath);
		if (!scene.isLoaded)
		{
			scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
		}
		// Don't save someone's in-progress edits along with ours; leave the scene dirty instead.
		var hadUnsavedChanges = scene.isDirty;

		var hotspots = Find(scene, HotspotsRoot);
		var legacy = hotspots.Find("Hotspot_005_Battleground_Legacy");
		var view004 = hotspots.Find("Hotspot_004_Battleground").GetComponent<HotSpotWorldMapViewBase>();
		var view006 = Find(scene, "WorldMap/2_Scenery/Zone_02/3_Hotspots/Hotspot_006_Battleground").GetComponent<HotSpotWorldMapViewBase>();
		if (hotspots.Find(NewName) != null)
		{
			Log("aborted: " + NewName + " already exists");
			return;
		}
		if (!view004.m_outgoingHotSpots.Contains(view006) || view006.m_previousHotSpot != view004)
		{
			Log("aborted: 004 -> 006 link not found, scene was changed by hand");
			return;
		}

		// Clone 004 so 005 gets the same battle flag proxy, collider, tap handlers and stand positions.
		var clone = Object.Instantiate(view004.gameObject, hotspots);
		clone.name = NewName;
		clone.transform.localPosition = legacy.localPosition;
		clone.transform.SetSiblingIndex(view004.transform.GetSiblingIndex() + 1);

		// 004's story sequence (Yellow taken to prison) belongs to 004 only.
		Object.DestroyImmediate(clone.GetComponent<ExecuteActionTree>());

		var view005 = clone.GetComponent<HotSpotWorldMapViewBase>();
		var data005 = new SerializedObject(view005);
		data005.FindProperty("m_nameId").stringValue = "hotspot_005_battleground";
		data005.FindProperty("m_previousHotSpot").objectReferenceValue = view004;
		data005.FindProperty("m_litePath").objectReferenceValue = null;
		var outgoing005 = data005.FindProperty("m_outgoingHotSpots");
		outgoing005.ClearArray();
		outgoing005.InsertArrayElementAtIndex(0);
		outgoing005.GetArrayElementAtIndex(0).objectReferenceValue = view006;
		data005.ApplyModifiedPropertiesWithoutUndo();

		var data004 = new SerializedObject(view004);
		var outgoing004 = data004.FindProperty("m_outgoingHotSpots");
		for (var i = 0; i < outgoing004.arraySize; i++)
		{
			if (outgoing004.GetArrayElementAtIndex(i).objectReferenceValue == view006)
			{
				outgoing004.GetArrayElementAtIndex(i).objectReferenceValue = view005;
			}
		}
		data004.ApplyModifiedPropertiesWithoutUndo();

		var data006 = new SerializedObject(view006);
		data006.FindProperty("m_previousHotSpot").objectReferenceValue = view005;
		data006.ApplyModifiedPropertiesWithoutUndo();

		var proxy = new SerializedObject(view005).FindProperty("m_ProxyAssetBase").objectReferenceValue as Component;
		Log("created " + NewName + " at local " + clone.transform.localPosition + " (world " + clone.transform.position + ")");
		Log("proxy: " + (proxy == null ? "null" : proxy.transform.IsChildOf(clone.transform) ? "own child (ok)" : "STILL POINTS AT 004"));
		Log("004 outgoing: " + Names(view004));
		Log("005 previous: " + view005.m_previousHotSpot.name + ", outgoing: " + Names(view005) + ", path: none");
		Log("006 previous: " + view006.m_previousHotSpot.name);

		EditorSceneManager.MarkSceneDirty(scene);
		if (hadUnsavedChanges)
		{
			Log("not saved: the scene already had unsaved changes, save it in the editor");
			return;
		}
		EditorSceneManager.SaveScene(scene);
		Log("saved " + ScenePath);
	}

	private static string Names(HotSpotWorldMapViewBase view)
	{
		var names = new StringBuilder();
		foreach (var hotspot in view.m_outgoingHotSpots)
		{
			names.Append(hotspot == null ? "null" : hotspot.name).Append(' ');
		}
		return names.ToString().Trim();
	}

	private static Transform Find(Scene scene, string path)
	{
		var parts = path.Split('/');
		foreach (var root in scene.GetRootGameObjects())
		{
			if (root.name == parts[0])
			{
				var found = parts.Length == 1 ? root.transform : root.transform.Find(path.Substring(parts[0].Length + 1));
				if (found != null)
				{
					return found;
				}
			}
		}
		throw new System.Exception("not found in scene: " + path);
	}
}
