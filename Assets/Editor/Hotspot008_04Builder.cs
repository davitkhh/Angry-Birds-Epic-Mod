using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 2026-11-08: undoes the 008/009 fork (paths and hotspot positions back to the committed scene) and adds
// Hotspot_008_04_Battleground, unlinked (never unlocks yet), where 008 stood during the fork.
// Temp/Hotspot008_04.request = build, Temp/Hotspot008_04Inspect.request = log only. Works on the live scene, never saves.
public static class Hotspot008_04Builder
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string HeadScenePath = "Assets/__Hotspot008_04_Head.unity";

	private const string NewName = "Hotspot_008_04_Battleground";

	private const string NewNameId = "hotspot_008_04_battleground";

	private static readonly Vector2 NewPosition = new Vector2(-1014.1f, -1450.1f);

	private static readonly string[] Paths = { "Path_008", "Path_008_01", "Path_009", "Path_010" };

	private static readonly string[] Hotspots = { "Hotspot_008_Battleground", "Hotspot_009_Battleground" };

	private static readonly StringBuilder s_Log = new StringBuilder();

	[InitializeOnLoadMethod]
	private static void Watch()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		var build = File.Exists("Temp/Hotspot008_04.request");
		var inspect = File.Exists("Temp/Hotspot008_04Inspect.request");
		if (!build && !inspect)
		{
			return;
		}
		File.Delete("Temp/Hotspot008_04.request");
		File.Delete("Temp/Hotspot008_04Inspect.request");
		s_Log.Length = 0;
		try
		{
			var scene = SceneManager.GetSceneByPath(ScenePath);
			Log("scene loaded=" + scene.isLoaded + " dirty=" + scene.isDirty);
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
			}
			if (inspect)
			{
				Inspect(scene);
			}
			else
			{
				Build(scene);
			}
		}
		catch (Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText("Temp/Hotspot008_04.log", s_Log.ToString());
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static T Find<T>(Scene scene, string name) where T : Component
	{
		return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).FirstOrDefault(c => c.name == name);
	}

	private static void Inspect(Scene scene)
	{
		foreach (var h in new[] { "Hotspot_007_Battleground", "Hotspot_008_Battleground", "Hotspot_008_02_Merchant", "Hotspot_008_03_ResourceNode", "Hotspot_008_04_Battleground", "Hotspot_008_05_Battleground", "Hotspot_009_Battleground" })
		{
			var v = Find<HotSpotWorldMapViewBase>(scene, h);
			Log(h + " " + (v ? v.transform.position.ToString("F2") + " prev=" + (v.m_previousHotSpot ? v.m_previousHotSpot.name : "-") + " out=" + string.Join(",", v.m_outgoingHotSpots.Select(o => o ? o.name : "null")) + " path=" + (v.m_litePath ? v.m_litePath.name : "-") : "missing"));
		}
		var view = Find<HotSpotWorldMapViewBase>(scene, "Hotspot_008_Battleground");
		foreach (var t in view.GetComponentsInChildren<Transform>(true))
		{
			Log(AnimationUtility.CalculateTransformPath(t, view.transform) + " active=" + t.gameObject.activeSelf + " [" + string.Join(",", t.GetComponents<Component>().Select(c => c ? c.GetType().Name : "null")) + "]");
		}
		foreach (var c in view.GetComponentsInChildren<Component>(true).Where(c => c != null))
		{
			var it = new SerializedObject(c).GetIterator();
			while (it.Next(true))
			{
				if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null && it.name != "m_Script" && it.name != "m_GameObject")
				{
					var target = it.objectReferenceValue;
					var go = target is Component ? ((Component)target).gameObject : target as GameObject;
					if (go != null && go.scene.IsValid() && !go.transform.IsChildOf(view.transform))
					{
						Log("outside ref: " + c.GetType().Name + " on " + c.name + " ." + it.propertyPath + " -> " + go.name);
					}
				}
			}
		}
	}

	private static void Build(Scene scene)
	{
		// The committed scene, opened beside the live one to copy from.
		File.WriteAllBytes(HeadScenePath, RunGit("show HEAD:" + ScenePath));
		AssetDatabase.ImportAsset(HeadScenePath);
		var head = EditorSceneManager.OpenScene(HeadScenePath, OpenSceneMode.Additive);
		try
		{
			foreach (var name in Paths)
			{
				var live = Find<RageSplineLite>(scene, name);
				var was = Find<RageSplineLite>(head, name);
				EditorUtility.CopySerialized(was, live);
				var wasRage = was.GetComponent<RageSpline>();
				var rages = live.GetComponents<RageSpline>();
				if (wasRage == null)
				{
					foreach (var rage in rages)
					{
						UnityEngine.Object.DestroyImmediate(rage);
					}
					live.GetComponent<MeshFilter>().sharedMesh = was.GetComponent<MeshFilter>().sharedMesh;
					Log(name + ": RageSpline removed, mesh " + AssetDatabase.GetAssetPath(live.GetComponent<MeshFilter>().sharedMesh));
				}
				else
				{
					live.gameObject.SetActive(true);
					rages[0].spline = wasRage.spline.Clone();
					rages[0].Mfilter = live.GetComponent<MeshFilter>();
					rages[0].RefreshMeshInEditor(true, true, true);
					EditorUtility.SetDirty(rages[0]);
					Log(name + ": spline restored (" + rages[0].GetPointCount() + " points)");
				}
				live.gameObject.SetActive(was.gameObject.activeSelf);
				EditorUtility.SetDirty(live);
				EditorUtility.SetDirty(live.GetComponent<MeshFilter>());
				Log(name + " " + live.GetPositionWorldSpace(0f) + " -> " + live.GetPositionWorldSpace(1f) + " active=" + live.gameObject.activeSelf);
			}
			foreach (var name in Hotspots)
			{
				var live = Find<HotSpotWorldMapViewBase>(scene, name);
				var was = Find<HotSpotWorldMapViewBase>(head, name);
				live.transform.localPosition = was.transform.localPosition;
				EditorUtility.SetDirty(live.transform);
				Log(name + " back at " + live.transform.position);
			}
		}
		finally
		{
			EditorSceneManager.CloseScene(head, true);
			AssetDatabase.DeleteAsset(HeadScenePath);
		}

		var source = Find<HotSpotWorldMapViewBase>(scene, "Hotspot_008_Battleground");
		var existing = Find<HotSpotWorldMapViewBase>(scene, NewName);
		if (existing != null)
		{
			UnityEngine.Object.DestroyImmediate(existing.gameObject);
		}
		var copy = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
		copy.name = NewName;
		copy.transform.position = new Vector3(NewPosition.x, NewPosition.y, source.transform.position.z);
		var view = copy.GetComponent<HotSpotWorldMapViewBase>();
		var data = new SerializedObject(view);
		data.FindProperty("m_nameId").stringValue = NewNameId;
		data.FindProperty("m_previousHotSpot").objectReferenceValue = null;
		data.FindProperty("m_litePath").objectReferenceValue = null;
		data.FindProperty("m_outgoingHotSpots").ClearArray();
		data.ApplyModifiedPropertiesWithoutUndo();
		// 008's story cutscene (McCool's shop) stays with 008.
		foreach (var tree in copy.GetComponents<ExecuteActionTree>())
		{
			UnityEngine.Object.DestroyImmediate(tree);
		}
		Log("created " + NewName + " at " + copy.transform.position + " (unlinked)");
		EditorSceneManager.MarkSceneDirty(scene);
		Log("not saved: save the scene in the editor");
	}

	private static byte[] RunGit(string args)
	{
		var info = new System.Diagnostics.ProcessStartInfo("git", args) { RedirectStandardOutput = true, UseShellExecute = false };
		using (var process = System.Diagnostics.Process.Start(info))
		using (var output = new MemoryStream())
		{
			process.StandardOutput.BaseStream.CopyTo(output);
			process.WaitForExit();
			return output.ToArray();
		}
	}
}
