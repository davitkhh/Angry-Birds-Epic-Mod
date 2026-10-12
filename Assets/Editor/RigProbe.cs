using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using ABH.GameDatas;
using UnityEditor;
using UnityEngine;

// Play mode only (world map loaded): Temp/RigProbe.request holds pig ids (comma separated). For each it renders
// Temp/RigProbe_<id>_map.png (world map character), _map_noback.png (same, Headgear_Back hidden) and _battle.png
// (battle rig from the high-res provider), and dumps submesh bones/materials to Temp/RigProbe.log.
// Spawns far from the map and destroys everything afterwards.
public static class RigProbe
{
	private const string RequestFile = "Temp/RigProbe.request";

	private const string LogFile = "Temp/RigProbe.log";

	private const string CharacterPrefabGuid = "11291cc1e4da9144aaeee158e59275c2";

	[InitializeOnLoadMethod]
	private static void WatchForRequests()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!EditorApplication.isPlaying || !File.Exists(RequestFile) || Object.FindObjectOfType<WorldMapStateMgr>() == null)
		{
			return;
		}
		var ids = File.ReadAllText(RequestFile).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
		File.Delete(RequestFile);
		var host = new GameObject("RigProbeHost").AddComponent<ProbeRunner>();
		host.StartCoroutine(Run(host.gameObject, ids));
	}

	private class ProbeRunner : MonoBehaviour
	{
	}

	private static IEnumerator Run(GameObject host, string[] ids)
	{
		var log = new StringBuilder();
		var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(CharacterPrefabGuid));
		var i = 0;
		foreach (var id in ids)
		{
			// World map character.
			var position = new Vector3(20000f + i * 1000f, 20000f, -40f);
			i++;
			var container = new GameObject("RigProbe_" + id);
			container.transform.position = position;
			var character = Object.Instantiate(prefab, container.transform);
			character.transform.localPosition = Vector3.zero;
			character.GetComponent<CharacterControllerWorldMap>().SetModel(id);
			character.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
			yield return null;
			yield return null;
			Dump(log, id + " (world map)", container);
			Render(container, position, 45f, "Temp/RigProbe_" + id + "_map.png");
			var asset = container.GetComponentInChildren<CharacterAssetController>();
			if (asset != null && asset.m_BoneAnimation != null)
			{
				asset.m_BoneAnimation.HideBone("Headgear_Back", true);
				yield return null;
				Render(container, position, 45f, "Temp/RigProbe_" + id + "_map_noback.png");
			}
			Object.Destroy(container);

			// Battle rig.
			position = new Vector3(20000f + i * 1000f, 20000f, -40f);
			i++;
			var model = new PigGameData(id, DIContainerInfrastructure.GetCurrentPlayer().Data.Level);
			var battleRoot = new GameObject("RigProbeBattle_" + id);
			battleRoot.transform.position = position;
			var battle = DIContainerInfrastructure.GetCharacterAssetProvider(false).InstantiateObject(model.AssetName, battleRoot.transform, position, Quaternion.identity, false);
			if (battle != null)
			{
				var battleAsset = battle.GetComponent<CharacterAssetController>();
				battleAsset.SetModel(model, false);
				battle.transform.localScale = Vector3.one;
				yield return null;
				yield return null;
				Dump(log, id + " (battle)", battleRoot);
				Render(battleRoot, position, 110f, "Temp/RigProbe_" + id + "_battle.png");
			}
			Object.Destroy(battleRoot);
		}
		File.WriteAllText(LogFile, log.ToString());
		Object.Destroy(host);
	}

	private static void Dump(StringBuilder log, string label, GameObject root)
	{
		var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
		log.AppendLine("== " + label + " rig " + (renderer ? renderer.gameObject.name : "none"));
		if (renderer == null)
		{
			return;
		}
		var mesh = renderer.sharedMesh;
		var weights = mesh.boneWeights;
		for (var s = 0; s < mesh.subMeshCount; s++)
		{
			var bones = mesh.GetTriangles(s).Select(v => weights[v].boneIndex0).Distinct().Select(b => renderer.bones[b] ? renderer.bones[b].name : "?").ToArray();
			var material = s < renderer.sharedMaterials.Length && renderer.sharedMaterials[s] ? renderer.sharedMaterials[s].name + "/" + renderer.sharedMaterials[s].mainTexture : "-";
			log.AppendLine("  submesh " + s + " [" + material + "] " + string.Join(", ", bones));
		}
	}

	private static void Render(GameObject root, Vector3 position, float size, string path)
	{
		var cam = new GameObject("RigProbeCamera").AddComponent<Camera>();
		cam.orthographic = true;
		cam.orthographicSize = size;
		cam.transform.position = position + new Vector3(0f, size * 0.75f, -500f);
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
	}
}
