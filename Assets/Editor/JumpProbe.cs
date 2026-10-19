using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Play mode only (world map loaded): Temp/JumpProbe.request holds bird asset ids (comma separated, e.g. WhiteBird).
// Spawns each of the player's birds like the world map does (equipment included), poses Idle and Jump at several
// times, renders Temp/JumpProbe_<id>_<clip>_<n>.png and dumps submesh order/materials to Temp/JumpProbe.log.
public static class JumpProbe
{
	private const string RequestFile = "Temp/JumpProbe.request";

	private const string LogFile = "Temp/JumpProbe.log";

	private static readonly float[] Times = { 0f, 0.25f, 0.5f, 0.75f };

	[InitializeOnLoadMethod]
	private static void WatchForRequests()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!EditorApplication.isPlaying || !File.Exists(RequestFile))
		{
			return;
		}
		var worldMap = Object.FindObjectOfType<WorldMapStateMgr>();
		if (worldMap == null)
		{
			return;
		}
		var ids = File.ReadAllText(RequestFile).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
		File.Delete(RequestFile);
		var host = new GameObject("JumpProbeHost").AddComponent<ProbeRunner>();
		host.StartCoroutine(Run(host.gameObject, worldMap, ids));
	}

	private class ProbeRunner : MonoBehaviour
	{
	}

	private static IEnumerator Run(GameObject host, WorldMapStateMgr worldMap, string[] ids)
	{
		var log = new StringBuilder();
		var index = 0;
		foreach (var id in ids)
		{
			var bird = DIContainerInfrastructure.GetCurrentPlayer().Birds.FirstOrDefault(b => b.BalancingData.AssetId == id);
			if (bird == null)
			{
				log.AppendLine("no player bird with asset " + id);
				continue;
			}
			var position = new Vector3(20000f + index * 1000f, 20000f, -40f);
			index++;
			var container = new GameObject("JumpProbe_" + id);
			container.transform.position = position;
			var character = Object.Instantiate(worldMap.m_WorldMapCharacterController, container.transform);
			character.transform.localPosition = Vector3.zero;
			var controller = character.GetComponent<CharacterControllerWorldMap>();
			controller.SetModel(bird);
			character.transform.localScale = worldMap.m_WorldBirdScale;
			yield return null;
			yield return null;
			var anim = controller.m_AssetController.GetComponent<Animation>();
			foreach (var clip in new[] { "Jump" })
			{
				var state = anim[clip];
				if (state == null)
				{
					log.AppendLine(id + ": no clip " + clip);
					continue;
				}
				// Natural playback, capturing every frame until the clip ends.
				anim.Stop();
				anim.Play(clip);
				var n = 0;
				while (anim.IsPlaying(clip) && state.time < state.length && n < 120)
				{
					yield return new WaitForEndOfFrame();
					Dump(log, id + " " + clip + " #" + n + " t=" + (state.time / state.length).ToString("0.00"), container);
					Render(container, position, 160f, "Temp/JumpProbe_" + id + "_" + clip + "_" + n + ".png");
					n++;
				}
			}
			Object.Destroy(container);
		}
		File.WriteAllText(LogFile, log.ToString());
		Object.Destroy(host);
	}

	private static void Dump(StringBuilder log, string label, GameObject root)
	{
		var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
		log.AppendLine("== " + label);
		if (renderer == null || renderer.sharedMesh == null)
		{
			return;
		}
		var mesh = renderer.sharedMesh;
		var weights = mesh.boneWeights;
		for (var s = 0; s < mesh.subMeshCount; s++)
		{
			var bones = mesh.GetTriangles(s).Select(v => weights[v].boneIndex0).Distinct().Select(b => b < renderer.bones.Length && renderer.bones[b] ? renderer.bones[b].name : "?").ToArray();
			var material = s < renderer.sharedMaterials.Length && renderer.sharedMaterials[s] ? renderer.sharedMaterials[s].name + " (" + renderer.sharedMaterials[s].shader.name + ", q" + renderer.sharedMaterials[s].renderQueue + ")" : "-";
			log.AppendLine("  submesh " + s + " [" + material + "] " + string.Join(", ", bones));
		}
	}

	private static void Render(GameObject root, Vector3 position, float size, string path)
	{
		var cam = new GameObject("JumpProbeCamera").AddComponent<Camera>();
		cam.orthographic = true;
		cam.orthographicSize = size;
		cam.transform.position = position + new Vector3(0f, size * 0.6f, -500f);
		cam.clearFlags = CameraClearFlags.SolidColor;
		cam.backgroundColor = new Color(0.3f, 0.5f, 0.3f);
		cam.cullingMask = 1 << root.GetComponentInChildren<Renderer>().gameObject.layer;
		var target = new RenderTexture(320, 320, 24);
		cam.targetTexture = target;
		cam.Render();
		RenderTexture.active = target;
		var image = new Texture2D(320, 320, TextureFormat.RGB24, false);
		image.ReadPixels(new Rect(0, 0, 320, 320), 0, 0);
		RenderTexture.active = null;
		File.WriteAllBytes(path, image.EncodeToPNG());
		Object.Destroy(cam.gameObject);
		Object.Destroy(target);
	}
}
