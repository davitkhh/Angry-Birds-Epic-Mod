using System.Collections.Generic;
using ABH.GameDatas.Battle;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// One pig changing type under a swap crystal (see SwapCrystalDirector). Begin() swaps the pig's model and rig right
// away; for the look, both rigs keep animating but are hidden from the battle camera, rendered each frame into their
// own texture, and shown through one quad (shader "ABH/Swap Crystal Pig Morph") that cross-fades old into new under a
// crystal-colored gradient fill covering the whole body (not its faint ground shadow), with a lighter border around
// it. The health bar belongs to the view, not the rig, so it never changes.
public class PigSwapMorph : MonoBehaviour
{
	// No camera renders this layer: the rigs live here while the quad shows them.
	private const int HiddenLayer = 30;

	// Only the capture camera renders this layer; each rig is moved onto it just for its own capture.
	private const int CaptureLayer = 31;

	private const float PaddingFraction = 0.12f;

	private const float BorderWorldWidth = 4f;

	private const int MaxTextureSize = 1024;

	// Fill opacity inside 70% of the gradient radius and from 90% out (20% transparency).
	private const float InnerOpacity = 0.65f;

	private const float OuterOpacity = 0.8f;

	private class Rig
	{
		public CharacterAssetController Asset;

		public Renderer[] Renderers;

		public int[] Layers;

		public bool[] UpdateWhenOffscreen;

		public Animation[] Animations;

		public AnimationCullingType[] Culling;

		public RenderTexture Texture;

		public Vector4 Ellipse = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
	}

	private CharacterControllerBattleGroundBase m_View;

	private string m_OldAssetId;

	private Rig m_Old;

	private Rig m_New;

	private Vector3 m_NewBaseScale;

	private int m_BeginFrame;

	private bool m_Capturing;

	private Camera m_SceneryCamera;

	private Camera m_CaptureCamera;

	// Capture area in world space (bottom-left corner and size).
	private Vector2 m_CaptureMin;

	private Vector2 m_CaptureSize;

	private GameObject m_Quad;

	private Material m_Material;

	private Material m_MaterialTemplate;

	public float Morph { get; set; }

	public float Strength { get; set; }

	public float ColorT { get; set; }

	private Color[] m_FromColors;

	private Color[] m_ToColors;

	public static PigSwapMorph Begin(PigCombatant pig, string newPigNameId, BattleMgrBase mgr, Material morphMaterial, Color[] fromColors, Color[] toColors)
	{
		var view = pig.CombatantView;
		var morph = new GameObject("PigSwapMorph_" + pig.CombatantNameId).AddComponent<PigSwapMorph>();
		morph.m_View = view;
		morph.m_MaterialTemplate = morphMaterial;
		morph.m_FromColors = fromColors;
		morph.m_ToColors = toColors;
		morph.m_OldAssetId = pig.CombatantAssetId;
		morph.m_BeginFrame = Time.frameCount;
		morph.m_SceneryCamera = FindSceneryCamera(view.gameObject.layer);

		var oldScale = pig.CharacterModel.Scale;
		pig.SwapForm(newPigNameId, mgr.Model);
		var oldAsset = view.m_AssetController;
		var newAsset = DIContainerInfrastructure.GetCharacterAssetProvider(false).InstantiateObject(pig.CombatantAssetId, view.transform, Vector3.zero, Quaternion.identity).GetComponent<CharacterAssetController>();
		newAsset.SetModel(pig.CharacterModel, false);
		// As on spawn (BattleMgrBase.PlaceCharacter): some rigs' hand-placed gear (e.g. PigKnightYellow) is saved on
		// the Default layer, which the battle camera doesn't draw.
		UnityHelper.SetLayerRecusively(newAsset.gameObject, view.gameObject.layer);
		newAsset.transform.localPosition = oldAsset.transform.localPosition;
		newAsset.transform.localRotation = oldAsset.transform.localRotation;
		// The view keeps its scale (the health bar is its child); the rig makes up the new type's size difference.
		var sizeRatio = oldScale > 0f ? pig.CharacterModel.Scale / oldScale : 1f;
		var oldScaler = oldAsset.GetComponent<ScaleController>();
		var newScaler = newAsset.GetComponent<ScaleController>();
		if (oldScaler != null && newScaler != null)
		{
			newScaler.m_BaseScale = new Vector3(oldScaler.m_BaseScale.x * sizeRatio, oldScaler.m_BaseScale.y * sizeRatio, oldScaler.m_BaseScale.z);
		}
		newAsset.transform.localScale = new Vector3(oldAsset.transform.localScale.x * sizeRatio, oldAsset.transform.localScale.y * sizeRatio, oldAsset.transform.localScale.z);
		view.SwapAssetController(newAsset);
		if (pig.IsStunned)
		{
			newAsset.PlayStunnedAnimation();
		}
		MoveAttachedEffects(view, oldAsset, newAsset);

		morph.m_Old = morph.WrapRig(oldAsset);
		morph.m_New = morph.WrapRig(newAsset);
		// The old rig stays visible until the capture is set up (next frame); the new one never shows directly.
		SetLayer(morph.m_New, HiddenLayer);
		return morph;
	}

	private static Camera FindSceneryCamera(int layer)
	{
		foreach (var camera in Camera.allCameras)
		{
			if (camera.orthographic && camera.targetTexture == null && (camera.cullingMask & (1 << layer)) != 0)
			{
				return camera;
			}
		}
		return Camera.main;
	}

	// Status effects and other FX parented to the old rig's bones move to the same bones of the new rig.
	private static void MoveAttachedEffects(CharacterControllerBattleGroundBase view, CharacterAssetController oldAsset, CharacterAssetController newAsset)
	{
		var attached = new List<Transform>();
		CollectAttachedEffects(view, oldAsset.transform, attached);
		foreach (var effect in attached)
		{
			var bone = FindDeep(newAsset.transform, effect.parent.name);
			if (bone == null)
			{
				bone = newAsset.BodyRoot != null ? newAsset.BodyRoot : newAsset.transform;
			}
			effect.SetParent(bone, false);
		}
	}

	private static void CollectAttachedEffects(CharacterControllerBattleGroundBase view, Transform parent, List<Transform> attached)
	{
		foreach (Transform child in parent)
		{
			if (view.LastingVisualEffects.ContainsValue(child.gameObject) || child.GetComponent<BattleFXController>() != null)
			{
				attached.Add(child);
			}
			else
			{
				CollectAttachedEffects(view, child, attached);
			}
		}
	}

	private static Transform FindDeep(Transform root, string name)
	{
		if (root.name == name)
		{
			return root;
		}
		foreach (Transform child in root)
		{
			var found = FindDeep(child, name);
			if (found != null)
			{
				return found;
			}
		}
		return null;
	}

	private Rig WrapRig(CharacterAssetController asset)
	{
		var rig = new Rig { Asset = asset, Renderers = asset.GetComponentsInChildren<Renderer>(true) };
		rig.Layers = new int[rig.Renderers.Length];
		rig.UpdateWhenOffscreen = new bool[rig.Renderers.Length];
		for (var i = 0; i < rig.Renderers.Length; i++)
		{
			rig.Layers[i] = rig.Renderers[i].gameObject.layer;
			var skinned = rig.Renderers[i] as SkinnedMeshRenderer;
			if (skinned != null)
			{
				rig.UpdateWhenOffscreen[i] = skinned.updateWhenOffscreen;
				skinned.updateWhenOffscreen = true;
			}
		}
		// The rigs are on layers the battle camera doesn't see: keep them animating regardless.
		rig.Animations = asset.GetComponentsInChildren<Animation>(true);
		rig.Culling = new AnimationCullingType[rig.Animations.Length];
		for (var i = 0; i < rig.Animations.Length; i++)
		{
			rig.Culling[i] = rig.Animations[i].cullingType;
			rig.Animations[i].cullingType = AnimationCullingType.AlwaysAnimate;
		}
		return rig;
	}

	private static void SetLayer(Rig rig, int layer)
	{
		foreach (var renderer in rig.Renderers)
		{
			if (renderer)
			{
				renderer.gameObject.layer = layer;
			}
		}
	}

	private void LateUpdate()
	{
		if (!m_Capturing)
		{
			// Wait a frame so the new rig has animated once and its bounds are real.
			if (Time.frameCount <= m_BeginFrame || !StartCapture())
			{
				return;
			}
		}
		// A form only needs capturing while it's part of the cross-fade.
		if (Morph < 1f)
		{
			Capture(m_Old);
		}
		if (Morph > 0f)
		{
			Capture(m_New);
		}
		m_Material.SetFloat("_Morph", Morph);
		m_Material.SetFloat("_Strength", Strength);
		m_Material.SetFloat("_ColorT", ColorT);
	}

	private bool StartCapture()
	{
		if (m_SceneryCamera == null || !m_Old.Asset || !m_New.Asset)
		{
			return false;
		}
		var bounds = default(Bounds);
		var hasBounds = false;
		foreach (var rig in new[] { m_Old, m_New })
		{
			foreach (var renderer in rig.Renderers)
			{
				if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
				{
					continue;
				}
				if (hasBounds)
				{
					bounds.Encapsulate(renderer.bounds);
				}
				else
				{
					bounds = renderer.bounds;
					hasBounds = true;
				}
			}
		}
		if (!hasBounds)
		{
			return false;
		}
		var size = (Vector2)bounds.size;
		var padding = Mathf.Max(size.x, size.y) * PaddingFraction + BorderWorldWidth * 2f;
		size += new Vector2(padding * 2f, padding * 2f);
		var center = bounds.center;
		m_CaptureMin = (Vector2)center - size * 0.5f;
		m_CaptureSize = size;

		// One texture pixel per screen pixel, so the quad shows the rigs as sharp as the battle camera would.
		var pixelsPerUnit = m_SceneryCamera.pixelHeight / (2f * m_SceneryCamera.orthographicSize);
		var width = Mathf.Clamp(Mathf.CeilToInt(size.x * pixelsPerUnit), 16, MaxTextureSize);
		var height = Mathf.Clamp(Mathf.CeilToInt(size.y * pixelsPerUnit), 16, MaxTextureSize);
		m_Old.Texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
		m_New.Texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);

		var cameraObject = new GameObject("CaptureCamera");
		cameraObject.transform.parent = base.transform;
		cameraObject.transform.position = new Vector3(center.x, center.y, m_SceneryCamera.transform.position.z);
		cameraObject.transform.rotation = m_SceneryCamera.transform.rotation;
		m_CaptureCamera = cameraObject.AddComponent<Camera>();
		m_CaptureCamera.enabled = false;
		m_CaptureCamera.orthographic = true;
		m_CaptureCamera.orthographicSize = size.y * 0.5f;
		m_CaptureCamera.aspect = size.x / size.y;
		m_CaptureCamera.nearClipPlane = m_SceneryCamera.nearClipPlane;
		m_CaptureCamera.farClipPlane = m_SceneryCamera.farClipPlane;
		m_CaptureCamera.clearFlags = CameraClearFlags.SolidColor;
		m_CaptureCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
		m_CaptureCamera.cullingMask = 1 << CaptureLayer;

		m_Material = new Material(m_MaterialTemplate);
		m_Material.SetTexture("_OldTex", m_Old.Texture);
		m_Material.SetTexture("_NewTex", m_New.Texture);
		m_Material.SetColor("_DarkFrom", m_FromColors[0]);
		m_Material.SetColor("_LightFrom", m_FromColors[1]);
		m_Material.SetColor("_BorderFrom", m_FromColors[2]);
		m_Material.SetColor("_DarkTo", m_ToColors[0]);
		m_Material.SetColor("_LightTo", m_ToColors[1]);
		m_Material.SetColor("_BorderTo", m_ToColors[2]);
		m_Material.SetFloat("_InnerOpacity", InnerOpacity);
		m_Material.SetFloat("_OuterOpacity", OuterOpacity);
		m_Material.SetFloat("_OutlineWidth", BorderWorldWidth * pixelsPerUnit);

		m_Quad = new GameObject("MorphQuad");
		m_Quad.layer = m_View.gameObject.layer;
		m_Quad.transform.parent = base.transform;
		// At the rigs' depth, so other pigs and effects still sort in front of or behind it as before.
		m_Quad.transform.position = center;
		m_Quad.transform.rotation = m_SceneryCamera.transform.rotation;
		var half = size * 0.5f;
		var mesh = new Mesh
		{
			name = "PigSwapMorph",
			vertices = new[] { new Vector3(0f - half.x, 0f - half.y, 0f), new Vector3(0f - half.x, half.y, 0f), new Vector3(half.x, half.y, 0f), new Vector3(half.x, 0f - half.y, 0f) },
			uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) },
			triangles = new[] { 0, 1, 2, 0, 2, 3 }
		};
		mesh.RecalculateBounds();
		m_Quad.AddComponent<MeshFilter>().sharedMesh = mesh;
		m_Quad.AddComponent<MeshRenderer>().sharedMaterial = m_Material;

		SetLayer(m_Old, HiddenLayer);
		SetLayer(m_New, HiddenLayer);
		m_Capturing = true;

		// The fill's gradient spans each form's actual body, not the padded capture area. Until the captures are read
		// back (asynchronously: a blocking read per pig stalled the frame), the renderers' bounds stand in.
		Capture(m_Old);
		Capture(m_New);
		m_Old.Ellipse = BoundsEllipse(m_Old);
		m_New.Ellipse = BoundsEllipse(m_New);
		ApplyEllipses();
		RequestSilhouetteEllipse(m_Old);
		RequestSilhouetteEllipse(m_New);
		return true;
	}

	private void ApplyEllipses()
	{
		if (m_Material != null)
		{
			m_Material.SetVector("_EllipseOld", m_Old.Ellipse);
			m_Material.SetVector("_EllipseNew", m_New.Ellipse);
		}
	}

	// Ellipse (center xy, radii zw, in uv) inscribed in the rig's renderer bounds; looser than the silhouette.
	private Vector4 BoundsEllipse(Rig rig)
	{
		var bounds = default(Bounds);
		var hasBounds = false;
		foreach (var renderer in rig.Renderers)
		{
			if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
			{
				continue;
			}
			if (hasBounds)
			{
				bounds.Encapsulate(renderer.bounds);
			}
			else
			{
				bounds = renderer.bounds;
				hasBounds = true;
			}
		}
		if (!hasBounds)
		{
			return new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
		}
		return new Vector4((bounds.center.x - m_CaptureMin.x) / m_CaptureSize.x, (bounds.center.y - m_CaptureMin.y) / m_CaptureSize.y, bounds.extents.x / m_CaptureSize.x, bounds.extents.y / m_CaptureSize.y);
	}

	private void RequestSilhouetteEllipse(Rig rig)
	{
		if (!SystemInfo.supportsAsyncGPUReadback)
		{
			return;
		}
		var width = rig.Texture.width;
		var height = rig.Texture.height;
		AsyncGPUReadback.Request(rig.Texture, 0, TextureFormat.RGBA32, request =>
		{
			if (this == null || request.hasError)
			{
				return;
			}
			rig.Ellipse = SilhouetteEllipse(request.GetData<Color32>(), width, height);
			ApplyEllipses();
		});
	}

	private void Capture(Rig rig)
	{
		if (!rig.Asset)
		{
			return;
		}
		SetLayer(rig, CaptureLayer);
		m_CaptureCamera.targetTexture = rig.Texture;
		m_CaptureCamera.Render();
		m_CaptureCamera.targetTexture = null;
		SetLayer(rig, HiddenLayer);
	}

	// Ellipse (center xy, radii zw, in uv) inscribed in the body's bounding box. Captured alpha is coverage squared
	// (see the shader); the threshold (coverage 0.5) leaves out the ground shadow, which is about 0.33.
	private static Vector4 SilhouetteEllipse(NativeArray<Color32> pixels, int width, int height)
	{
		int minX = width, minY = height, maxX = -1, maxY = -1;
		// Every other pixel each way is plenty for a bounding box and a quarter of the work.
		for (var y = 0; y < height; y += 2)
		{
			for (var x = 0; x < width; x += 2)
			{
				if (pixels[y * width + x].a > 64)
				{
					minX = Mathf.Min(minX, x);
					maxX = Mathf.Max(maxX, x);
					minY = Mathf.Min(minY, y);
					maxY = Mathf.Max(maxY, y);
				}
			}
		}
		if (maxX < 0)
		{
			return new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
		}
		var w = (float)width;
		var h = (float)height;
		return new Vector4((minX + maxX + 1) * 0.5f / w, (minY + maxY + 1) * 0.5f / h, (maxX - minX + 1) * 0.5f / w, (maxY - minY + 1) * 0.5f / h);
	}

	// Ends the effect: the new rig shows directly again and the old one goes back to the asset pool.
	public void Finish()
	{
		RestoreRig(m_New);
		RestoreRig(m_Old);
		if (m_Old.Asset)
		{
			DIContainerInfrastructure.GetCharacterAssetProvider(false).DestroyObject(m_OldAssetId, m_Old.Asset.gameObject);
		}
		Destroy(base.gameObject);
	}

	private static void RestoreRig(Rig rig)
	{
		if (!rig.Asset)
		{
			return;
		}
		for (var i = 0; i < rig.Renderers.Length; i++)
		{
			if (rig.Renderers[i])
			{
				rig.Renderers[i].gameObject.layer = rig.Layers[i];
			}
			var skinned = rig.Renderers[i] as SkinnedMeshRenderer;
			if (skinned != null)
			{
				skinned.updateWhenOffscreen = rig.UpdateWhenOffscreen[i];
			}
		}
		for (var i = 0; i < rig.Animations.Length; i++)
		{
			if (rig.Animations[i])
			{
				rig.Animations[i].cullingType = rig.Culling[i];
			}
		}
	}

	private void OnDestroy()
	{
		foreach (var rig in new[] { m_Old, m_New })
		{
			if (rig != null && rig.Texture != null)
			{
				rig.Texture.Release();
				Destroy(rig.Texture);
			}
		}
		if (m_Material != null)
		{
			Destroy(m_Material);
		}
		if (m_Quad != null)
		{
			Destroy(m_Quad.GetComponent<MeshFilter>().sharedMesh);
		}
	}
}
