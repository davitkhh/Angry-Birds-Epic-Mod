using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Instrument shrapnel built at runtime (King DJ's MusicShrapnelVolley): no prefab or material asset. Each projectile
// takes one of the game's musical items (Matilda's harps, the Blues' horns; copies in Resources/Combatants/KingDJ), cuts it into
// a grid of jagged pieces and fires a single well-filled piece at the size the instrument has on a bird's rig. It
// tumbles along an arc to the target and bounces off when it lands. Drawn with Sprites/Default in front of the rig at its render queue.
public static class MusicShrapnelVisual
{
	// Every harp (Matilda's main hands) and horn (the Blues' off hands) in the game; each projectile picks one at random.
	private static readonly string[] TextureNames =
	{
		"Combatants/KingDJ/MusicShrapnel_Harp_01",
		"Combatants/KingDJ/MusicShrapnel_Harp_02",
		"Combatants/KingDJ/MusicShrapnel_Harp_03",
		"Combatants/KingDJ/MusicShrapnel_Harp_04",
		"Combatants/KingDJ/MusicShrapnel_Harp_05",
		"Combatants/KingDJ/MusicShrapnel_Harp_06",
		"Combatants/KingDJ/MusicShrapnel_Harp_07",
		"Combatants/KingDJ/MusicShrapnel_Harp_08",
		"Combatants/KingDJ/MusicShrapnel_Harp_09",
		"Combatants/KingDJ/MusicShrapnel_Horn_01",
		"Combatants/KingDJ/MusicShrapnel_Horn_02",
		"Combatants/KingDJ/MusicShrapnel_Horn_03",
		"Combatants/KingDJ/MusicShrapnel_Horn_04",
		"Combatants/KingDJ/MusicShrapnel_Horn_05",
		"Combatants/KingDJ/MusicShrapnel_Horn_06",
		"Combatants/KingDJ/MusicShrapnel_Horn_07",
		"Combatants/KingDJ/MusicShrapnel_Horn_08",
		"Combatants/KingDJ/MusicShrapnel_Horn_09"
	};

	// Every instrument breaks into Grid x Grid pieces; a projectile is a 2 x 2 block of them (empty pieces left out).
	private const int Grid = 3;

	private const float SpeedPx = 1500f;

	private const float MinFlight = 0.35f;

	private const float MaxFlight = 0.7f;

	// Height of the arc as a share of the distance.
	private const float ArcHeight = 0.15f;

	// How much the pieces drift apart by the time they land.
	private const float Loosen = 0.6f;

	private const float ScatterTime = 0.3f;

	private const float ScatterSpeedPx = 260f;

	private const float DepthInFront = 3f;

	private static Shader s_Shader;

	private static List<Instrument> s_Instruments;

	// A fragment is drawn this big relative to the whole instrument (longest side).
	private const float FragmentShare = 0.8f;

	// Starts one projectile on host after delay and returns the seconds until it lands. onLand runs when it lands.
	public static float Launch(MonoBehaviour host, Vector3 origin, Vector3 target, float pixelSize, float itemPixelSize, Transform parent, int layer, int renderQueue, float delay, Action onLand)
	{
		var instrument = PickInstrument();
		if (host == null || pixelSize <= 0f || instrument == null)
		{
			if (onLand != null)
			{
				onLand();
			}
			return 0f;
		}
		var distance = Vector2.Distance(origin, target);
		var flight = Mathf.Clamp(distance / (SpeedPx * pixelSize), MinFlight, MaxFlight);
		host.StartCoroutine(Run(instrument, origin, target, pixelSize, itemPixelSize, flight, delay, parent, layer, renderQueue, onLand));
		return delay + flight;
	}

	private static IEnumerator Run(Instrument instrument, Vector3 origin, Vector3 target, float pixelSize, float itemPixelSize, float flight, float delay, Transform parent, int layer, int renderQueue, Action onLand)
	{
		if (delay > 0f)
		{
			yield return new WaitForSeconds(delay);
		}
		if (s_Shader == null)
		{
			s_Shader = Shader.Find("Sprites/Default");
		}
		var root = new GameObject("MusicShrapnel");
		root.layer = layer;
		root.transform.position = new Vector3(origin.x, origin.y, origin.z - DepthInFront);
		if (parent != null)
		{
			root.transform.SetParent(parent, true);
		}
		var material = new Material(s_Shader) { renderQueue = renderQueue, mainTexture = instrument.Texture };
		var pieces = BuildPieces(instrument, itemPixelSize, root.transform, material, layer);
		var spin = (UnityEngine.Random.value < 0.5f ? -1f : 1f) * UnityEngine.Random.Range(480f, 840f);
		var distance = Vector2.Distance(origin, target);
		var elapsed = 0f;
		while (elapsed < flight && root != null)
		{
			var u = elapsed / flight;
			var position = Vector3.Lerp(origin, target, u) + Vector3.up * (ArcHeight * distance * 4f * u * (1f - u));
			position.z = Mathf.Lerp(origin.z, target.z, u) - DepthInFront;
			root.transform.position = position;
			var angle = spin * elapsed;
			foreach (var piece in pieces)
			{
				piece.Place(angle, elapsed, 1f + Loosen * u, 1f);
			}
			yield return null;
			elapsed += Time.deltaTime;
		}
		if (onLand != null)
		{
			onLand();
		}
		if (root != null)
		{
			var landAngle = spin * flight;
			var scatter = ScatterSpeedPx * pixelSize;
			elapsed = 0f;
			while (elapsed < ScatterTime && root != null)
			{
				var u = elapsed / ScatterTime;
				foreach (var piece in pieces)
				{
					piece.Scatter(landAngle, flight + elapsed, 1f + Loosen, elapsed * scatter, pixelSize);
				}
				var color = Color.white;
				color.a = 1f - u * u;
				material.color = color;
				yield return null;
				elapsed += Time.deltaTime;
			}
		}
		foreach (var piece in pieces)
		{
			UnityEngine.Object.Destroy(piece.Mesh);
		}
		UnityEngine.Object.Destroy(material);
		if (root != null)
		{
			UnityEngine.Object.Destroy(root);
		}
	}

	// ---------------------------------------------------------------- breaking an instrument

	private static Instrument PickInstrument()
	{
		if (s_Instruments == null)
		{
			s_Instruments = new List<Instrument>();
			foreach (var name in TextureNames)
			{
				var texture = Resources.Load<Texture2D>(name);
				if (texture != null)
				{
					s_Instruments.Add(new Instrument(texture));
				}
			}
		}
		return s_Instruments.Count > 0 ? s_Instruments[UnityEngine.Random.Range(0, s_Instruments.Count)] : null;
	}

	private static List<Piece> BuildPieces(Instrument instrument, float itemPixelSize, Transform root, Material material, int layer)
	{
		var cells = instrument.Shatter();
		// One broken piece: a random one among those that are mostly filled.
		var candidates = new List<Cell>();
		var best = 0f;
		foreach (var cell in cells)
		{
			best = Mathf.Max(best, cell.Coverage);
		}
		foreach (var cell in cells)
		{
			if (cell.Coverage >= best * 0.6f)
			{
				candidates.Add(cell);
			}
		}
		var chosen = new List<Cell> { candidates[UnityEngine.Random.Range(0, candidates.Count)] };
		var blockCenter = chosen[0].Center;
		var blockSize = 0f;
		foreach (var point in chosen[0].Points)
		{
			blockSize = Mathf.Max(blockSize, 2f * (point - blockCenter).magnitude);
		}
		// World units per texture pixel: the fragment is FragmentShare of the whole instrument's size as it is on a bird's
		// rig (one texture pixel = one rig pixel).
		var fullSize = Mathf.Max(instrument.Bounds.width, instrument.Bounds.height);
		var scale = itemPixelSize * FragmentShare * fullSize / Mathf.Max(blockSize, 1f);
		var pieces = new List<Piece>();
		foreach (var cell in chosen)
		{
			pieces.Add(new Piece(cell, instrument, blockCenter, scale, root, material, layer));
		}
		return pieces;
	}

	private class Instrument
	{
		public readonly Texture2D Texture;

		private readonly Color32[] m_Pixels;

		private readonly int m_Width;

		private readonly int m_Height;

		// The opaque part of the texture, in pixels (y up).
		private readonly Rect m_Bounds;

		// The instrument's opaque area in texture pixels.
		public Rect Bounds
		{
			get { return m_Bounds; }
		}

		public Instrument(Texture2D texture)
		{
			Texture = texture;
			m_Width = texture.width;
			m_Height = texture.height;
			try
			{
				m_Pixels = texture.GetPixels32();
			}
			catch (Exception)
			{
				// Not readable: every piece counts as filled.
				m_Pixels = null;
			}
			m_Bounds = FindBounds();
		}

		private Rect FindBounds()
		{
			if (m_Pixels == null)
			{
				return new Rect(0f, 0f, m_Width, m_Height);
			}
			int minX = m_Width, minY = m_Height, maxX = -1, maxY = -1;
			for (var y = 0; y < m_Height; y++)
			{
				for (var x = 0; x < m_Width; x++)
				{
					if (m_Pixels[y * m_Width + x].a > 64)
					{
						minX = Mathf.Min(minX, x);
						maxX = Mathf.Max(maxX, x);
						minY = Mathf.Min(minY, y);
						maxY = Mathf.Max(maxY, y);
					}
				}
			}
			return maxX < 0 ? new Rect(0f, 0f, m_Width, m_Height) : Rect.MinMaxRect(minX, minY, maxX + 1, maxY + 1);
		}

		public Vector2 ToUv(Vector2 pixel)
		{
			return new Vector2(pixel.x / m_Width, pixel.y / m_Height);
		}

		// Cuts the opaque part into Grid x Grid pieces along jagged lines: jittered grid corners plus a kinked point in
		// the middle of every edge, shared by the two pieces either side of it so they still fit together.
		public Cell[,] Shatter()
		{
			var cw = m_Bounds.width / Grid;
			var ch = m_Bounds.height / Grid;
			var corners = new Vector2[Grid + 1, Grid + 1];
			for (var i = 0; i <= Grid; i++)
			{
				for (var j = 0; j <= Grid; j++)
				{
					var point = new Vector2(m_Bounds.xMin + i * cw, m_Bounds.yMin + j * ch);
					if (i > 0 && i < Grid)
					{
						point.x += UnityEngine.Random.Range(-0.22f, 0.22f) * cw;
					}
					if (j > 0 && j < Grid)
					{
						point.y += UnityEngine.Random.Range(-0.22f, 0.22f) * ch;
					}
					corners[i, j] = point;
				}
			}
			// Kinks of the edges from corners[i, j] to corners[i + 1, j] and from corners[i, j] to corners[i, j + 1].
			var across = new Vector2[Grid, Grid + 1];
			var up = new Vector2[Grid + 1, Grid];
			for (var i = 0; i < Grid; i++)
			{
				for (var j = 0; j <= Grid; j++)
				{
					var kink = (corners[i, j] + corners[i + 1, j]) * 0.5f;
					if (j > 0 && j < Grid)
					{
						kink += new Vector2(UnityEngine.Random.Range(-0.12f, 0.12f) * cw, UnityEngine.Random.Range(-0.2f, 0.2f) * ch);
					}
					across[i, j] = kink;
				}
			}
			for (var i = 0; i <= Grid; i++)
			{
				for (var j = 0; j < Grid; j++)
				{
					var kink = (corners[i, j] + corners[i, j + 1]) * 0.5f;
					if (i > 0 && i < Grid)
					{
						kink += new Vector2(UnityEngine.Random.Range(-0.2f, 0.2f) * cw, UnityEngine.Random.Range(-0.12f, 0.12f) * ch);
					}
					up[i, j] = kink;
				}
			}
			var cells = new Cell[Grid, Grid];
			for (var i = 0; i < Grid; i++)
			{
				for (var j = 0; j < Grid; j++)
				{
					// Counter-clockwise from the bottom left corner.
					var points = new[]
					{
						corners[i, j], across[i, j], corners[i + 1, j], up[i + 1, j],
						corners[i + 1, j + 1], across[i, j + 1], corners[i, j + 1], up[i, j]
					};
					cells[i, j] = new Cell(points, GetCoverage(points));
				}
			}
			return cells;
		}

		// Share of opaque pixels in the piece's bounding box, sampled on a grid.
		private float GetCoverage(Vector2[] points)
		{
			if (m_Pixels == null)
			{
				return 1f;
			}
			var min = points[0];
			var max = points[0];
			foreach (var point in points)
			{
				min = Vector2.Min(min, point);
				max = Vector2.Max(max, point);
			}
			const int samples = 8;
			var filled = 0;
			for (var sx = 0; sx < samples; sx++)
			{
				for (var sy = 0; sy < samples; sy++)
				{
					var x = Mathf.Clamp((int)Mathf.Lerp(min.x, max.x, (sx + 0.5f) / samples), 0, m_Width - 1);
					var y = Mathf.Clamp((int)Mathf.Lerp(min.y, max.y, (sy + 0.5f) / samples), 0, m_Height - 1);
					if (m_Pixels[y * m_Width + x].a > 64)
					{
						filled++;
					}
				}
			}
			return (float)filled / (samples * samples);
		}
	}

	private class Cell
	{
		public readonly Vector2[] Points;

		public readonly Vector2 Center;

		public readonly float Coverage;

		public Cell(Vector2[] points, float coverage)
		{
			Points = points;
			Coverage = coverage;
			var sum = Vector2.zero;
			foreach (var point in points)
			{
				sum += point;
			}
			Center = sum / points.Length;
		}
	}

	private class Piece
	{
		public readonly Mesh Mesh;

		private readonly Transform m_Transform;

		// Position in the cluster, in world units, before spinning.
		private readonly Vector2 m_Offset;

		private readonly float m_OwnSpin;

		private readonly Vector2 m_ScatterDirection;

		public Piece(Cell cell, Instrument instrument, Vector2 blockCenter, float scale, Transform root, Material material, int layer)
		{
			var child = new GameObject("Piece");
			child.layer = layer;
			child.transform.SetParent(root, false);
			m_Transform = child.transform;
			m_Offset = (cell.Center - blockCenter) * scale;
			m_OwnSpin = UnityEngine.Random.Range(-300f, 300f);
			m_ScatterDirection = m_Offset.sqrMagnitude > 0.000001f ? m_Offset.normalized : UnityEngine.Random.insideUnitCircle.normalized;
			// Triangle fan around the centre of the piece.
			var count = cell.Points.Length;
			var vertices = new Vector3[count + 1];
			var uvs = new Vector2[count + 1];
			var colors = new Color[count + 1];
			vertices[0] = Vector3.zero;
			uvs[0] = instrument.ToUv(cell.Center);
			for (var i = 0; i < count; i++)
			{
				vertices[i + 1] = (cell.Points[i] - cell.Center) * scale;
				uvs[i + 1] = instrument.ToUv(cell.Points[i]);
			}
			for (var i = 0; i < colors.Length; i++)
			{
				colors[i] = Color.white;
			}
			var triangles = new int[count * 3];
			for (var i = 0; i < count; i++)
			{
				triangles[i * 3] = 0;
				triangles[i * 3 + 1] = (i + 1) % count + 1;
				triangles[i * 3 + 2] = i + 1;
			}
			Mesh = new Mesh();
			Mesh.vertices = vertices;
			Mesh.uv = uvs;
			Mesh.colors = colors;
			Mesh.triangles = triangles;
			Mesh.RecalculateBounds();
			child.AddComponent<MeshFilter>().sharedMesh = Mesh;
			var renderer = child.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
		}

		public void Place(float clusterAngle, float time, float spread, float size)
		{
			m_Transform.localPosition = Rotate(m_Offset * spread, clusterAngle * Mathf.Deg2Rad);
			m_Transform.localRotation = Quaternion.Euler(0f, 0f, clusterAngle + m_OwnSpin * time);
			m_Transform.localScale = new Vector3(size, size, 1f);
		}

		// Flying apart after the hit, dropping a little.
		public void Scatter(float clusterAngle, float time, float spread, float travelled, float pixelSize)
		{
			var direction = Rotate(m_ScatterDirection, clusterAngle * Mathf.Deg2Rad);
			var drop = new Vector2(0f, -travelled * travelled / (120f * pixelSize));
			m_Transform.localPosition = Rotate(m_Offset * spread, clusterAngle * Mathf.Deg2Rad) + direction * travelled + drop;
			m_Transform.localRotation = Quaternion.Euler(0f, 0f, clusterAngle + m_OwnSpin * 2f * time);
		}
	}

	private static Vector2 Rotate(Vector2 v, float radians)
	{
		var c = Mathf.Cos(radians);
		var s = Mathf.Sin(radians);
		return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
	}
}
