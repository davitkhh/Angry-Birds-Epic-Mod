using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Read-only: renders each world map foreground prop on its own over a world rect and reports how much of the rect it
// covers. Temp/ForegroundCoverProbe.request = "x y halfWidth halfHeight" (world). Opens the world map additively if
// needed and closes it again without saving. Also writes Temp/ForegroundCoverProbe_all.png (whole scenery, no props).
public static class ForegroundCoverProbe
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/ForegroundCoverProbe.request";

	private const string LogFile = "Temp/ForegroundCoverProbe.log";

	[InitializeOnLoadMethod]
	private static void Watch()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		var words = File.ReadAllText(RequestFile).Split(new[] { ' ', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
		// "only <path part> x y halfW halfH": render just the renderers whose path contains the part (inactive ones too)
		// to Temp/ForegroundCoverProbe_only.png, with the rest of the scenery dimmed underneath.
		string only = null;
		if (words.Length > 0 && words[0] == "only")
		{
			only = words[1];
			words = words.Skip(2).ToArray();
		}
		var v = words.Select(float.Parse).ToArray();
		File.Delete(RequestFile);
		var log = new StringBuilder();
		var scene = SceneManager.GetSceneByPath(ScenePath);
		var opened = false;
		try
		{
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
				opened = true;
			}
			var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
			// Fifth value: also list every renderer over the rect whose bounds are nearer the camera (z below it).
			if (v.Length > 4)
			{
				var rect = new Rect(v[0] - v[2], v[1] - v[3], v[2] * 2f, v[3] * 2f);
				foreach (var r in all.Where(r => r.bounds.center.z < v[4] && rect.Overlaps(new Rect(r.bounds.min.x, r.bounds.min.y, r.bounds.size.x, r.bounds.size.y))).OrderBy(r => r.bounds.center.z))
				{
					log.AppendLine("near z=" + r.bounds.center.z.ToString("0.#") + " active=" + r.gameObject.activeInHierarchy + " " + Path(r.transform) + " mats=" + string.Join(",", r.sharedMaterials.Where(m => m != null).Select(m => m.name + "/" + m.renderQueue).ToArray()));
				}
				foreach (var h in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<HotSpotWorldMapViewBase>(true)).Where(h => rect.Contains(h.transform.position)))
				{
					log.AppendLine("hotspot " + h.name + " pos=" + h.transform.position + " lossyScale=" + h.transform.lossyScale);
				}
			}
			var props = all.Where(r => r.transform.parent != null && r.GetComponentsInParent<Transform>(true).Any(t => t.name == "7_ForegroundProps")).ToArray();
			var go = new GameObject("ProbeCamera");
			var cam = go.AddComponent<Camera>();
			cam.orthographic = true;
			cam.orthographicSize = v[3];
			cam.aspect = v[2] / v[3];
			cam.transform.position = new Vector3(v[0], v[1], -2000f);
			cam.clearFlags = CameraClearFlags.SolidColor;
			cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
			cam.nearClipPlane = 1f;
			cam.farClipPlane = 4000f;
			cam.cullingMask = ~0;
			var rt = new RenderTexture(only != null ? 512 : 128, Mathf.Max(16, (int)((only != null ? 512 : 128) * v[3] / v[2])), 24, RenderTextureFormat.ARGB32);
			cam.targetTexture = rt;
			var states = all.ToDictionary(r => r, r => r.enabled);
			if (only != null)
			{
				var picked = all.Where(r => Path(r.transform).Contains(only)).ToArray();
				var activeStates = picked.ToDictionary(r => r.gameObject, r => r.gameObject.activeSelf);
				foreach (var r in picked)
				{
					for (var t = r.transform; t != null; t = t.parent)
					{
						t.gameObject.SetActive(true);
					}
				}
				foreach (var r in all)
				{
					r.enabled = picked.Contains(r);
				}
				Save(cam, rt, "Temp/ForegroundCoverProbe_only.png");
				foreach (var r in all)
				{
					r.enabled = states[r] && !picked.Contains(r);
				}
				Save(cam, rt, "Temp/ForegroundCoverProbe_rest.png");
				log.AppendLine("rendered " + picked.Length + " renderers: " + string.Join(", ", picked.Select(r => Path(r.transform)).ToArray()));
				foreach (var r in all)
				{
					r.enabled = states[r];
				}
				cam.targetTexture = null;
				Object.DestroyImmediate(rt);
				Object.DestroyImmediate(go);
				File.WriteAllText(LogFile, log.ToString());
				return;
			}
			foreach (var prop in props)
			{
				foreach (var r in all)
				{
					r.enabled = r == prop;
				}
				log.AppendLine(Coverage(cam, rt).ToString("0.000") + " " + Path(prop.transform) + " z=" + prop.transform.position.z.ToString("0.#") + " boundsZ=" + prop.bounds.center.z.ToString("0.#"));
			}
			foreach (var r in all)
			{
				r.enabled = states[r] && !props.Contains(r);
			}
			Save(cam, rt, "Temp/ForegroundCoverProbe_noprops.png");
			foreach (var r in all)
			{
				r.enabled = states[r];
			}
			Save(cam, rt, "Temp/ForegroundCoverProbe_all.png");
			cam.targetTexture = null;
			Object.DestroyImmediate(rt);
			Object.DestroyImmediate(go);
		}
		catch (System.Exception e)
		{
			log.AppendLine("error: " + e);
		}
		finally
		{
			if (opened)
			{
				EditorSceneManager.CloseScene(scene, true);
			}
		}
		File.WriteAllText(LogFile, log.ToString());
	}

	private static float Coverage(Camera cam, RenderTexture rt)
	{
		cam.Render();
		RenderTexture.active = rt;
		var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
		tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
		RenderTexture.active = null;
		var pixels = tex.GetPixels();
		Object.DestroyImmediate(tex);
		return pixels.Count(p => p.a > 0.3f) / (float)pixels.Length;
	}

	private static void Save(Camera cam, RenderTexture rt, string file)
	{
		cam.Render();
		RenderTexture.active = rt;
		var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
		tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
		RenderTexture.active = null;
		File.WriteAllBytes(file, tex.EncodeToPNG());
		Object.DestroyImmediate(tex);
	}

	private static string Path(Transform t)
	{
		var s = t.name;
		while (t.parent != null)
		{
			t = t.parent;
			s = t.name + "/" + s;
		}
		return s;
	}
}
