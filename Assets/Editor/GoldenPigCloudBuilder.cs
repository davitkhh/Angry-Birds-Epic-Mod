using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Adds the permanent golden pig cloud near the pig prison (hotspot_golden_pig_cloud_battleground): a clone of a regular
// battle hotspot switched to HotSpotWorldMapViewGoldenPigCloud, dressed with the daily golden pig cloud's cloud sprites
// and golden pig. No path. For now it hangs off the camp (completed from the start) so it is active immediately; to
// make it appear after castle 1, move it to Hotspot_014_Castle's outgoing list and set its previous hotspot.
// Menu item, or touch Temp/GoldenPigCloudBuild.request.
public static class GoldenPigCloudBuilder
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/1_Scenery/WorldMap_Generated.unity";

	private const string RequestFile = "Temp/GoldenPigCloudBuild.request";

	private const string LogFile = "Temp/GoldenPigCloudBuild.log";

	private const string NameId = "hotspot_golden_pig_cloud_battleground";

	private const string ObjectName = "Hotspot_GoldenPigCloud";

	private static readonly Vector3 WorldPosition = new Vector3(-620f, -1630f, -10f);

	private static readonly StringBuilder s_Log = new StringBuilder();

	[InitializeOnLoadMethod]
	private static void WatchForRequests()
	{
		EditorApplication.update -= PollRequest;
		EditorApplication.update += PollRequest;
	}

	private static void PollRequest()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		File.Delete(RequestFile);
		Build();
	}

	[MenuItem("Tools/World Map/Add Golden Pig Cloud Near Pig Prison")]
	public static void Build()
	{
		s_Log.Length = 0;
		try
		{
			var scene = SceneManager.GetSceneByPath(ScenePath);
			if (!scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
			}
			var hadUnsavedChanges = scene.isDirty;
			BuildInternal(scene);
			EditorSceneManager.MarkSceneDirty(scene);
			if (hadUnsavedChanges)
			{
				Log("not saved: the scene already had unsaved changes, save it in the editor");
			}
			else
			{
				EditorSceneManager.SaveScene(scene);
				Log("saved " + ScenePath);
			}
		}
		catch (Exception e)
		{
			Log("error: " + e);
		}
		File.WriteAllText(LogFile, s_Log.ToString());
		Debug.Log("[GoldenPigCloudBuilder]\n" + s_Log);
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static void BuildInternal(Scene scene)
	{
		var root = scene.GetRootGameObjects().First(r => r.name == "WorldMap").transform;
		var hotspots = root.Find("2_Scenery/Zone_02/3_Hotspots");
		if (hotspots.Find(ObjectName) != null)
		{
			Log("aborted: " + ObjectName + " already exists");
			return;
		}
		var template = hotspots.Find("Hotspot_007_Battleground");
		var dailyCloud = root.Find("2_Scenery/GoldenPigsCloud/OrthoIndepentent/Hotspot_GoldenPigsCloud");
		var camp = root.Find("2_Scenery/Zone_01/3_Hotspots/Hotspot_001_Camp").GetComponent<HotSpotWorldMapViewBase>();

		var cloud = UnityEngine.Object.Instantiate(template.gameObject, hotspots);
		cloud.name = ObjectName;
		cloud.transform.position = WorldPosition;
		foreach (var execute in cloud.GetComponents<ExecuteActionTree>())
		{
			UnityEngine.Object.DestroyImmediate(execute);
		}

		// Switch the battle view to the cloud view (same serialized fields plus the pig).
		var view = cloud.GetComponent<HotSpotWorldMapViewBattle>();
		var data = new SerializedObject(view);
		var cloudScript = AssetDatabase.FindAssets("HotSpotWorldMapViewGoldenPigCloud t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<MonoScript>).First();
		data.FindProperty("m_Script").objectReferenceValue = cloudScript;
		data.ApplyModifiedPropertiesWithoutUndo();
		var cloudView = cloud.GetComponent<HotSpotWorldMapViewGoldenPigCloud>();

		// The daily cloud's look: cloud sprites and the golden pig.
		CharacterAssetController pig = null;
		foreach (var part in new[] { "Cloud", "Character" })
		{
			var source = dailyCloud.Find(part);
			var copy = UnityEngine.Object.Instantiate(source.gameObject, cloud.transform);
			copy.name = part;
			copy.transform.localPosition = source.localPosition;
			copy.transform.localRotation = source.localRotation;
			copy.transform.localScale = source.localScale;
			if (part == "Character")
			{
				pig = copy.GetComponentInChildren<CharacterAssetController>(true);
			}
		}

		data = new SerializedObject(cloudView);
		data.FindProperty("m_nameId").stringValue = NameId;
		data.FindProperty("m_previousHotSpot").objectReferenceValue = camp;
		data.FindProperty("m_litePath").objectReferenceValue = null;
		data.FindProperty("m_outgoingHotSpots").ClearArray();
		data.FindProperty("m_CloudGoldenPig").objectReferenceValue = pig;
		data.ApplyModifiedPropertiesWithoutUndo();

		var campData = new SerializedObject(camp);
		var outgoing = campData.FindProperty("m_outgoingHotSpots");
		outgoing.InsertArrayElementAtIndex(outgoing.arraySize);
		outgoing.GetArrayElementAtIndex(outgoing.arraySize - 1).objectReferenceValue = cloudView;
		campData.ApplyModifiedPropertiesWithoutUndo();

		Log("created " + ObjectName + " at " + cloud.transform.position + " (" + NameId + "), view " + cloudView.GetType().Name + ", pig " + (pig != null ? pig.name : "none") + ", unlocked from the start via Hotspot_001_Camp, no path");
	}
}
