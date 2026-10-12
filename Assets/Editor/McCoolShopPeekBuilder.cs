using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Turns the hand-placed Piggy McCool peek sprites (Window_WorldShop: 3_MightyEagle/Animation/4_PiggMcCool)
// into NGUI widgets on the board, layered between the header sign and the board body, with a minion-pig
// style idle bob. Touch Temp/McCoolShopPeek.request in an open editor to run it ("preview" = render only).
public static class McCoolShopPeekBuilder
{
	private const string ScenePath = "Assets/Scenes/1_WorldMap/2_Interface/Window_WorldShop.unity";

	private const string RequestFile = "Temp/McCoolShopPeek.request";

	private const string LogFile = "Temp/McCoolShopPeek.log";

	private const string ClipPath = "Assets/AnimationClip/McCoolPeek_Idle.anim";

	private const string EyebrowLeftPath = "Assets/content/4_Characters/8_Base/1_MinionPig/BodyParts/Eyebrow_Left.png";

	private const string EyebrowRightPath = "Assets/content/4_Characters/8_Base/1_MinionPig/BodyParts/Eyebrow_Right.png";

	private const string PropName = "5_McCoolPeek";

	// Board-body y of McCool's bottom edge as first built from the hand-placed sprites.
	private const float BuiltOriginY = 155f;

	// Board depths, back to front. The header used to share -41 with the body planks, which put both in
	// one draw call; giving it its own depth lets McCool sit between them.
	private const int PoleDepth = -46;

	private const int HeaderDepth = -45;

	private const int BodyDepth = -44;

	private const int EyeDepth = -43;

	private const int EyebrowDepth = -42;

	// Above the offer list panel (17) so the beak hangs over everything in the window.
	private const int NosePanelDepth = 18;

	// Eyebrow centres relative to the eye centres, before the head tilt. The rig's offsets (-7,23)/(7,24)
	// float off this art's head, whose eyes sit right under the hood brim, so the brows rest on the brim.
	// "eyebrows lx ly rx ry" in the request file overrides these without a recompile.
	private static Vector2 EyebrowLeftOffset = new Vector2(1f, 12f);

	private static Vector2 EyebrowRightOffset = new Vector2(0f, 12f);

	private const float EyebrowLeftAngle = 35f;

	private const float EyebrowRightAngle = -35f;

	// The art's eyes are ~1.3x the rig's (16/22 wide vs 21/28), so the brows scale with them.
	private const float EyebrowScale = 1.4f;

	private static StringBuilder s_Log;

	[InitializeOnLoadMethod]
	private static void WatchForRequests()
	{
		EditorApplication.update -= PollRequest;
		EditorApplication.update += PollRequest;
	}

	private static void PollRequest()
	{
		if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
		{
			return;
		}
		var args = File.ReadAllText(RequestFile).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		File.Delete(RequestFile);
		var mode = args.Length > 0 ? args[0] : "build";
		if (args.Length == 5)
		{
			var v = args.Skip(1).Select(a => float.Parse(a, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
			EyebrowLeftOffset = new Vector2(v[0], v[1]);
			EyebrowRightOffset = new Vector2(v[2], v[3]);
		}
		Run(mode);
	}

	[MenuItem("Tools/McCool Shop Peek/Build")]
	public static void Build()
	{
		Run("build");
	}

	// mode: "build" (from 4_PiggMcCool), "eyebrows" (re-place brows from the eye widgets), "preview".
	private static void Run(string mode)
	{
		var previewOnly = mode == "preview";
		s_Log = new StringBuilder();
		var opened = false;
		var scene = SceneManager.GetSceneByPath(ScenePath);
		try
		{
			if (!scene.IsValid() || !scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
				opened = true;
			}
			else if (scene.isDirty && !previewOnly)
			{
				Log("ABORT: Window_WorldShop has unsaved changes in the editor; save it and request again.");
				return;
			}
			var root = scene.GetRootGameObjects().First(g => g.name == "Window_WorldShop");
			if (mode == "bob")
			{
				var prop = root.transform.Find("1_Board/Animation/0_Body/" + PropName);
				// Re-align the body with the face (it had been dragged 50 units down) at the built origin.
				prop.localPosition = new Vector3(prop.localPosition.x, BuiltOriginY, prop.localPosition.z);
				prop.Find("Body").localPosition = Vector3.zero;
				AddBob(prop.gameObject);
				EditorSceneManager.MarkSceneDirty(scene);
				EditorSceneManager.SaveScene(scene);
				Log("saved " + ScenePath);
			}
			else if (mode == "eyebrows")
			{
				var face = root.transform.Find("1_Board/Animation/0_Body/" + PropName + "/Face");
				PlaceEyebrows(face, face.Find("Eye_Left").localPosition, face.Find("Eye_Right").localPosition);
				EditorSceneManager.MarkSceneDirty(scene);
				EditorSceneManager.SaveScene(scene);
				Log("saved " + ScenePath);
			}
			else if (!previewOnly)
			{
				BuildProp(root);
				EditorSceneManager.MarkSceneDirty(scene);
				EditorSceneManager.SaveScene(scene);
				Log("saved " + ScenePath);
			}
			RenderPreviews(scene, root);
		}
		catch (Exception e)
		{
			Log("EXCEPTION " + e);
			Debug.LogException(e);
		}
		finally
		{
			if (opened && scene.IsValid())
			{
				EditorSceneManager.CloseScene(scene, true);
			}
			Log("done");
			File.WriteAllText(LogFile, s_Log.ToString());
			Debug.Log("[McCoolShopPeekBuilder]\n" + s_Log);
		}
	}

	private static void Log(string line)
	{
		s_Log.AppendLine(line);
	}

	private static void BuildProp(GameObject root)
	{
		var boardBody = root.transform.Find("1_Board/Animation/0_Body");
		var source = root.transform.Find("3_MightyEagle/Animation/4_PiggMcCool");
		if (source == null)
		{
			throw new Exception("4_PiggMcCool sprites not found; the prop was already built (edit " + PropName + " directly).");
		}
		SeparateHeader(boardBody);

		// Visual centres of the hand-placed sprites, in board-body space.
		Func<string, SpriteRenderer> piece = name =>
		{
			var t = source.Find(name);
			if (t == null)
			{
				throw new Exception("missing " + name + " under 4_PiggMcCool");
			}
			return t.GetComponent<SpriteRenderer>();
		};
		var body = piece("mccoolpeekbodynoeye");
		var eyeLeft = piece("mccoolpeekeyeleft");
		var eyeRight = piece("mccoolpeekeyeright");
		var nose = piece("mccoolpeeknose");
		Func<SpriteRenderer, Vector2> centre = r => boardBody.InverseTransformPoint(r.bounds.center);
		Func<SpriteRenderer, Vector2> size = r => new Vector2(r.sprite.rect.width, r.sprite.rect.height);

		var bodySize = size(body);
		// The prop's origin is McCool's bottom edge: the body squashes from there like the rig's Body bone.
		var origin = centre(body) - new Vector2(0f, bodySize.y * 0.5f);
		Func<SpriteRenderer, Vector2> local = r => centre(r) - origin;
		Log("origin " + origin + " body " + local(body) + " eyeL " + local(eyeLeft) + " eyeR " + local(eyeRight) + " nose " + local(nose));

		var old = boardBody.Find(PropName);
		if (old != null)
		{
			UnityEngine.Object.DestroyImmediate(old.gameObject);
		}
		var layer = boardBody.gameObject.layer;
		var prop = NewChild(boardBody, PropName, origin, layer);

		var bodyGo = NewChild(prop.transform, "Body", Vector2.zero, layer);
		AddTexture(bodyGo, body.sprite.texture, bodySize, UIWidget.Pivot.Bottom, BodyDepth);

		var face = NewChild(prop.transform, "Face", Vector2.zero, layer);
		var eyeLeftPos = local(eyeLeft);
		var eyeRightPos = local(eyeRight);
		AddTexture(NewChild(face.transform, "Eye_Left", eyeLeftPos, layer), eyeLeft.sprite.texture, size(eyeLeft), UIWidget.Pivot.Center, EyeDepth);
		AddTexture(NewChild(face.transform, "Eye_Right", eyeRightPos, layer), eyeRight.sprite.texture, size(eyeRight), UIWidget.Pivot.Center, EyeDepth);

		PlaceEyebrows(face.transform, eyeLeftPos, eyeRightPos);

		// The beak gets its own panel so it draws over the board, its labels and the offer list.
		var nosePos = local(nose);
		var nosePanel = NewChild(face.transform, "NosePanel", nosePos, layer);
		nosePanel.AddComponent<UIPanel>().depth = NosePanelDepth;
		AddTexture(NewChild(nosePanel.transform, "Nose", Vector2.zero, layer), nose.sprite.texture, size(nose), UIWidget.Pivot.Center, 0);

		AddBob(prop);
		prop.SetActive(false);

		var menu = new SerializedObject(root.GetComponent<WorldMapShopMenuUI>());
		menu.FindProperty("m_McCoolProp").objectReferenceValue = prop;
		menu.ApplyModifiedPropertiesWithoutUndo();

		UnityEngine.Object.DestroyImmediate(source.gameObject);
		Log("built " + PropName + " under " + boardBody.name + "; removed 4_PiggMcCool");
	}

	// His head is tilted in the art; measure it from the eyes and turn the rig's eyebrow layout with it.
	private static void PlaceEyebrows(Transform face, Vector2 eyeLeftPos, Vector2 eyeRightPos)
	{
		var eyeLine = eyeRightPos - eyeLeftPos;
		var tilt = Mathf.Atan2(eyeLine.y, eyeLine.x) * Mathf.Rad2Deg;
		Log("head tilt " + tilt.ToString("0.0") + " deg");
		PlaceEyebrow(face, "Eyebrow_Left", EyebrowLeftPath, eyeLeftPos + Rotate(EyebrowLeftOffset, tilt), EyebrowLeftAngle + tilt);
		PlaceEyebrow(face, "Eyebrow_Right", EyebrowRightPath, eyeRightPos + Rotate(EyebrowRightOffset, tilt), EyebrowRightAngle + tilt);
	}

	private static void PlaceEyebrow(Transform face, string name, string texturePath, Vector2 position, float angle)
	{
		var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
		var existing = face.Find(name);
		var go = existing != null ? existing.gameObject : NewChild(face, name, position, face.gameObject.layer);
		go.transform.localPosition = new Vector3(position.x, position.y, 0f);
		go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
		var widget = go.GetComponent<UITexture>();
		if (widget == null)
		{
			AddTexture(go, texture, new Vector2(texture.width, texture.height) * EyebrowScale, UIWidget.Pivot.Center, EyebrowDepth);
		}
		else
		{
			widget.width = Mathf.RoundToInt(texture.width * EyebrowScale);
			widget.height = Mathf.RoundToInt(texture.height * EyebrowScale);
		}
		Log("  " + name + " at " + position + " rot " + angle.ToString("0.0") + " size " + (texture.width * EyebrowScale) + "x" + (texture.height * EyebrowScale));
	}

	// The bob is a script working on top of each piece's rest pose (a clip would pin the pieces' positions).
	private static void AddBob(GameObject prop)
	{
		var animation = prop.GetComponent<Animation>();
		if (animation != null)
		{
			UnityEngine.Object.DestroyImmediate(animation);
		}
		if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath) != null)
		{
			AssetDatabase.DeleteAsset(ClipPath);
		}
		var bob = prop.GetComponent<PeekIdleBob>() ?? prop.AddComponent<PeekIdleBob>();
		var data = new SerializedObject(bob);
		data.FindProperty("m_Body").objectReferenceValue = prop.transform.Find("Body");
		data.FindProperty("m_Face").objectReferenceValue = prop.transform.Find("Face");
		data.FindProperty("m_Nose").objectReferenceValue = prop.transform.Find("Face/NosePanel");
		data.ApplyModifiedPropertiesWithoutUndo();
		Log("bob on " + prop.name + " speed " + bob.m_Speed);
	}

	private static void SeparateHeader(Transform boardBody)
	{
		foreach (var sprite in boardBody.GetComponentsInChildren<UISprite>(true).Where(s => s.transform.parent == boardBody))
		{
			if (sprite.name.StartsWith("Pole_") || sprite.name.StartsWith("Foot_"))
			{
				sprite.depth = PoleDepth;
			}
			else if (sprite.name == "Header")
			{
				sprite.depth = HeaderDepth;
			}
			Log("  board " + sprite.name + " depth " + sprite.depth);
		}
	}

	private static GameObject NewChild(Transform parent, string name, Vector2 position, int layer)
	{
		var go = new GameObject(name);
		go.layer = layer;
		go.transform.SetParent(parent, false);
		go.transform.localPosition = new Vector3(position.x, position.y, 0f);
		return go;
	}

	private static void AddTexture(GameObject go, Texture texture, Vector2 size, UIWidget.Pivot pivot, int depth)
	{
		var widget = go.AddComponent<UITexture>();
		widget.mainTexture = texture;
		widget.pivot = pivot;
		widget.width = Mathf.RoundToInt(size.x);
		widget.height = Mathf.RoundToInt(size.y);
		widget.depth = depth;
	}

	private static Vector2 Rotate(Vector2 v, float degrees)
	{
		var r = degrees * Mathf.Deg2Rad;
		return new Vector2(v.x * Mathf.Cos(r) - v.y * Mathf.Sin(r), v.x * Mathf.Sin(r) + v.y * Mathf.Cos(r));
	}

	private static void RenderPreviews(Scene scene, GameObject root)
	{
		var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c => c.name == "Camera_Preview");
		var prop = root.transform.Find("1_Board/Animation/0_Body/" + PropName);
		if (camera == null || prop == null)
		{
			Log("no preview camera or prop");
			return;
		}
		// Show the window as it looks after its enter animations, with McCool on.
		var moved = new List<KeyValuePair<Transform, Vector3>>();
		foreach (var path in new[] { "1_Board/Animation", "2_Ground/Animation", "3_Back/Animation" })
		{
			var t = root.transform.Find(path);
			if (t != null)
			{
				moved.Add(new KeyValuePair<Transform, Vector3>(t, t.localPosition));
				t.localPosition = Vector3.zero;
			}
		}
		var eagle = root.transform.Find("3_MightyEagle");
		var eagleWasActive = eagle != null && eagle.gameObject.activeSelf;
		if (eagle != null)
		{
			eagle.gameObject.SetActive(false);
		}
		var wasActive = prop.gameObject.activeSelf;
		prop.gameObject.SetActive(true);
		var body = prop.Find("Body");
		var face = prop.Find("Face");
		var bodyScale = body.localScale;
		var facePos = face.localPosition;
		try
		{
			RenderTo(camera, root, "Temp/McCoolShopPeek_Rest.png");
			// Peak of the bob (nose omitted): same offsets PeekIdleBob applies.
			body.localScale = Vector3.Scale(bodyScale, new Vector3(1.05f, 0.92f, 1f));
			face.localPosition = facePos + new Vector3(1f, -4f, 0f);
			RenderTo(camera, root, "Temp/McCoolShopPeek_Squash.png");
		}
		finally
		{
			body.localScale = bodyScale;
			face.localPosition = facePos;
			prop.gameObject.SetActive(wasActive);
			if (eagle != null)
			{
				eagle.gameObject.SetActive(eagleWasActive);
			}
			foreach (var pair in moved)
			{
				pair.Key.localPosition = pair.Value;
			}
		}
	}

	// Same NGUI-in-edit-mode render as EquipmentPresetSceneBuilder.RenderTo.
	private static void RenderTo(Camera camera, GameObject root, string path)
	{
		var updateFrame = typeof(UIPanel).GetField("mUpdateFrame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
		var lateUpdate = typeof(UIPanel).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
		foreach (var rect in root.GetComponentsInChildren<UIRect>().OrderBy(r => r is UIPanel ? 0 : 1))
		{
			foreach (var method in new[] { "OnEnable", "Start", "Update" })
			{
				var m = rect.GetType().GetMethod(method, flags, null, Type.EmptyTypes, null);
				if (m != null)
				{
					try { m.Invoke(rect, null); } catch (Exception) { }
				}
			}
		}
		foreach (var widget in root.GetComponentsInChildren<UIWidget>())
		{
			widget.MarkAsChanged();
		}
		for (var pass = 0; pass < 3 && UIPanel.list.Count > 0; pass++)
		{
			foreach (var panel in UIPanel.list.ToList())
			{
				updateFrame.SetValue(updateFrame.IsStatic ? null : panel, -1);
			}
			lateUpdate.Invoke(UIPanel.list[0], null);
		}
		foreach (var panel in root.GetComponentsInChildren<UIPanel>())
		{
			Log("  panel " + panel.name + " depth " + panel.depth + " drawcalls " + panel.drawCalls.Count + " rq " + panel.startingRenderQueue);
			foreach (var dc in panel.drawCalls)
			{
				Log("    rq " + dc.renderQueue + " depth " + dc.depthStart + ".." + dc.depthEnd + " tex " + (dc.mainTexture != null ? dc.mainTexture.name : "-"));
			}
		}
		var hidden = new List<Renderer>();
		foreach (var drawCall in UIDrawCall.activeList)
		{
			var renderer = drawCall != null ? drawCall.GetComponent<Renderer>() : null;
			if (renderer != null && renderer.enabled && (drawCall.manager == null || !drawCall.manager.transform.IsChildOf(root.transform)))
			{
				renderer.enabled = false;
				hidden.Add(renderer);
			}
		}
		var texture = new RenderTexture(1365, 768, 24);
		var previous = camera.targetTexture;
		var mask = camera.cullingMask;
		camera.targetTexture = texture;
		camera.cullingMask = 1 << root.layer;
		camera.clearFlags = CameraClearFlags.SolidColor;
		camera.backgroundColor = new Color(0.2f, 0.25f, 0.3f);
		camera.Render();
		RenderTexture.active = texture;
		var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
		image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
		image.Apply();
		File.WriteAllBytes(path, image.EncodeToPNG());
		RenderTexture.active = null;
		camera.targetTexture = previous;
		camera.cullingMask = mask;
		UnityEngine.Object.DestroyImmediate(image);
		texture.Release();
		UnityEngine.Object.DestroyImmediate(texture);
		foreach (var renderer in hidden)
		{
			renderer.enabled = true;
		}
		Log("preview " + path);
	}
}
