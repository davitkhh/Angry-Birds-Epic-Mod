using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Edit-mode preview of the merchant's paired shield-upgrade tile (ShopOfferBlindWorldmap pair layout), rendered from
// Window_WorldShop.unity with mock texts. Request: touch Temp/PairTilePreview.request ("soldout" in it = right half sold
// out). Output: Temp/PairTilePreview.png (+ .log). The scene is closed unsaved when it wasn't open.
public static class PairTilePreview
{
	private const string RequestFile = "Temp/PairTilePreview.request";

	private const string LogFile = "Temp/PairTilePreview.log";

	private const string ScenePath = "Assets/Scenes/1_WorldMap/2_Interface/Window_WorldShop.unity";

	private static readonly string[] IconPrefabs =
	{
		"Assets/Resources/Prefabs/0_Generic/1_Interface/BirdEquipment/OffHand/1_RedBird/OffHand_Shield_01.prefab",
		"Assets/Resources/Prefabs/0_Generic/1_Interface/BirdEquipment/OffHand/1_RedBird/OffHand_Shield_05.prefab"
	};

	private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

	[InitializeOnLoadMethod]
	private static void Init()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		var request = File.ReadAllText(RequestFile).Trim();
		File.Delete(RequestFile);
		var log = new StringBuilder();
		try
		{
			Render(log, request.Contains("soldout"));
		}
		catch (Exception e)
		{
			log.AppendLine("EXCEPTION " + e);
		}
		File.WriteAllText(LogFile, log.ToString());
	}

	private static void Render(StringBuilder log, bool soldOut)
	{
		var previousActive = SceneManager.GetActiveScene();
		var alreadyOpen = SceneManager.GetSceneByPath(ScenePath).isLoaded;
		if (alreadyOpen)
		{
			// The preview builds runtime objects into the scene; never do that to the copy the user edits
			log.AppendLine("Window_WorldShop is open in the editor; close it and request again");
			return;
		}
		var scene = alreadyOpen ? SceneManager.GetSceneByPath(ScenePath) : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
		var created = new List<Object>();
		try
		{
			SceneManager.SetActiveScene(scene);
			var roots = scene.GetRootGameObjects();
			var blinds = roots.SelectMany(r => r.GetComponentsInChildren<ShopOfferBlindWorldmap>(true)).ToList();
			var blind = blinds.First();
			for (var t = blind.transform; t != null; t = t.parent)
			{
				t.gameObject.SetActive(true);
			}
			foreach (var other in blinds.Where(x => x != blind))
			{
				other.gameObject.SetActive(false);
			}
			var textures = blind.PreviewPair(
				new[] { "Shield", "Shield" },
				new[] { "Bulwark +10%", "Bulwark +10%" },
				new[] { "2 in stock", soldOut ? "Sold out!" : "1 in stock" },
				new[] { "3", "14" },
				new[] { false, soldOut });
			log.AppendLine("pair built on " + blind.name);

			var pool = roots.SelectMany(r => r.GetComponentsInChildren<EquipmentIconPreviewPool>(true)).FirstOrDefault();
			var stages = pool == null ? null : pool.GetType().GetField("m_Stages", Fields).GetValue(pool) as List<EquipmentIconPreviewPool.PreviewStage>;
			log.AppendLine("stages " + (stages == null ? -1 : stages.Count));
			for (var i = 0; i < 2 && stages != null && i < stages.Count; i++)
			{
				var stage = stages[i];
				var icon = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(IconPrefabs[i]), stage.StageRoot);
				created.Add(icon);
				icon.transform.localPosition = Vector3.zero;
				NGUITools.SetLayer(icon, 25);
				foreach (var component in icon.GetComponentsInChildren<MonoBehaviour>(true))
				{
					foreach (var method in new[] { "Awake", "OnEnable", "Start", "LateUpdate" })
					{
						var m = component.GetType().GetMethod(method, Fields, null, Type.EmptyTypes, null);
						if (m != null)
						{
							try { m.Invoke(component, null); } catch (Exception) { }
						}
					}
				}
				stage.PreviewCamera.enabled = true;
				stage.PreviewCamera.Render();
				// Each stage renders into its own texture; render now so the next icon doesn't share the frame
				textures[i].mainTexture = stage.RenderTexture;
				textures[i].gameObject.SetActive(true);
				icon.SetActive(false);
			}
			// Re-render both stages with only their own icon visible
			for (var i = 0; i < 2 && stages != null && i < stages.Count && i < created.Count; i++)
			{
				foreach (var o in created)
				{
					((GameObject)o).SetActive(o == created[i]);
				}
				stages[i].PreviewCamera.Render();
			}
			UpgradeTilePreview.RenderTile(blind, log);
			File.Copy("Temp/UpgradeTilePreview.png", "Temp/PairTilePreview.png", true);
		}
		finally
		{
			foreach (var o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			if (previousActive.IsValid())
			{
				SceneManager.SetActiveScene(previousActive);
			}
			if (!alreadyOpen)
			{
				EditorSceneManager.CloseScene(scene, true);
			}
		}
	}
}
