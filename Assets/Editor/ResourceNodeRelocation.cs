using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The pig prison resource node moved to after hotspot_008's merchant (Hotspot_008_03_ResourceNode) and the hotspot_008 one moved
// to after the first castle (Hotspot_014_01_ResourceNode). "Relocate" relinks both and reverts Path_006_01;
// "Wire Paths" hooks up Path_008_03 / Path_014_01 once they are drawn (reversing a path drawn backwards).
// Menu items, or touch Temp/ResourceNodeRelocate.request / Temp/ResourceNodeWirePaths.request with the editor open.
public static class ResourceNodeRelocation
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RelocateRequest = "Temp/ResourceNodeRelocate.request";

	private const string WireRequest = "Temp/ResourceNodeWirePaths.request";

	private const string LogFile = "Temp/ResourceNodeRelocation.log";

	private const string Hotspots = "WorldMap/2_Scenery/Zone_02/3_Hotspots/";

	private const string Paths = "WorldMap/2_Scenery/Zone_02/6_Paths/";

	// Path_006_01's baked mesh before the RageSpline was added to it.
	private const string OriginalPath00601Mesh = "Assets/Mesh/Mesh_693.asset";

	private static readonly StringBuilder s_Log = new StringBuilder();

	// node object, nameId, parent hotspot, path name
	private static readonly string[][] Nodes =
	{
		// Path_008_03 branches off the merchant stall, so the node follows the merchant (008 -> merchant -> node).
		new[] { "Hotspot_008_03_ResourceNode", "hotspot_008_03_resourcenode", "Hotspot_008_02_Merchant", "Path_008_03" },
		new[] { "Hotspot_014_01_ResourceNode", "hotspot_014_01_resourcenode", "Hotspot_014_Castle", "Path_014_01" }
	};

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
		if (File.Exists(RelocateRequest))
		{
			File.Delete(RelocateRequest);
			Relocate();
		}
		if (File.Exists(WireRequest))
		{
			File.Delete(WireRequest);
			WirePaths();
		}
	}

	[MenuItem("Tools/World Map/Relocate Resource Nodes")]
	public static void Relocate()
	{
		Run(RelocateInternal);
	}

	[MenuItem("Tools/World Map/Wire Resource Node Paths")]
	public static void WirePaths()
	{
		Run(WirePathsInternal);
	}

	private static void Run(Action<Scene> action)
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
			action(scene);
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
		Debug.Log("[ResourceNodeRelocation]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static void RelocateInternal(Scene scene)
	{
		var hotspots = Find(scene, Hotspots.TrimEnd('/'));
		// The old hotspot_008 node keeps its object; it now sits after the castle.
		var castleNode = hotspots.Find("Hotspot_008_01_ResourceNode");
		if (castleNode != null)
		{
			castleNode.name = "Hotspot_014_01_ResourceNode";
			Log("renamed Hotspot_008_01_ResourceNode -> Hotspot_014_01_ResourceNode");
		}

		foreach (var entry in Nodes)
		{
			var node = hotspots.Find(entry[0]).GetComponent<HotSpotWorldMapViewBase>();
			var parent = hotspots.Find(entry[2]).GetComponent<HotSpotWorldMapViewBase>();

			// Detach from every hotspot that still lists it (the pig prison node hung off 006, the 008 node off nothing).
			foreach (var other in hotspots.GetComponentsInChildren<HotSpotWorldMapViewBase>(true))
			{
				if (other != parent && other.m_outgoingHotSpots != null && other.m_outgoingHotSpots.Contains(node))
				{
					var data = new SerializedObject(other);
					RemoveFromList(data.FindProperty("m_outgoingHotSpots"), node);
					data.ApplyModifiedPropertiesWithoutUndo();
					Log(other.name + " no longer leads to " + node.name);
				}
			}

			var parentData = new SerializedObject(parent);
			var outgoing = parentData.FindProperty("m_outgoingHotSpots");
			if (!Enumerable.Range(0, outgoing.arraySize).Any(i => outgoing.GetArrayElementAtIndex(i).objectReferenceValue == node))
			{
				outgoing.InsertArrayElementAtIndex(outgoing.arraySize);
				outgoing.GetArrayElementAtIndex(outgoing.arraySize - 1).objectReferenceValue = node;
			}
			parentData.ApplyModifiedPropertiesWithoutUndo();

			// Own save id (the pig prison node shared hotspot_021_01's). No path until one is drawn: walk in a straight line.
			var nodeData = new SerializedObject(node);
			nodeData.FindProperty("m_nameId").stringValue = entry[1];
			nodeData.FindProperty("m_previousHotSpot").objectReferenceValue = parent;
			nodeData.FindProperty("m_litePath").objectReferenceValue = null;
			nodeData.ApplyModifiedPropertiesWithoutUndo();
			Log(node.name + ": nameId " + entry[1] + ", after " + parent.name + ", no path (draw " + entry[3] + ")");
		}

		RevertPath00601(scene);
	}

	private static void RevertPath00601(Scene scene)
	{
		var path = Find(scene, Paths + "Path_006_01");
		var rage = path.GetComponent<RageSpline>();
		if (rage == null)
		{
			Log("Path_006_01 has no RageSpline, left as is");
			return;
		}
		UnityEngine.Object.DestroyImmediate(rage);
		var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(OriginalPath00601Mesh);
		path.GetComponent<MeshFilter>().sharedMesh = mesh;
		EditorUtility.SetDirty(path.GetComponent<MeshFilter>());
		var lite = path.GetComponent<RageSplineLite>();
		Log("reverted Path_006_01: removed the added RageSpline, mesh back to " + OriginalPath00601Mesh + " (" + (mesh != null) + "), walk spline " + lite.GetPointCount() + " points " + lite.GetPositionWorldSpace(0f) + " -> " + lite.GetPositionWorldSpace(1f));
	}

	private static void WirePathsInternal(Scene scene)
	{
		var hotspots = Find(scene, Hotspots.TrimEnd('/'));
		var paths = Find(scene, Paths.TrimEnd('/'));
		foreach (var entry in Nodes)
		{
			var pathObject = paths.Find(entry[3]);
			if (pathObject == null)
			{
				Log(entry[3] + " not found, skipped");
				continue;
			}
			var node = hotspots.Find(entry[0]).GetComponent<HotSpotWorldMapViewBase>();
			var parent = hotspots.Find(entry[2]);
			var lite = pathObject.GetComponent<RageSplineLite>();
			var rage = pathObject.GetComponent<RageSpline>();
			if (lite == null)
			{
				Log(entry[3] + " has no RageSplineLite, skipped");
				continue;
			}
			if (rage != null)
			{
				lite.CreateFromSpline();
			}
			// Paths start at the previous hotspot; PathMovingService walks them 0 -> 1 going forward.
			var start = lite.GetPositionWorldSpace(0f);
			var end = lite.GetPositionWorldSpace(1f);
			if (Distance2D(start, node.transform.position) + Distance2D(end, parent.position) < Distance2D(start, parent.position) + Distance2D(end, node.transform.position))
			{
				if (rage == null)
				{
					Log(entry[3] + " runs " + node.name + " -> " + parent.name + " and has no RageSpline to reverse; skipped");
					continue;
				}
				var points = rage.spline.points;
				Array.Reverse(points);
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
				Log("reversed " + entry[3]);
			}
			EditorUtility.SetDirty(lite);
			var nodeData = new SerializedObject(node);
			nodeData.FindProperty("m_litePath").objectReferenceValue = lite;
			nodeData.ApplyModifiedPropertiesWithoutUndo();
			Log(node.name + " path: " + entry[3] + " " + lite.GetPositionWorldSpace(0f) + " -> " + lite.GetPositionWorldSpace(1f) + " (" + parent.name + " " + parent.position + ", node " + node.transform.position + ")");
		}
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

	private static float Distance2D(Vector3 a, Vector3 b)
	{
		return Vector2.Distance(a, b);
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
