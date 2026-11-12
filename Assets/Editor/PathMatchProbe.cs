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

	// 2026-11-08, matching the user's drawn paths: 007 -Path_008-> 008 -Path_009-> 009; 008 -Path_008_05-> merchant;
	// merchant -Path_008_01 (reversed)-> 008_05; merchant -> resource node (off the map).
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

		var reversed = p("Path_008_01");
		var rage = reversed.GetComponents<RageSpline>().First(r => r.GetPointCount() > 0);
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

		Link(at007, null, null, at008);
		Link(at008, at007, p("Path_008"), at009, merchant);
		Link(at009, at008, p("Path_009"), null);
		Link(merchant, at008, p("Path_008_05"), node, gate);
		Link(gate, merchant, reversed, null);
		foreach (var name in new[] { "Path_008", "Path_008_05", "Path_008_01" })
		{
			var lite = p(name);
			lite.CreateFromSpline();
			EditorUtility.SetDirty(lite);
		}
		foreach (var name in new[] { "Path_008", "Path_008_05", "Path_008_01", "Path_009" })
		{
			p(name).gameObject.SetActive(false);
		}
		sb.AppendLine("applied");
	}

	// prev/path null = leave as is; outgoing given = replace the list (null entry list = keep).
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
		if (outgoing != null && !(outgoing.Length == 1 && outgoing[0] == null))
		{
			var list = data.FindProperty("m_outgoingHotSpots");
			if (owner.name == "Hotspot_007_Battleground" || owner.name == "Hotspot_008_Battleground" || owner.name == "Hotspot_008_02_Merchant")
			{
				list.ClearArray();
			}
			foreach (var o in outgoing)
			{
				list.InsertArrayElementAtIndex(list.arraySize);
				list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = o;
			}
		}
		else if (owner.name == "Hotspot_008_05_Battleground")
		{
			data.FindProperty("m_outgoingHotSpots").ClearArray();
		}
		data.ApplyModifiedPropertiesWithoutUndo();
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
