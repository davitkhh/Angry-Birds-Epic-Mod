using System;
using System.Collections.Generic;
using UnityEngine;

// Eye beams: a helmet whose eyes briefly shine bright light (a directional beam per eye that still spreads a lot, plus a
// flare on the eye) when its wearer uses certain skills. The basic Stoneguard helmet does it on support skills.
//
// Assets/Resources/HelmetBeams.json lists them by the texture a bone wears (CharacterAssetController.ShownTexture):
// the game's own helmets by their art (Headgear_StoneGuard) and Combatant Forge ones by their baked texture (Forge_<id>,
// written by Tools/CombatantForge/beams.py from a "beams" layer in a slot). JsonUtility reads flat arrays only, so points
// are x,y pairs in one list.
//
// Coordinates: "eyes" and "angle" are in the art's own space (rig pixels from its centre, y up, angle 0 = +x).
// "affine" (a,b,d,e,cx,cy) places that art in the slot's sprite space: x' = a x + b y + cx, y' = d x + e y + cy. A plain
// game texture sits at its -pivot * size; a Forge layer carries its own move, turn, scale and flip, and the beams turn
// and flip with it. Lengths and widths are rig pixels in the sprite's space.
//
// The beams are drawn in front of the whole character (like KingDJHeadgearDrop's pieces). A Forge beams layer that sits
// under other layers of its slot gets "cover": those layers baked into one texture, drawn over the beams while they shine.
public class HelmetBeams : MonoBehaviour
{
	public const string Support = "Support";

	public const string Attack = "Attack";

	public const string Rage = "Rage";

	// An "on" token for one skill: "skill:<SkillNameId>".
	public const string SkillPrefix = "skill:";

	[Serializable]
	public class Entry
	{
		public string texture;

		public string slot = "Headgear";

		public string[] on;

		public float[] eyes;

		public float[] affine;

		public float angle;

		public string color = "FFD940";

		public float length = 430f;

		public float spread = 36f;

		public float duration = 0.75f;

		public float delay = 0.1f;

		public float intensity = 1f;

		public string cover;

		public float[] coverPivot;
	}

	[Serializable]
	private class Table
	{
		public Entry[] beams;
	}

	private const string ConfigName = "HelmetBeams";

	// Seconds to full brightness, and the share of the duration spent fading out.
	private const float RiseTime = 0.07f;

	private const float FadeShare = 0.55f;

	private static List<Entry> s_Entries;

	private static Shader s_BeamShader;

	private static Shader s_CoverShader;

	private CharacterAssetController m_Asset;

	private readonly List<Flash> m_Flashes = new List<Flash>();

	private class Flash
	{
		public Entry Entry;

		public float Start;

		public float Seed;

		public GameObject Root;

		public Mesh Mesh;

		public GameObject Cover;

		public Mesh CoverMesh;

		public Texture2D CoverTexture;

		public bool OwnsCover;
	}

	private static List<Entry> Entries()
	{
#if UNITY_EDITOR
		// In the editor, read the file itself: a Forge upload applies without waiting for Unity to reimport it.
		s_Entries = null;
#endif
		if (s_Entries != null)
		{
			return s_Entries;
		}
		s_Entries = new List<Entry>();
		string text = null;
#if UNITY_EDITOR
		var path = System.IO.Path.Combine(Application.dataPath, "Resources/" + ConfigName + ".json");
		if (System.IO.File.Exists(path))
		{
			text = System.IO.File.ReadAllText(path);
		}
#else
		var asset = Resources.Load<TextAsset>(ConfigName);
		text = asset != null ? asset.text : null;
#endif
		Table table = null;
		try
		{
			table = string.IsNullOrEmpty(text) ? null : JsonUtility.FromJson<Table>(text);
		}
		catch (Exception e)
		{
			DebugLog.Error("[HelmetBeams] " + ConfigName + ".json: " + e.Message);
		}
		if (table != null && table.beams != null)
		{
			foreach (var entry in table.beams)
			{
				if (entry != null && !string.IsNullOrEmpty(entry.texture) && entry.eyes != null && entry.eyes.Length >= 2)
				{
					s_Entries.Add(entry);
				}
			}
		}
		return s_Entries;
	}

	// Called as a skill's clip starts (CharacterControllerBattleGroundBase.PlaySupport/Attack/RageSkillAnimation).
	public static void OnSkillClip(CharacterControllerBattleGroundBase character, string kind)
	{
		var asset = character != null ? character.m_AssetController : null;
		if (asset == null || asset.m_BoneAnimation == null || !asset.gameObject.activeInHierarchy)
		{
			return;
		}
		var skill = character.m_ClipSkill;
		var skillId = skill != null && skill.Model != null ? skill.Model.SkillNameId : null;
		foreach (var entry in Entries())
		{
			if (asset.ShownTexture(entry.slot) != entry.texture || !TurnsOn(entry, kind, skillId))
			{
				continue;
			}
			var beams = asset.GetComponent<HelmetBeams>();
			if (beams == null)
			{
				beams = asset.gameObject.AddComponent<HelmetBeams>();
			}
			beams.m_Asset = asset;
			beams.Play(entry);
		}
	}

	private static bool TurnsOn(Entry entry, string kind, string skillId)
	{
		if (entry.on == null)
		{
			return false;
		}
		foreach (var token in entry.on)
		{
			if (token == kind || (!string.IsNullOrEmpty(skillId) && token == SkillPrefix + skillId))
			{
				return true;
			}
		}
		return false;
	}

	private void Play(Entry entry)
	{
		var flash = m_Flashes.Find(f => f.Entry == entry);
		if (flash == null)
		{
			flash = new Flash { Entry = entry };
			m_Flashes.Add(flash);
		}
		flash.Start = Time.time + Mathf.Max(0f, entry.delay);
		flash.Seed = UnityEngine.Random.value * 100f;
	}

	private Transform SpriteNode(string slot)
	{
		var bone = m_Asset.m_BoneAnimation.GetBoneTransform(slot);
		if (bone == null)
		{
			return null;
		}
		var sprite = bone.Find(slot + "_Sprite");
		return sprite != null ? sprite : bone;
	}

	private void LateUpdate()
	{
		if (m_Asset == null)
		{
			Destroy(this);
			return;
		}
		for (var i = m_Flashes.Count - 1; i >= 0; i--)
		{
			var flash = m_Flashes[i];
			var t = Time.time - flash.Start;
			var duration = Mathf.Max(0.05f, flash.Entry.duration);
			if (t > duration)
			{
				Clear(flash);
				m_Flashes.RemoveAt(i);
				continue;
			}
			var sprite = SpriteNode(flash.Entry.slot);
			if (t < 0f || sprite == null)
			{
				Show(flash, false);
				continue;
			}
			Draw(flash, sprite, Brightness(t, duration, flash.Seed) * Mathf.Max(0f, flash.Entry.intensity));
		}
	}

	// Quick rise, a short hold with a little flicker, a long soft fade.
	private static float Brightness(float t, float duration, float seed)
	{
		var rise = Mathf.Clamp01(t / RiseTime);
		var fadeFrom = duration * (1f - FadeShare);
		var fade = t <= fadeFrom ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - fadeFrom) / (duration - fadeFrom));
		var flicker = 0.88f + 0.12f * Mathf.PerlinNoise(seed, t * 18f);
		return rise * fade * flicker;
	}

	private float Depth()
	{
		return (m_Asset.BodyCenter != null ? m_Asset.BodyCenter : m_Asset.transform).position.z - 2f;
	}

	private int RenderQueue()
	{
		var renderer = m_Asset.GetComponentInChildren<Renderer>();
		return renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.renderQueue : 3000;
	}

	private void Draw(Flash flash, Transform sprite, float brightness)
	{
		var entry = flash.Entry;
		if (flash.Root == null)
		{
			Build(flash);
		}
		Show(flash, true);
		var depth = Depth();
		var color = Hex(entry.color, new Color(1f, 0.85f, 0.25f));
		var core = Color.Lerp(color, Color.white, 0.65f);
		var vertices = new List<Vector3>();
		var colors = new List<Color>();
		var triangles = new List<int>();
		Func<Vector2, Vector3> world = p =>
		{
			var w = sprite.TransformPoint(new Vector3(p.x, p.y, 0f));
			return new Vector3(w.x, w.y, depth);
		};
		var a = Affine(entry);
		var radians = entry.angle * Mathf.Deg2Rad;
		var direction = Linear(a, new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)));
		direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
		var across = new Vector2(-direction.y, direction.x);
		var length = Mathf.Max(10f, entry.length);
		var spread = Mathf.Clamp(entry.spread, 1f, 170f);
		for (var i = 0; i + 1 < entry.eyes.Length; i += 2)
		{
			var eye = Point(a, new Vector2(entry.eyes[i], entry.eyes[i + 1]));
			// a wide soft beam, then a narrower bright core inside it
			Cone(vertices, colors, triangles, world, eye, direction, across, length, spread * 0.5f, 6f, color, 0.6f * brightness);
			Cone(vertices, colors, triangles, world, eye, direction, across, length * 0.75f, spread * 0.17f, 3.5f, core, 0.85f * brightness);
			// the eye itself: a soft halo and a white-hot centre
			Disc(vertices, colors, triangles, world, eye, 30f, color, 0.45f * brightness);
			Disc(vertices, colors, triangles, world, eye, 11f, Color.Lerp(core, Color.white, 0.5f), brightness);
		}
		var mesh = flash.Mesh;
		mesh.Clear();
		mesh.SetVertices(vertices);
		mesh.SetColors(colors);
		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateBounds();
		if (flash.Cover != null)
		{
			PlaceCover(flash, world, depth - 0.1f);
		}
	}

	// A cone from the eye: rows along the beam, five columns across (dark edges, bright middle), fading with distance.
	private static void Cone(List<Vector3> vertices, List<Color> colors, List<int> triangles, Func<Vector2, Vector3> world, Vector2 eye, Vector2 direction, Vector2 across, float length, float halfAngle, float startHalfWidth, Color color, float alpha)
	{
		const int rows = 10;
		var columns = new[] { -1f, -0.45f, 0f, 0.45f, 1f };
		var profile = new[] { 0f, 0.55f, 1f, 0.55f, 0f };
		var slope = Mathf.Tan(halfAngle * Mathf.Deg2Rad);
		var first = vertices.Count;
		for (var r = 0; r <= rows; r++)
		{
			var s = (float)r / rows;
			var distance = length * s;
			var halfWidth = startHalfWidth + slope * distance;
			var along = Mathf.Pow(1f - s, 1.7f);
			var centre = eye + direction * distance;
			for (var c = 0; c < columns.Length; c++)
			{
				vertices.Add(world(centre + across * (halfWidth * columns[c])));
				colors.Add(new Color(color.r, color.g, color.b, alpha * along * profile[c]));
			}
		}
		for (var r = 0; r < rows; r++)
		{
			for (var c = 0; c + 1 < columns.Length; c++)
			{
				var v = first + r * columns.Length + c;
				var up = v + columns.Length;
				triangles.Add(v);
				triangles.Add(up);
				triangles.Add(v + 1);
				triangles.Add(v + 1);
				triangles.Add(up);
				triangles.Add(up + 1);
			}
		}
	}

	private static void Disc(List<Vector3> vertices, List<Color> colors, List<int> triangles, Func<Vector2, Vector3> world, Vector2 centre, float radius, Color color, float alpha)
	{
		const int segments = 18;
		var first = vertices.Count;
		vertices.Add(world(centre));
		colors.Add(new Color(color.r, color.g, color.b, alpha));
		for (var k = 0; k < segments; k++)
		{
			var angle = k * Mathf.PI * 2f / segments;
			vertices.Add(world(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius));
			colors.Add(new Color(color.r, color.g, color.b, 0f));
		}
		for (var k = 0; k < segments; k++)
		{
			triangles.Add(first);
			triangles.Add(first + 1 + (k + 1) % segments);
			triangles.Add(first + 1 + k);
		}
	}

	private void Build(Flash flash)
	{
		if (s_BeamShader == null)
		{
			s_BeamShader = FirstShader("Legacy Shaders/Particles/Additive", "Particles/Additive", "Sprites/Default");
		}
		var layer = m_Asset.gameObject.layer;
		var queue = RenderQueue();
		flash.Root = new GameObject("HelmetBeams");
		flash.Root.layer = layer;
		flash.Mesh = new Mesh { name = "HelmetBeams" };
		flash.Mesh.MarkDynamic();
		flash.Root.AddComponent<MeshFilter>().sharedMesh = flash.Mesh;
		var material = new Material(s_BeamShader) { mainTexture = Texture2D.whiteTexture, renderQueue = queue };
		if (material.HasProperty("_TintColor"))
		{
			material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
		}
		var renderer = flash.Root.AddComponent<MeshRenderer>();
		renderer.sharedMaterial = material;
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		bool owned;
		var cover = LoadCover(flash.Entry.cover, out owned);
		flash.OwnsCover = owned;
		if (cover != null)
		{
			if (s_CoverShader == null)
			{
				s_CoverShader = Shader.Find("Sprites/Default");
			}
			flash.CoverTexture = cover;
			flash.Cover = new GameObject("HelmetBeamsCover");
			flash.Cover.layer = layer;
			flash.CoverMesh = new Mesh { name = "HelmetBeamsCover" };
			flash.CoverMesh.MarkDynamic();
			flash.Cover.AddComponent<MeshFilter>().sharedMesh = flash.CoverMesh;
			var coverRenderer = flash.Cover.AddComponent<MeshRenderer>();
			coverRenderer.sharedMaterial = new Material(s_CoverShader) { mainTexture = cover, renderQueue = queue };
			coverRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			coverRenderer.receiveShadows = false;
		}
	}

	private static Shader FirstShader(params string[] names)
	{
		foreach (var name in names)
		{
			var shader = Shader.Find(name);
			if (shader != null)
			{
				return shader;
			}
		}
		return null;
	}

	private static Texture2D LoadCover(string path, out bool owned)
	{
		owned = false;
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}
#if UNITY_EDITOR
		var file = System.IO.Path.Combine(Application.dataPath, "Resources/" + path + ".png");
		if (System.IO.File.Exists(file))
		{
			var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = path, wrapMode = TextureWrapMode.Clamp };
			if (texture.LoadImage(System.IO.File.ReadAllBytes(file)))
			{
				owned = true;
				return texture;
			}
			Destroy(texture);
		}
#endif
		return Resources.Load<Texture2D>(path);
	}

	// The slot's layers above the beams, where the bone draws its texture: centred at -pivot * size in sprite space.
	private static void PlaceCover(Flash flash, Func<Vector2, Vector3> world, float depth)
	{
		var texture = flash.CoverTexture;
		var pivot = flash.Entry.coverPivot != null && flash.Entry.coverPivot.Length >= 2 ? new Vector2(flash.Entry.coverPivot[0], flash.Entry.coverPivot[1]) : Vector2.zero;
		var size = new Vector2(texture.width, texture.height);
		var centre = -Vector2.Scale(pivot, size);
		var corners = new Vector3[4];
		var index = 0;
		foreach (var v in new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f) })
		{
			var p = world(centre + Vector2.Scale(v, size));
			corners[index++] = new Vector3(p.x, p.y, depth);
		}
		var mesh = flash.CoverMesh;
		mesh.Clear();
		mesh.vertices = corners;
		mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
		mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
		mesh.RecalculateBounds();
	}

	private static float[] Affine(Entry entry)
	{
		return entry.affine != null && entry.affine.Length >= 6 ? entry.affine : new[] { 1f, 0f, 0f, 1f, 0f, 0f };
	}

	private static Vector2 Point(float[] a, Vector2 p)
	{
		return new Vector2(a[0] * p.x + a[1] * p.y + a[4], a[2] * p.x + a[3] * p.y + a[5]);
	}

	private static Vector2 Linear(float[] a, Vector2 v)
	{
		return new Vector2(a[0] * v.x + a[1] * v.y, a[2] * v.x + a[3] * v.y);
	}

	private static Color Hex(string hex, Color fallback)
	{
		Color c;
		return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out c) ? c : fallback;
	}

	private static void Show(Flash flash, bool shown)
	{
		if (flash.Root != null && flash.Root.activeSelf != shown)
		{
			flash.Root.SetActive(shown);
		}
		if (flash.Cover != null && flash.Cover.activeSelf != shown)
		{
			flash.Cover.SetActive(shown);
		}
	}

	private static void Clear(Flash flash)
	{
		if (flash.Root != null)
		{
			Destroy(flash.Root.GetComponent<MeshRenderer>().sharedMaterial);
			Destroy(flash.Mesh);
			Destroy(flash.Root);
		}
		if (flash.Cover != null)
		{
			Destroy(flash.Cover.GetComponent<MeshRenderer>().sharedMaterial);
			Destroy(flash.CoverMesh);
			Destroy(flash.Cover);
		}
		if (flash.OwnsCover && flash.CoverTexture != null)
		{
			Destroy(flash.CoverTexture);
		}
		flash.CoverTexture = null;
		flash.Root = null;
		flash.Cover = null;
	}

	private void OnDisable()
	{
		foreach (var flash in m_Flashes)
		{
			Clear(flash);
		}
		m_Flashes.Clear();
	}
}
