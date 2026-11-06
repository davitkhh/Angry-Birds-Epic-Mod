using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SmoothMoves;
using Sprite = UnityEngine.Sprite;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Drag editor for the pirate bucket headgear placements (Headgear_PirateBucket_1..4).
// Tools > Pirate Bucket > Build Placement Scene: a scene with the bare pirate pig four times, each with its bucket. Click
// a bucket and drag it (Move tool, W). Its back layer (inside of the bucket, handle behind the head) is a child and moves along.
// Tools > Pirate Bucket > Apply Dragged Positions (save the scene first if you want to keep it): writes the offsets to
// ArtSource/PirateBucket/placement_offsets.txt (pack.py keeps them when re-packing) and moves the textures' pivots in
// PigPirate_Atlas_03 / _low, so the game shows the buckets where they were dragged. Only position is used, not rotation or scale.
// The art comes from ArtSource/PirateBucket/out (bucket.py). Rebuilding the scene keeps the applied offsets.
[InitializeOnLoad]
public static class PirateBucketPlacement
{
	private const string Folder = "Assets/Editor/PirateBucketPlacement";

	private const string ScenePath = Folder + "/PirateBucketPlacement.unity";

	private const string SourceDir = "ArtSource/PirateBucket/out";

	private const string OffsetsFile = "ArtSource/PirateBucket/placement_offsets.txt";

	private const string RequestFile = "Temp/PirateBucketPlacement.request";

	private const string LogFile = "Temp/PirateBucketPlacement.log";

	private const float Spacing = 420f;

	// bone (Headgear) pixel in pig_reference.png, which is 512x512
	private static readonly Vector2 PigBone = new Vector2(236f, 266f);

	private static readonly string[] Atlases = { "Assets/MonoBehaviour/PigPirate_Atlas_03.asset", "Assets/MonoBehaviour/PigPirate_Atlas_03_low.asset" };

	private static readonly int[] Chances = { 30, 30, 30, 10 };

	static PirateBucketPlacement()
	{
		EditorApplication.update -= Poll;
		EditorApplication.update += Poll;
	}

	private static void Poll()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
		{
			return;
		}
		var request = File.ReadAllText(RequestFile).Trim();
		File.Delete(RequestFile);
		File.WriteAllText(LogFile, string.Empty);
		try
		{
			if (request == "apply")
			{
				Apply();
			}
			else if (request == "sprites")
			{
				RefreshSprites();
			}
			else
			{
				Build();
			}
		}
		catch (System.Exception e)
		{
			Log("error: " + e);
		}
	}

	private static void Log(string line)
	{
		Debug.Log("[PirateBucketPlacement] " + line);
		File.AppendAllText(LogFile, line + "\n");
	}

	private class Placement
	{
		public string Name;
		public int Width;
		public int Height;
		public Vector2 Pivot;
		public bool HasBack;
	}

	private static List<Placement> ReadPivots()
	{
		var list = new List<Placement>();
		foreach (var line in File.ReadAllLines(SourceDir + "/pivots.txt"))
		{
			var f = line.Split(' ');
			if (f.Length < 6)
			{
				continue;
			}
			list.Add(new Placement
			{
				Name = f[0],
				Width = int.Parse(f[1]),
				Height = int.Parse(f[2]),
				Pivot = new Vector2(float.Parse(f[3], CultureInfo.InvariantCulture), float.Parse(f[4], CultureInfo.InvariantCulture)),
				HasBack = f[5] == "1"
			});
		}
		return list;
	}

	private static Dictionary<int, Vector2> ReadOffsets()
	{
		var offsets = new Dictionary<int, Vector2>();
		if (!File.Exists(OffsetsFile))
		{
			return offsets;
		}
		foreach (var line in File.ReadAllLines(OffsetsFile))
		{
			var f = line.Split(' ');
			if (f.Length >= 3)
			{
				offsets[int.Parse(f[0])] = new Vector2(float.Parse(f[1], CultureInfo.InvariantCulture), float.Parse(f[2], CultureInfo.InvariantCulture));
			}
		}
		return offsets;
	}

	// rotation already baked into the art (4th column, degrees counter-clockwise)
	private static Dictionary<int, float> ReadRotations()
	{
		var rotations = new Dictionary<int, float>();
		if (!File.Exists(OffsetsFile))
		{
			return rotations;
		}
		foreach (var line in File.ReadAllLines(OffsetsFile))
		{
			var f = line.Split(' ');
			if (f.Length >= 4)
			{
				rotations[int.Parse(f[0])] = float.Parse(f[3], CultureInfo.InvariantCulture);
			}
		}
		return rotations;
	}

	private static bool RunPython(string script)
	{
		var info = new System.Diagnostics.ProcessStartInfo("/usr/bin/python3", script)
		{
			WorkingDirectory = Path.GetFullPath("ArtSource/PirateBucket"),
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		using (var process = System.Diagnostics.Process.Start(info))
		{
			var output = process.StandardOutput.ReadToEnd();
			var error = process.StandardError.ReadToEnd();
			process.WaitForExit();
			if (process.ExitCode != 0)
			{
				Log(script + " failed:\n" + output + error);
				return false;
			}
		}
		return true;
	}

	private static Sprite ImportSprite(string source, Vector2 pivot01)
	{
		if (!AssetDatabase.IsValidFolder(Folder))
		{
			AssetDatabase.CreateFolder("Assets/Editor", "PirateBucketPlacement");
		}
		var path = Folder + "/" + Path.GetFileName(source);
		File.Copy(source, path, true);
		AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
		var importer = (TextureImporter)AssetImporter.GetAtPath(path);
		importer.textureType = TextureImporterType.Sprite;
		importer.spriteImportMode = SpriteImportMode.Single;
		importer.mipmapEnabled = false;
		importer.alphaIsTransparency = true;
		importer.textureCompression = TextureImporterCompression.Uncompressed;
		var settings = new TextureImporterSettings();
		importer.ReadTextureSettings(settings);
		settings.spritePixelsPerUnit = 1f;
		settings.spriteAlignment = (int)SpriteAlignment.Custom;
		settings.spritePivot = pivot01;
		settings.spriteMeshType = SpriteMeshType.FullRect;
		importer.SetTextureSettings(settings);
		importer.SaveAndReimport();
		return AssetDatabase.LoadAssetAtPath<Sprite>(path);
	}

	private static GameObject SpriteObject(string name, Sprite sprite, int order, Transform parent)
	{
		var go = new GameObject(name);
		go.transform.SetParent(parent, false);
		var renderer = go.AddComponent<SpriteRenderer>();
		renderer.sprite = sprite;
		renderer.sortingOrder = order;
		return go;
	}

	[MenuItem("Tools/Pirate Bucket/Build Placement Scene")]
	public static void Build()
	{
		if (EditorApplication.isPlaying)
		{
			Log("leave play mode first");
			return;
		}
		if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
		{
			return;
		}
		var placements = ReadPivots();
		var offsets = ReadOffsets();
		var pig = ImportSprite(SourceDir + "/pig_reference.png", new Vector2(PigBone.x / 512f, 1f - PigBone.y / 512f));
		var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
		var font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
		var buckets = new List<GameObject>();
		var hidden = new List<GameObject>();
		for (var i = 0; i < placements.Count; i++)
		{
			var p = placements[i];
			var number = i + 1;
			// texture pivot (offset from centre, y up) -> sprite pivot (0..1 from bottom-left): the bone sits on the pivot
			var pivot01 = new Vector2(p.Pivot.x + 0.5f, p.Pivot.y + 0.5f);
			var front = ImportSprite(SourceDir + "/" + p.Name + ".png", pivot01);
			var back = p.HasBack ? ImportSprite(SourceDir + "/" + p.Name + "_Back.png", pivot01) : null;
			var root = new GameObject("Placement " + number);
			root.transform.position = new Vector3(i * Spacing, 0f, 0f);
			var pigObject = SpriteObject("Pig (fixed)", pig, 1, root.transform);
			hidden.Add(pigObject);
			var bucket = SpriteObject("Bucket " + number, front, 2, root.transform);
			Vector2 offset;
			if (offsets.TryGetValue(number, out offset))
			{
				bucket.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
			}
			buckets.Add(bucket);
			if (back != null)
			{
				hidden.Add(SpriteObject("Back (follows bucket)", back, 0, bucket.transform));
			}
			var label = new GameObject("Label");
			label.transform.SetParent(root.transform, false);
			label.transform.localPosition = new Vector3(-150f, -150f, 0f);
			var text = label.AddComponent<TextMesh>();
			text.font = font;
			text.fontSize = 64;
			text.characterSize = 4f;
			text.color = Color.black;
			text.text = "Placement " + number + "  (" + Chances[Mathf.Min(i, Chances.Length - 1)] + "%)";
			label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
			hidden.Add(label);
		}
		var cameraObject = new GameObject("Camera");
		var cam = cameraObject.AddComponent<Camera>();
		cam.orthographic = true;
		cam.orthographicSize = 260f;
		cam.clearFlags = CameraClearFlags.SolidColor;
		cam.backgroundColor = new Color(0.7f, 0.7f, 0.7f);
		cam.transform.position = new Vector3((placements.Count - 1) * Spacing / 2f, 40f, -1000f);
		cam.farClipPlane = 5000f;
		hidden.Add(cameraObject);
		EditorSceneManager.SaveScene(scene, ScenePath);
		// only the buckets can be clicked, so a drag always moves the whole bucket (back layer included)
		foreach (var go in hidden)
		{
			SceneVisibilityManager.instance.DisablePicking(go, true);
		}
		foreach (var bucket in buckets)
		{
			SceneVisibilityManager.instance.EnablePicking(bucket, false);
		}
		Selection.activeGameObject = buckets.FirstOrDefault();
		if (SceneView.lastActiveSceneView != null)
		{
			SceneView.lastActiveSceneView.in2DMode = true;
			SceneView.lastActiveSceneView.Frame(new Bounds(cam.transform.position, new Vector3(placements.Count * Spacing, 520f, 10f)), false);
		}
		Log("built " + ScenePath + " with " + placements.Count + " placements");
	}

	// New art from bucket.py without rebuilding the scene: re-imports the sprites in place, so bucket positions in the
	// open scene (applied or not) stay as they are.
	[MenuItem("Tools/Pirate Bucket/Refresh Bucket Art")]
	public static void RefreshSprites()
	{
		var placements = ReadPivots();
		var sceneChanged = false;
		for (var i = 0; i < placements.Count; i++)
		{
			var p = placements[i];
			var pivot01 = new Vector2(p.Pivot.x + 0.5f, p.Pivot.y + 0.5f);
			var front = ImportSprite(SourceDir + "/" + p.Name + ".png", pivot01);
			var bucket = GameObject.Find("Placement " + (i + 1) + "/Bucket " + (i + 1));
			var backObject = bucket != null ? bucket.transform.Find("Back (follows bucket)") : null;
			if (p.HasBack)
			{
				var back = ImportSprite(SourceDir + "/" + p.Name + "_Back.png", pivot01);
				if (bucket != null && backObject == null)
				{
					SceneVisibilityManager.instance.DisablePicking(SpriteObject("Back (follows bucket)", back, 0, bucket.transform), true);
					sceneChanged = true;
				}
			}
			else
			{
				// the placement lost its back layer: remove the old sprite and its object
				AssetDatabase.DeleteAsset(Folder + "/" + p.Name + "_Back.png");
				if (backObject != null)
				{
					Object.DestroyImmediate(backObject.gameObject);
					sceneChanged = true;
				}
			}
			if (bucket != null && bucket.GetComponent<SpriteRenderer>().sprite != front)
			{
				bucket.GetComponent<SpriteRenderer>().sprite = front;
				sceneChanged = true;
			}
		}
		if (sceneChanged)
		{
			var scene = EditorSceneManager.GetActiveScene();
			EditorSceneManager.MarkSceneDirty(scene);
			EditorSceneManager.SaveScene(scene);
		}
		Log("refreshed " + placements.Count + " bucket sprites");
	}

	// Records each bucket's position and rotation, re-renders the art with the rotation baked in (bucket.py: the water
	// levels and the handle re-settles for the new tilt), repacks the atlas with the positions as pivot shifts (pack.py),
	// then resets the buckets' rotation here since the art now carries it, and saves the scene.
	[MenuItem("Tools/Pirate Bucket/Apply Dragged Positions")]
	public static void Apply()
	{
		var placements = ReadPivots();
		var stored = ReadRotations();
		var lines = new List<string>();
		var buckets = new List<GameObject>();
		for (var i = 0; i < placements.Count; i++)
		{
			var number = i + 1;
			var bucket = GameObject.Find("Placement " + number + "/Bucket " + number);
			if (bucket == null)
			{
				Log("Bucket " + number + " not found; open " + ScenePath);
				return;
			}
			buckets.Add(bucket);
			if (bucket.transform.localScale != Vector3.one)
			{
				Log("Bucket " + number + ": scale is ignored");
			}
			var offset = (Vector2)bucket.transform.localPosition;
			float before;
			stored.TryGetValue(number, out before);
			var rotation = before + Mathf.DeltaAngle(0f, bucket.transform.localEulerAngles.z);
			lines.Add(number + " " + offset.x.ToString("0.###", CultureInfo.InvariantCulture) + " " + offset.y.ToString("0.###", CultureInfo.InvariantCulture) + " " + rotation.ToString("0.###", CultureInfo.InvariantCulture));
		}
		File.WriteAllLines(OffsetsFile, lines.ToArray());
		try
		{
			EditorUtility.DisplayProgressBar("Pirate Bucket", "Re-rendering the buckets", 0.3f);
			if (!RunPython("bucket.py"))
			{
				return;
			}
			EditorUtility.DisplayProgressBar("Pirate Bucket", "Packing the atlas", 0.7f);
			if (!RunPython("pack.py"))
			{
				return;
			}
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}
		foreach (var atlasPath in Atlases)
		{
			AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
		}
		AssetDatabase.Refresh();
		RefreshSprites();
		foreach (var bucket in buckets)
		{
			bucket.transform.localRotation = Quaternion.identity;
		}
		var scene = buckets[0].scene;
		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);
		Log("applied " + string.Join(", ", lines.ToArray()) + " (number dx dy rotation)");
	}
}
