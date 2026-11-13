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

	// Colour the copies are drawn in (white: as they are)
	public Color Tint = Color.white;

	// Each copy starts up to this far (world units) from where the rig was, moves at Drift (world units per second)
	// and scales from 1 to Grow over its fade, around the rig's position (Combatant Forge after-images)
	public float Spread;

	public Vector2 Drift;

	public float Grow = 1f;

	// Copies sit this far behind the rig (larger z draws further back with the orthographic sort).
	public float DepthBehind = 1f;

	private class Ghost
	{
		public GameObject Root;

		public List<Material> Materials;

		public List<Mesh> Meshes;

		public float Age;

		public Vector3 Start;
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
			if (ghost.Root != null && (Drift != Vector2.zero || Grow != 1f))
			{
				ghost.Root.transform.position = ghost.Start + (Vector3)(Drift * ghost.Age);
				ghost.Root.transform.localScale = Vector3.one * Mathf.Lerp(1f, Grow, Mathf.Clamp01(ghost.Age / FadeTime));
			}
			var alpha = StartAlpha * (1f - Mathf.Clamp01(ghost.Age / FadeTime));
			if (alpha <= 0f || ghost.Root == null)
			{
				DestroyGhost(ghost);
				m_Ghosts.RemoveAt(i);
				continue;
			}
			foreach (var material in ghost.Materials)
			{
				material.color = new Color(Tint.r, Tint.g, Tint.b, alpha);
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
		// the copy's own origin at the rig (scaling happens around it), shifted by the spread
		var offset = Spread > 0f ? (Vector3)(UnityEngine.Random.insideUnitCircle * Spread) : Vector3.zero;
		ghost.Root.transform.position = new Vector3(m_Rig.position.x, m_Rig.position.y, ghost.Root.transform.position.z) + offset;
		ghost.Start = ghost.Root.transform.position;
		foreach (var renderer in m_Rig.GetComponentsInChildren<Renderer>())
		{
			if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.name.IndexOf("Shadow", System.StringComparison.OrdinalIgnoreCase) >= 0)
			{
				continue;  // (shadows aren't copied)
			}
			Mesh mesh;
			var skinned = renderer as SkinnedMeshRenderer;
			if (skinned != null)
			{
				// Baked without the renderer's scale; the copy's transform puts it back (pigs face left with -1 x).
				mesh = new Mesh();
				skinned.BakeMesh(mesh);
				DropShadow(skinned, mesh);
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
				material.color = new Color(Tint.r, Tint.g, Tint.b, StartAlpha);
				materials[i] = material;
				ghost.Materials.Add(material);
			}
			copyRenderer.sharedMaterials = materials;
			ghost.Meshes.Add(mesh);
		}
		m_Ghosts.Add(ghost);
	}

	// The rig's shadow is part of its one skinned mesh: take out the triangles whose vertices follow a shadow bone.
	private static void DropShadow(SkinnedMeshRenderer skinned, Mesh mesh)
	{
		var bones = skinned.bones;
		var source = skinned.sharedMesh;
		if (bones == null || source == null)
		{
			return;
		}
		var shadow = new bool[bones.Length];
		var any = false;
		for (var i = 0; i < bones.Length; i++)
		{
			shadow[i] = bones[i] != null && bones[i].name.IndexOf("Shadow", System.StringComparison.OrdinalIgnoreCase) >= 0;
			any |= shadow[i];
		}
		var weights = source.boneWeights;
		if (!any || weights == null || weights.Length != mesh.vertexCount)
		{
			return;
		}
		for (var sub = 0; sub < mesh.subMeshCount; sub++)
		{
			var tris = mesh.GetTriangles(sub);
			var kept = new List<int>(tris.Length);
			for (var t = 0; t + 2 < tris.Length; t += 3)
			{
				var w = weights[tris[t]];
				var bone = w.boneIndex0;
				if (bone >= 0 && bone < shadow.Length && shadow[bone] && w.weight0 > 0.5f)
				{
					continue;
				}
				kept.Add(tris[t]);
				kept.Add(tris[t + 1]);
				kept.Add(tris[t + 2]);
			}
			mesh.SetTriangles(kept, sub);
		}
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
