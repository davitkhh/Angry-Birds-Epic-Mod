using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One-shot: the golden pig copied from the daily cloud is an unbaked rig (no mesh/materials until the daily event
// builds it at runtime), so it rendered magenta on the permanent cloud. Replace it with a copy of the CinePig hotspot's
// baked golden pig, keeping its position and size. Touch Temp/GoldenPigCloudPig.request.
public static class GoldenPigCloudPigFix
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	[InitializeOnLoadMethod]
	private static void Watch()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!File.Exists("Temp/GoldenPigCloudPig.request") || EditorApplication.isCompiling || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		File.Delete("Temp/GoldenPigCloudPig.request");
		var log = new System.Text.StringBuilder();
		try
		{
			var scene = SceneManager.GetSceneByPath(ScenePath);
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
			}
			var hadUnsavedChanges = scene.isDirty;
			var root = scene.GetRootGameObjects().First(r => r.name == "WorldMap").transform;
			var cloud = root.Find("2_Scenery/Zone_02/3_Hotspots/Hotspot_GoldenPigCloud");
			var oldPig = cloud.Find("Character/GoldenPig");
			var baked = root.Find("2_Scenery/Zone_02/1_Landscape/HotSpot_CinePig/GoldenPig");
			var pig = Object.Instantiate(baked.gameObject, oldPig.parent);
			pig.name = "GoldenPig";
			pig.transform.localPosition = oldPig.localPosition;
			pig.transform.localRotation = oldPig.localRotation;
			pig.transform.localScale = oldPig.localScale;
			Object.DestroyImmediate(oldPig.gameObject);
			var view = new SerializedObject(cloud.GetComponent<HotSpotWorldMapViewGoldenPigCloud>());
			view.FindProperty("m_CloudGoldenPig").objectReferenceValue = null;
			view.ApplyModifiedPropertiesWithoutUndo();
			log.AppendLine("replaced the cloud's golden pig with a copy of HotSpot_CinePig/GoldenPig (baked mesh " + (pig.GetComponent<SkinnedMeshRenderer>().sharedMesh != null) + ")");
			EditorSceneManager.MarkSceneDirty(scene);
			if (hadUnsavedChanges)
			{
				log.AppendLine("not saved: the scene already had unsaved changes, save it in the editor");
			}
			else
			{
				EditorSceneManager.SaveScene(scene);
				log.AppendLine("saved " + ScenePath);
			}
		}
		catch (System.Exception e)
		{
			log.AppendLine("error: " + e);
		}
		File.WriteAllText("Temp/GoldenPigCloudPig.log", log.ToString());
	}
}
