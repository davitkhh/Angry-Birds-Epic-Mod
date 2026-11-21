using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// Debug probe for BirdUnlockSequence. Write Temp/BirdUnlockProbe.request containing:
//   backdrop  - (edit or play mode) builds a BirdUnlockBackdrop, renders its camera to Temp/BirdUnlockProbe_backdrop.png
//   state     - (play mode) logs every camera to Temp/BirdUnlockProbe.log and saves a screenshot Temp/BirdUnlockProbe_shot.png
public static class BirdUnlockProbe
{
	private const string RequestPath = "Temp/BirdUnlockProbe.request";

	private const string LogPath = "Temp/BirdUnlockProbe.log";

	[InitializeOnLoadMethod]
	private static void Watch()
	{
		EditorApplication.update += () =>
		{
			if (!File.Exists(RequestPath))
			{
				return;
			}
			var command = File.ReadAllText(RequestPath).Trim();
			File.Delete(RequestPath);
			try
			{
				if (command == "backdrop")
				{
					Backdrop();
				}
				else if (command == "state")
				{
					State();
				}
			}
			catch (System.Exception e)
			{
				File.WriteAllText(LogPath, "EXCEPTION " + e);
			}
		};
	}

	private static void Backdrop()
	{
		var log = new StringBuilder();
		var backdrop = BirdUnlockBackdrop.Create(new Vector3(0f, -70000f, 0f), 0.85f, 1.2f);
		backdrop.Box = Rect.MinMaxRect(200f, -50f, Screen.width - 200f, Screen.height + 50f);
		backdrop.RayCenter = new Vector2(Screen.width * 0.4f, Screen.height * 0.4f);
		backdrop.OverlayAlpha = 0.75f;
		var camera = backdrop.GetComponentInChildren<Camera>();
		typeof(BirdUnlockBackdrop).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(backdrop, null);
		var rt = new RenderTexture(Mathf.Max(64, Screen.width), Mathf.Max(64, Screen.height), 24);
		camera.targetTexture = rt;
		camera.clearFlags = CameraClearFlags.SolidColor;
		camera.backgroundColor = new Color(0f, 1f, 0f, 1f);
		camera.Render();
		RenderTexture.active = rt;
		var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
		tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
		tex.Apply();
		RenderTexture.active = null;
		File.WriteAllBytes("Temp/BirdUnlockProbe_backdrop.png", tex.EncodeToPNG());
		log.AppendLine("screen " + Screen.width + "x" + Screen.height + " rt " + rt.width + "x" + rt.height);
		log.AppendLine("center " + tex.GetPixel(rt.width / 2, rt.height / 2) + " corner " + tex.GetPixel(5, 5));
		var flat = typeof(BirdUnlockBackdrop).GetField("m_FlatMesh", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(backdrop) as Mesh;
		var material = typeof(BirdUnlockBackdrop).GetField("m_FlatMaterial", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(backdrop) as Material;
		log.AppendLine("mesh verts " + (flat != null ? flat.vertexCount : -1) + " bounds " + (flat != null ? flat.bounds.ToString() : "") + " shader " + (material != null && material.shader != null ? material.shader.name : "null"));
		camera.targetTexture = null;
		Object.DestroyImmediate(rt);
		Object.DestroyImmediate(backdrop.gameObject);
		File.WriteAllText(LogPath, log.ToString());
	}

	private static void State()
	{
		var log = new StringBuilder();
		log.AppendLine("playing " + EditorApplication.isPlaying + " active sequence " + (BirdUnlockSequence.Active != null));
		foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>().Where(c => c.gameObject.scene.IsValid()).OrderBy(c => c.depth))
		{
			log.AppendLine(string.Format("{0} scene={1} tag={2} enabled={3} active={4} depth={5} clear={6} mask={7} rect={8} hdr={9} msaa={10} rt={11} pos={12} ortho={13} path={14}",
				GetPath(camera.transform), camera.gameObject.scene.name, camera.tag, camera.enabled, camera.gameObject.activeInHierarchy, camera.depth, camera.clearFlags, camera.cullingMask, camera.rect, camera.allowHDR, camera.allowMSAA, camera.targetTexture, camera.transform.position, camera.orthographicSize, camera.actualRenderingPath));
		}
		File.WriteAllText(LogPath, log.ToString());
		ScreenCapture.CaptureScreenshot("Temp/BirdUnlockProbe_shot.png");
	}

	private static string GetPath(Transform t)
	{
		return t.parent == null ? t.name : GetPath(t.parent) + "/" + t.name;
	}
}
