using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Read-only: Zone_02 hotspots (links, paths) and paths (RageSpline/Lite ends, nearest hotspots). Temp/PathMatchProbe.request.
public static class PathMatchProbe
{
	[InitializeOnLoadMethod]
	private static void Watch()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	// 2026-11-17: 007 -Path_008-> 008 -Path_009-> 009, and 007 -Path_008-> 008_05 (McCool's level, a side branch at the
	// junction where Path_008 ends) -Path_008_05-> merchant -> resource node (off the map; Path_008_01 is unowned).
	// McCool's cutscene trigger (ExecuteActionTree) moves from 008 to 008_05. Never saves; leaves the scene dirty.
	private static void Apply(System.Collections.Generic.List<HotSpotWorldMapViewBase> hotspots, Transform zone, StringBuilder sb)
	{
		System.Func<string, HotSpotWorldMapViewBase> h = n => hotspots.First(x => x.name == n);
		System.Func<string, RageSplineLite> p = n => zone.GetComponentsInChildren<RageSplineLite>(true).First(x => x.name == n);
		var at007 = h("Hotspot_007_Battleground");
		var at008 = h("Hotspot_008_Battleground");
		var at009 = h("Hotspot_009_Battleground");
		var gate = h("Hotspot_008_05_Battleground");
		var merchant = h("Hotspot_008_02_Merchant");
		var node = h("Hotspot_008_03_ResourceNode");

		Link(at007, null, null, at008, gate);
		Link(at008, at007, p("Path_008"), at009);
		Link(at009, at008, p("Path_009"), h("Hotspot_010_Battleground"));
		Link(gate, at007, p("Path_008"), merchant);
		Link(merchant, gate, p("Path_008_05"), node);
		var after = new SerializedObject(at009);
		after.FindProperty("m_unlocksPastPrevious").boolValue = false;
		after.ApplyModifiedPropertiesWithoutUndo();

		var cutscene = at008.GetComponent<ExecuteActionTree>();
		if (cutscene != null)
		{
			if (gate.GetComponent<ExecuteActionTree>() != null)
			{
				sb.AppendLine("ERROR 008_05 already has an ExecuteActionTree; cutscene not moved");
			}
			else
			{
				var moved = gate.gameObject.AddComponent<ExecuteActionTree>();
				moved.m_ActionTree = cutscene.m_ActionTree;
				moved.m_executeBeforeUnlock = cutscene.m_executeBeforeUnlock;
				moved.m_propagateRateAppPopup = cutscene.m_propagateRateAppPopup;
				EditorUtility.SetDirty(moved);
				Object.DestroyImmediate(cutscene);
				sb.AppendLine("cutscene moved: " + (moved.m_ActionTree ? moved.m_ActionTree.name : "null") + " before=" + moved.m_executeBeforeUnlock);
			}
		}
		else
		{
			sb.AppendLine("008 has no ExecuteActionTree; 008_05 has " + (gate.GetComponent<ExecuteActionTree>() ? gate.GetComponent<ExecuteActionTree>().m_ActionTree.name : "none"));
		}
		foreach (var name in new[] { "Path_008", "Path_008_05", "Path_008_01", "Path_009" })
		{
			p(name).gameObject.SetActive(false);
		}
		sb.AppendLine("applied");
	}

	// previous/path null = leave as is; outgoing = the new list (empty = no follow-ups).
	private static void Link(HotSpotWorldMapViewBase owner, HotSpotWorldMapViewBase previous, RageSplineLite path, params HotSpotWorldMapViewBase[] outgoing)
	{
		var data = new SerializedObject(owner);
		if (previous != null)
		{
			data.FindProperty("m_previousHotSpot").objectReferenceValue = previous;
		}
		if (path != null)
		{
			data.FindProperty("m_litePath").objectReferenceValue = path;
		}
		var list = data.FindProperty("m_outgoingHotSpots");
		list.ClearArray();
		foreach (var o in outgoing)
		{
			list.InsertArrayElementAtIndex(list.arraySize);
			list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = o;
		}
		data.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(owner);
	}

	private static void Render(Transform zone)
	{
		var shown = zone.GetComponentsInChildren<RageSplineLite>(true).Where(l => !l.gameObject.activeSelf).ToList();
		shown.ForEach(l => l.gameObject.SetActive(true));
		var go = new GameObject("ProbeCam") { hideFlags = HideFlags.HideAndDontSave };
		var cam = go.AddComponent<Camera>();
		cam.orthographic = true;
		cam.orthographicSize = 250f;
		cam.aspect = 1f;
		cam.transform.position = new Vector3(-950f, -1520f, -1250f);
		cam.farClipPlane = 3000f;
		cam.clearFlags = CameraClearFlags.SolidColor;
		cam.cullingMask = ~(1 << 8);
		var rt = new RenderTexture(1000, 1000, 24);
		cam.targetTexture = rt;
		cam.Render();
		RenderTexture.active = rt;
		var tex = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
		tex.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0);
		RenderTexture.active = null;
		File.WriteAllBytes("Temp/PathMatchProbe.png", tex.EncodeToPNG());
		cam.targetTexture = null;
		Object.DestroyImmediate(go);
		Object.DestroyImmediate(rt);
		Object.DestroyImmediate(tex);
		shown.ForEach(l => l.gameObject.SetActive(false));
	}

	private static void Poll()
	{
		if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Temp/PathMatchProbe.request"))
		{
			return;
		}
		var apply = File.ReadAllText("Temp/PathMatchProbe.request").Trim() == "apply";
		File.Delete("Temp/PathMatchProbe.request");
		var sb = new StringBuilder();
		var scene = SceneManager.GetSceneByPath("Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity");
		sb.AppendLine("dirty=" + scene.isDirty + " playing=" + EditorApplication.isPlaying);
		var zone = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).First(t => t.name == "Zone_02");
		var hotspots = zone.GetComponentsInChildren<HotSpotWorldMapViewBase>(true).OrderBy(h => h.name).ToList();
		if (apply)
		{
			Apply(hotspots, zone, sb);
			UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
		}
		foreach (var h in hotspots)
		{
			sb.AppendLine("H " + h.name + " " + ((Vector2)h.transform.position).ToString("F0") + " prev=" + (h.m_previousHotSpot ? h.m_previousHotSpot.name : "-") + " out=" + string.Join(",", h.m_outgoingHotSpots.Select(o => o ? o.name : "null")) + " path=" + (h.m_litePath ? h.m_litePath.name : "-") + " jump=" + h.m_jumpPath);
		}
		System.Func<Vector3, string> near = p => string.Join(" ", hotspots.Select(h => new { h, d = Vector2.Distance(h.transform.position, p) }).OrderBy(x => x.d).Take(2).Select(x => x.h.name.Replace("Hotspot_", "") + ":" + x.d.ToString("F0")));
		foreach (var l in zone.GetComponentsInChildren<RageSplineLite>(true).OrderBy(l => l.name))
		{
			var rages = l.GetComponents<RageSpline>();
			var rage = rages.FirstOrDefault(r => r.GetPointCount() > 0);
			var line = "P " + l.name + " active=" + l.gameObject.activeSelf + " rages=" + rages.Length + "(" + string.Join("/", rages.Select(r => r.GetPointCount())) + ")";
			line += " lite " + ((Vector2)l.GetPositionWorldSpace(0f)).ToString("F0") + "->" + ((Vector2)l.GetPositionWorldSpace(1f)).ToString("F0");
			if (rage != null)
			{
				var a = rage.GetPositionWorldSpace(0);
				var b = rage.GetPositionWorldSpace(rage.GetPointCount() - 1);
				line += " rage " + ((Vector2)a).ToString("F0") + "->" + ((Vector2)b).ToString("F0") + " | start near " + near(a) + " | end near " + near(b);
			}
			else
			{
				line += " | start near " + near(l.GetPositionWorldSpace(0f)) + " | end near " + near(l.GetPositionWorldSpace(1f));
			}
			sb.AppendLine(line);
			if (rage != null && System.Text.RegularExpressions.Regex.IsMatch(l.name, "^Path_00[6-9]"))
			{
				for (var i = 0; i < rage.GetPointCount(); i++)
				{
					sb.AppendLine("    " + i + " " + ((Vector2)rage.GetPositionWorldSpace(i)).ToString("F1") + " in " + ((Vector2)(rage.GetInControlPositionWorldSpace(i) - rage.GetPositionWorldSpace(i))).ToString("F1") + " out " + ((Vector2)(rage.GetOutControlPositionWorldSpace(i) - rage.GetPositionWorldSpace(i))).ToString("F1") + " w " + rage.GetOutlineWidthMultiplier(i).ToString("F2") + " nat " + rage.GetNatural(i));
				}
			}
		}
		Render(zone);
		File.WriteAllText("Temp/PathMatchProbe.log", sb.ToString());
	}
}
