using System.Collections.Generic;
using UnityEngine;

// King DJ's defeat: his headgear falls off like a minion pig's or brute's helmet (a heavy object: a short pop, strong
// gravity, a low bounce, then it slides and tips over to rest) instead of floating away like PigMage's fabric hat. The
// crown and the headphones fall as two separate objects. The Headgear / Headgear_Back bones are hidden for the rest of the
// defeat, and the objects are built from copies of their textures split by the Combatant Forge's layers
// (Resources/Combatants/KingDJ/Defeat: the crown and the headband from Headgear_Back, the front earcup from Headgear), placed exactly
// where the bones drew them. They fade out when the defeat animation ends.
public class KingDJHeadgearDrop : MonoBehaviour
{
	// Forge-baked sprite placement: a bone draws its texture centred at -pivot * size (bone-local rig pixels).
	private static readonly Vector2 FrontPivot = new Vector2(-0.3382839f, 0.413316897f);

	private static readonly Vector2 BackPivot = new Vector2(0.0783096728f, -0.326302914f);

	private const string FrontBone = "Headgear";

	private const string BackBone = "Headgear_Back";

	// In rig pixels per second (squared): heavy, quick fall.
	private const float GravityPx = 3200f;

	private const float Restitution = 0.28f;

	private const float GroundFriction = 0.55f;

	private const float FadeTime = 0.35f;

	private static Shader s_Shader;

	private CharacterAssetController m_Asset;

	private readonly List<Piece> m_Pieces = new List<Piece>();

	private float m_PixelSize;

	private float m_Ground;

	private float m_FadeAt;

	private float m_Elapsed;

	private class Piece
	{
		public GameObject Root;

		public List<Material> Materials = new List<Material>();

		public Vector2 Velocity;

		public float Spin;

		// Distance from the piece's centre to its lowest point when lying down.
		public float Rest;

		public bool Settled;
	}

	public static void Begin(CharacterAssetController asset)
	{
		if (asset == null || asset.HasNoBoneAnimation || asset.m_BoneAnimation == null || asset.GetComponent<KingDJHeadgearDrop>() != null)
		{
			return;
		}
		var front = asset.m_BoneAnimation.GetBoneTransform(FrontBone);
		var back = asset.m_BoneAnimation.GetBoneTransform(BackBone);
		var crown = Resources.Load<Texture2D>("Combatants/KingDJ/Defeat/KingDJ_Crown");
		var band = Resources.Load<Texture2D>("Combatants/KingDJ/Defeat/KingDJ_Headphones_Back");
		var earcup = Resources.Load<Texture2D>("Combatants/KingDJ/Defeat/KingDJ_Headphones_Front");
		if (front == null || back == null || crown == null || band == null || earcup == null)
		{
			return;
		}
		asset.gameObject.AddComponent<KingDJHeadgearDrop>().Init(asset, front, back, crown, band, earcup);
	}

	private void Init(CharacterAssetController asset, Transform front, Transform back, Texture2D crown, Texture2D band, Texture2D earcup)
	{
		m_Asset = asset;
		if (s_Shader == null)
		{
			s_Shader = Shader.Find("Sprites/Default");
		}
		var root = FindBone(asset, "Root") ?? asset.transform;
		m_PixelSize = Mathf.Abs(root.lossyScale.y);
		m_Ground = (asset.BodyRoot != null ? asset.BodyRoot : asset.transform).position.y;
		m_FadeAt = Mathf.Max(0.8f, asset.GetDefeatAnimationLength() - FadeTime);
		var renderer = asset.GetComponentInChildren<Renderer>();
		var renderQueue = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.renderQueue : 3000;
		var depth = (asset.BodyCenter != null ? asset.BodyCenter : asset.transform).position.z - 2f;
		// Away from the birds is +x for a pig; the crown pops up and back, the headphones drop forward.
		var away = asset.transform.lossyScale.x < 0f ? -1f : 1f;
		var crownPiece = new Piece { Velocity = new Vector2(away * 140f, 520f) * m_PixelSize, Spin = -away * 260f };
		AddQuad(crownPiece, crown, back, BackPivot, depth, renderQueue, asset.gameObject.layer);
		m_Pieces.Add(Finish(crownPiece, "KingDJ_Crown"));
		var phones = new Piece { Velocity = new Vector2(-away * 110f, 360f) * m_PixelSize, Spin = away * 340f };
		AddQuad(phones, band, back, BackPivot, depth - 0.2f, renderQueue, asset.gameObject.layer);
		AddQuad(phones, earcup, front, FrontPivot, depth - 0.4f, renderQueue, asset.gameObject.layer);
		m_Pieces.Add(Finish(phones, "KingDJ_Headphones"));
		HideHeadgear();
	}

	// A quad with the texture exactly where bone draws it, in world space (children of the piece's root are set up
	// once the piece's centre is known).
	private void AddQuad(Piece piece, Texture2D texture, Transform bone, Vector2 pivot, float depth, int renderQueue, int layer)
	{
		if (piece.Root == null)
		{
			piece.Root = new GameObject("KingDJHeadgear");
			piece.Root.layer = layer;
		}
		var size = new Vector2(texture.width, texture.height);
		var center = -Vector2.Scale(pivot, size);
		var corners = new Vector3[4];
		var index = 0;
		foreach (var v in new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f) })
		{
			var world = bone.TransformPoint(new Vector3(center.x + v.x * size.x, center.y + v.y * size.y, 0f));
			corners[index++] = new Vector3(world.x, world.y, depth);
		}
		var child = new GameObject(texture.name);
		child.layer = layer;
		var mesh = new Mesh { name = texture.name };
		mesh.vertices = corners;
		mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
		mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
		mesh.RecalculateBounds();
		child.AddComponent<MeshFilter>().sharedMesh = mesh;
		var material = new Material(s_Shader) { mainTexture = texture, renderQueue = renderQueue };
		var meshRenderer = child.AddComponent<MeshRenderer>();
		meshRenderer.sharedMaterial = material;
		meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		meshRenderer.receiveShadows = false;
		child.transform.SetParent(piece.Root.transform, true);
		piece.Materials.Add(material);
	}

	// Moves the piece's root to the centre of its quads (they keep their world placement) and sizes its rest height.
	private Piece Finish(Piece piece, string name)
	{
		piece.Root.name = name;
		var bounds = new Bounds();
		var first = true;
		foreach (var filter in piece.Root.GetComponentsInChildren<MeshFilter>())
		{
			foreach (var vertex in filter.sharedMesh.vertices)
			{
				if (first)
				{
					bounds = new Bounds(vertex, Vector3.zero);
					first = false;
				}
				else
				{
					bounds.Encapsulate(vertex);
				}
			}
		}
		var center = new Vector3(bounds.center.x, bounds.center.y, 0f);
		foreach (var filter in piece.Root.GetComponentsInChildren<MeshFilter>())
		{
			var vertices = filter.sharedMesh.vertices;
			for (var i = 0; i < vertices.Length; i++)
			{
				vertices[i] -= center;
			}
			filter.sharedMesh.vertices = vertices;
			filter.sharedMesh.RecalculateBounds();
		}
		piece.Root.transform.position = center;
		// Lying on its side: rests on the shorter half-extent, a little sunk in like a heavy object.
		piece.Rest = Mathf.Min(bounds.extents.x, bounds.extents.y) * 0.75f;
		return piece;
	}

	private void HideHeadgear()
	{
		if (m_Asset != null && m_Asset.m_BoneAnimation != null)
		{
			m_Asset.m_BoneAnimation.HideBone(FrontBone, true);
			m_Asset.m_BoneAnimation.HideBone(BackBone, true);
		}
	}

	private void LateUpdate()
	{
		var dt = Time.deltaTime;
		m_Elapsed += dt;
		// The defeat clip may show the bones again through its own keys.
		HideHeadgear();
		var gravity = GravityPx * m_PixelSize;
		foreach (var piece in m_Pieces)
		{
			if (piece.Root == null)
			{
				continue;
			}
			var transform = piece.Root.transform;
			if (!piece.Settled)
			{
				piece.Velocity.y -= gravity * dt;
				var position = transform.position + (Vector3)(piece.Velocity * dt);
				var rotation = transform.eulerAngles.z + piece.Spin * dt;
				var floor = m_Ground + piece.Rest;
				if (position.y <= floor)
				{
					position.y = floor;
					if (Mathf.Abs(piece.Velocity.y) < 120f * m_PixelSize)
					{
						piece.Settled = true;
						piece.Velocity = Vector2.zero;
					}
					else
					{
						piece.Velocity = new Vector2(piece.Velocity.x * GroundFriction, -piece.Velocity.y * Restitution);
						piece.Spin *= 0.4f;
					}
				}
				transform.position = position;
				transform.rotation = Quaternion.Euler(0f, 0f, rotation);
			}
			else
			{
				// Tips over onto its nearest side.
				var angle = transform.eulerAngles.z;
				var target = Mathf.Round(angle / 90f) * 90f;
				transform.rotation = Quaternion.Euler(0f, 0f, Mathf.MoveTowardsAngle(angle, target, 400f * dt));
			}
			var alpha = 1f - Mathf.Clamp01((m_Elapsed - m_FadeAt) / FadeTime);
			foreach (var material in piece.Materials)
			{
				material.color = new Color(1f, 1f, 1f, alpha);
			}
		}
		if (m_Elapsed > m_FadeAt + FadeTime)
		{
			Destroy(this);
		}
	}

	private void OnDestroy()
	{
		foreach (var piece in m_Pieces)
		{
			foreach (var material in piece.Materials)
			{
				Destroy(material);
			}
			if (piece.Root != null)
			{
				foreach (var filter in piece.Root.GetComponentsInChildren<MeshFilter>())
				{
					Destroy(filter.sharedMesh);
				}
				Destroy(piece.Root);
			}
		}
		m_Pieces.Clear();
	}

	private static Transform FindBone(CharacterAssetController asset, string name)
	{
		foreach (var transform in asset.GetComponentsInChildren<Transform>(true))
		{
			if (transform.name == name)
			{
				return transform;
			}
		}
		return null;
	}
}
