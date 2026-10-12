using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Play mode: Temp/CutsceneCapture.request holds "objectName,seconds[,screen]". Once a GameObject with that name exists,
// renders it with a copy of the world map camera (same layers, so scenery that draws over it shows up) every 0.37 s
// (not a multiple of the 0.25 s walk hop, so shots don't alias onto one pose) into Temp/Capture_<n>.png. With "screen"
// the copy keeps the game camera's own framing (what the player sees). Read-only.
public static class CutsceneCapture
{
	private const string RequestFile = "Temp/CutsceneCapture.request";

	private static string s_Target;

	private static float s_Seconds;

	private static bool s_Screen;

	private const float Interval = 0.37f;

	[InitializeOnLoadMethod]
	private static void WatchForRequests()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!EditorApplication.isPlaying)
		{
			return;
		}
		if (s_Target == null && File.Exists(RequestFile))
		{
			var parts = File.ReadAllText(RequestFile).Split(',');
			s_Target = parts[0].Trim();
			s_Seconds = parts.Length > 1 ? float.Parse(parts[1]) : 10f;
			s_Screen = parts.Length > 2 && parts[2].Trim() == "screen";
		}
		if (s_Target == null)
		{
			return;
		}
		var target = GameObject.Find(s_Target);
		var worldCamera = Camera.allCameras.FirstOrDefault(c => c.name == "Camera" && c.transform.parent != null && c.transform.parent.name == "Animation");
		if (target == null || worldCamera == null)
		{
			return;
		}
		File.Delete(RequestFile);
		var host = new GameObject("CutsceneCaptureHost").AddComponent<Runner>();
		host.StartCoroutine(Capture(host.gameObject, target.transform, worldCamera, s_Seconds, s_Screen));
		s_Target = null;
	}

	private class Runner : MonoBehaviour
	{
	}

	private static IEnumerator Capture(GameObject host, Transform target, Camera worldCamera, float seconds, bool screen)
	{
		var shot = 0;
		for (var elapsed = 0f; elapsed < seconds && (screen || target != null); elapsed += Interval)
		{
			yield return new WaitForEndOfFrame();
			var cam = new GameObject("CutsceneCaptureCamera").AddComponent<Camera>();
			cam.CopyFrom(worldCamera);
			var width = 360;
			if (screen)
			{
				width = 640;
			}
			else
			{
				cam.orthographicSize = 60f;
				cam.transform.position = new Vector3(target.position.x, target.position.y + 25f, worldCamera.transform.position.z);
			}
			var rt = new RenderTexture(width, 360, 24);
			cam.targetTexture = rt;
			cam.Render();
			RenderTexture.active = rt;
			var image = new Texture2D(width, 360, TextureFormat.RGB24, false);
			image.ReadPixels(new Rect(0, 0, width, 360), 0, 0);
			RenderTexture.active = null;
			File.WriteAllBytes("Temp/Capture_" + shot.ToString("00") + ".png", image.EncodeToPNG());
			Object.Destroy(cam.gameObject);
			shot++;
			yield return new WaitForSeconds(Interval);
		}
		Object.Destroy(host);
	}
}
