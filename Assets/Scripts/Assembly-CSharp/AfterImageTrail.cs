using System.Collections.Generic;
using UnityEngine;

// Leaves still copies of a rig behind it while enabled: every Interval (real time, so slow motion doesn't thin the
// trail out) the rig's current pose is baked into a mesh that starts at StartAlpha and fades out over FadeTime. The
// rig atlases use Unlit/Transparent, which has no tint, so the copies draw with Sprites/Default and fade its color.
public class AfterImageTrail : MonoBehaviour
{
	public float Interval = 0.3f;

	public float FadeTime = 0.6f;

	public float StartAlpha = 1f;

	// Copies sit this far behind the rig (larger z draws further back with the orthographic sort).
	public float DepthBehind = 1f;

	private class Ghost
	{
		public GameObject Root;

		public List<Material> Materials;

		public List<Mesh> Meshes;

		public float Age;
	}

	private readonly List<Ghost> m_Ghosts = new List<Ghost>();

	private Transform m_Rig;

	private Transform m_Parent;

	private float m_NextSpawn;

	private bool m_Spawning;

	private static Shader s_Shader;

	public static AfterImageTrail Begin(Transform rig, Transform parent)
	{
		var trail = rig.gameObject.GetComponent<AfterImageTrail>();
		if (trail == null)
		{
			trail = rig.gameObject.AddComponent<AfterImageTrail>();
		}
		trail.m_Rig = rig;
		trail.m_Parent = parent;
		trail.m_Spawning = true;
		trail.m_NextSpawn = 0f;
		return trail;
	}

	// Stops leaving copies; the ones already left keep fading and the component removes itself after the last.
	public void End()
	{
		m_Spawning = false;
	}

	private void Update()
	{
		var dt = Time.unscaledDeltaTime;
		if (m_Spawning)
		{
			m_NextSpawn -= dt;
			if (m_NextSpawn <= 0f)
			{
				Spawn();
				m_NextSpawn += Interval;
			}
		}
		for (var i = m_Ghosts.Count - 1; i >= 0; i--)
		{
			var ghost = m_Ghosts[i];
			ghost.Age += dt;
			var alpha = StartAlpha * (1f - Mathf.Clamp01(ghost.Age / FadeTime));
			if (alpha <= 0f || ghost.Root == null)
			{
				DestroyGhost(ghost);
				m_Ghosts.RemoveAt(i);
				continue;
			}
			foreach (var material in ghost.Materials)
			{
				material.color = new Color(1f, 1f, 1f, alpha);
			}
		}
		if (!m_Spawning && m_Ghosts.Count == 0)
		{
			Destroy(this);
		}
	}

	private void Spawn()
	{
		if (s_Shader == null)
		{
			s_Shader = Shader.Find("Sprites/Default");
		}
		var ghost = new Ghost { Root = new GameObject("AfterImage"), Materials = new List<Material>(), Meshes = new List<Mesh>() };
		ghost.Root.layer = m_Rig.gameObject.layer;
		ghost.Root.transform.SetParent(m_Parent, false);
		foreach (var renderer in m_Rig.GetComponentsInChildren<Renderer>())
		{
			if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
			{
				continue;
			}
			Mesh mesh;
			var skinned = renderer as SkinnedMeshRenderer;
			if (skinned != null)
			{
				// Baked without the renderer's scale; the copy's transform puts it back (pigs face left with -1 x).
				mesh = new Mesh();
				skinned.BakeMesh(mesh);
			}
			else
			{
				var filter = renderer.GetComponent<MeshFilter>();
				if (!(renderer is MeshRenderer) || filter == null || filter.sharedMesh == null)
				{
					continue;
				}
				mesh = Instantiate(filter.sharedMesh);
			}
			var copy = new GameObject(renderer.name);
			copy.layer = renderer.gameObject.layer;
			copy.transform.position = renderer.transform.position + new Vector3(0f, 0f, DepthBehind);
			copy.transform.rotation = renderer.transform.rotation;
			copy.transform.localScale = renderer.transform.lossyScale;
			copy.transform.SetParent(ghost.Root.transform, true);
			copy.AddComponent<MeshFilter>().sharedMesh = mesh;
			var copyRenderer = copy.AddComponent<MeshRenderer>();
			var materials = new Material[renderer.sharedMaterials.Length];
			for (var i = 0; i < materials.Length; i++)
			{
				var source = renderer.sharedMaterials[i];
				var material = new Material(s_Shader);
				if (source != null && source.HasProperty("_MainTex"))
				{
					material.mainTexture = source.mainTexture;
				}
				if (source != null)
				{
					material.renderQueue = source.renderQueue;
				}
				material.color = new Color(1f, 1f, 1f, StartAlpha);
				materials[i] = material;
				ghost.Materials.Add(material);
			}
			copyRenderer.sharedMaterials = materials;
			ghost.Meshes.Add(mesh);
		}
		m_Ghosts.Add(ghost);
	}

	private static void DestroyGhost(Ghost ghost)
	{
		foreach (var material in ghost.Materials)
		{
			Destroy(material);
		}
		foreach (var mesh in ghost.Meshes)
		{
			Destroy(mesh);
		}
		if (ghost.Root != null)
		{
			Destroy(ghost.Root);
		}
	}

	private void OnDestroy()
	{
		foreach (var ghost in m_Ghosts)
		{
			DestroyGhost(ghost);
		}
		m_Ghosts.Clear();
	}
}
