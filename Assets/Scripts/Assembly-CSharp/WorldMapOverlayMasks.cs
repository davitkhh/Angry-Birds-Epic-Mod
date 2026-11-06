using System.Collections.Generic;
using System.Linq;
using SmoothMoves;
using UnityEngine;
using UnityEngine.Rendering;

// World map overlays that should show over the zone clouding and foreground props but under characters and nodes (the
// next-level marker's stars). Depth can't do both, because the clouding and props are in front of characters. So a
// command buffer on the world camera, run after the opaque pass (the foreground props write depth) and before the
// transparent pass, draws every character (every SmoothMoves rig and what it carries) and every hotspot's contents once
// more with "WorldMap/Stencil Mark", which only marks their visible pixels in the stencil buffer. Overlays ("WorldMap/
// Masked Overlay Colored") draw last, over everything, except on marked pixels. Nothing's own materials are touched:
// replacing a rig's materials reorders its parts.
public class WorldMapOverlayMasks : MonoBehaviour
{
	public const int OverlayQueue = 3100;

	private static Shader s_MarkShader;

	private static Shader s_OverlayShader;

	private readonly Dictionary<Material, Material> m_MarkMaterials = new Dictionary<Material, Material>();

	private readonly List<Renderer> m_Markers = new List<Renderer>();

	private CommandBuffer m_Buffer;

	private Camera m_Camera;

	private float m_NextScan;

	private void Awake()
	{
		LoadShaders();
	}

	private static void LoadShaders()
	{
		if (s_MarkShader == null)
		{
			s_MarkShader = Shader.Find("WorldMap/Stencil Mark");
		}
		if (s_OverlayShader == null)
		{
			s_OverlayShader = Shader.Find("WorldMap/Masked Overlay Colored");
		}
	}

	private void OnEnable()
	{
		Camera.onPreCull += PreCull;
	}

	private void OnDisable()
	{
		Camera.onPreCull -= PreCull;
		Detach();
	}

	private void OnDestroy()
	{
		Detach();
		foreach (var material in m_MarkMaterials.Values)
		{
			Destroy(material);
		}
	}

	private void Detach()
	{
		if (m_Camera != null && m_Buffer != null)
		{
			m_Camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, m_Buffer);
		}
		m_Camera = null;
	}

	private void Update()
	{
		if (Time.unscaledTime >= m_NextScan)
		{
			m_NextScan = Time.unscaledTime + 1f;
			Scan();
		}
	}

	// Characters and hotspots come and go (cutscenes, walking birds); look for them once a second.
	private void Scan()
	{
		var markers = new HashSet<Renderer>();
		foreach (var rig in FindObjectsOfType<BoneAnimation>())
		{
			Collect(rig.transform, markers);
		}
		foreach (var hotspot in FindObjectsOfType<HotSpotWorldMapViewBase>())
		{
			Collect(hotspot.transform, markers);
		}
		m_Markers.Clear();
		m_Markers.AddRange(markers);
	}

	private static void Collect(Transform root, HashSet<Renderer> into)
	{
		foreach (var renderer in root.GetComponentsInChildren<Renderer>())
		{
			if (!(renderer is ParticleSystemRenderer) && !(renderer is TrailRenderer) && !(renderer is LineRenderer) && !IsEffect(renderer.transform, root))
			{
				into.Add(renderer);
			}
		}
	}

	// Effects (the marker's own light beams, node flashes, smoke) are not characters or nodes: they never hide the stars.
	private static bool IsEffect(Transform transform, Transform root)
	{
		for (var t = transform; t != null && t != root.parent; t = t.parent)
		{
			if (t.name.StartsWith("FX_"))
			{
				return true;
			}
		}
		return false;
	}

	private void PreCull(Camera camera)
	{
		var state = DIContainerInfrastructure.LocationStateMgr;
		if (state == null || camera != state.SceneryCamera || s_MarkShader == null)
		{
			return;
		}
		if (m_Camera != camera)
		{
			Detach();
			if (m_Buffer == null)
			{
				m_Buffer = new CommandBuffer { name = "World map overlay masks" };
			}
			camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, m_Buffer);
			m_Camera = camera;
		}
		m_Buffer.Clear();
		foreach (var renderer in m_Markers)
		{
			if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
			{
				continue;
			}
			var subMeshes = SubMeshCount(renderer);
			if (subMeshes <= 0)
			{
				continue;
			}
			var materials = renderer.sharedMaterials;
			for (var i = 0; i < materials.Length; i++)
			{
				var mark = MarkFor(materials[i]);
				if (mark != null)
				{
					m_Buffer.DrawRenderer(renderer, mark, Mathf.Min(i, subMeshes - 1));
				}
			}
		}
	}

	private static int SubMeshCount(Renderer renderer)
	{
		var skinned = renderer as SkinnedMeshRenderer;
		if (skinned != null)
		{
			return skinned.sharedMesh != null ? skinned.sharedMesh.subMeshCount : 0;
		}
		var filter = renderer.GetComponent<MeshFilter>();
		return filter != null && filter.sharedMesh != null ? filter.sharedMesh.subMeshCount : 0;
	}

	// One mark material per original material, sampling its texture for the alpha.
	private Material MarkFor(Material material)
	{
		if (material == null)
		{
			return null;
		}
		Material mark;
		if (!m_MarkMaterials.TryGetValue(material, out mark))
		{
			mark = null;
			if (material.HasProperty("_MainTex") && material.mainTexture != null)
			{
				mark = new Material(s_MarkShader) { name = material.name + " (stencil mark)" };
				mark.mainTexture = material.mainTexture;
				mark.mainTextureScale = material.mainTextureScale;
				mark.mainTextureOffset = material.mainTextureOffset;
			}
			m_MarkMaterials[material] = mark;
		}
		return mark;
	}

	// Draws an effect's particles last, over the clouding and props but not over characters or nodes.
	public static void MakeOverlay(GameObject effect)
	{
		LoadShaders();
		if (s_OverlayShader == null)
		{
			DebugLog.Warn(typeof(WorldMapOverlayMasks), "shader WorldMap/Masked Overlay Colored not found");
			return;
		}
		foreach (var renderer in effect.GetComponentsInChildren<ParticleSystemRenderer>(true))
		{
			renderer.sharedMaterials = renderer.sharedMaterials.Select(m =>
			{
				if (m == null)
				{
					return null;
				}
				var copy = new Material(m) { name = m.name + " (overlay)" };
				copy.shader = s_OverlayShader;
				copy.renderQueue = OverlayQueue;
				return copy;
			}).ToArray();
		}
	}
}
