using System.Collections;
using UnityEngine;

// Sound wave projectile built at runtime (King DJ's AttackAndForceMoveOrder): no prefab, texture or material asset.
// Three styles, picked by Style (editor menu Tools/Build/King DJ/Sound Wave Style; stored in PlayerPrefs): the ribbon below,
// and the earlier physically accurate and longitudinal waves (ArcWave). In every style the sound fades with distance
// (Falloff: narrower lines, smaller swings); everything is fully opaque.
// Ribbon: a transverse wave along the line from the muzzle to the target: a ribbon that swings from side to side, square to
// the line of flight. The wave pattern lies along the whole path, but only a short window of it (WindowWaves
// wavelengths, tapered at both ends) shows at a time, and that window travels from the muzzle to the target and a
// little past it. The pattern doesn't travel with the window (its crests only drift slowly at PhaseSpeedPx), so as the
// window moves the crests inside it shift phase instead of riding along. Amplitude, wavelength and width never change. The ribbon is dark blue in its core with rainbow edges and ends, with hues that
// keep cycling. Coloured notes ride along beside it. Drawn with Sprites/Default and vertex colours, in front of the rig
// at its render queue.
public static class SoundWaveVisual
{
	private const int Segments = 120;

	// Rows across the ribbon: outer rim, outer edge, core, inner edge, inner rim.
	private const int Rows = 5;

	// Sizes and speed in rig pixels (scaled to the world by the caller's pixel size).
	private const float WavelengthPx = 70f;

	private const float AmplitudePx = 34f;

	// How fast the crests run along the ribbon.
	private const float PhaseSpeedPx = 650f;

	// Length of the visible window, in wavelengths.
	private const float WindowWaves = 3f;

	// Half the dark core, and the rainbow edge outside it on each side.
	private const float CoreHalfPx = 10f;

	private const float RimPx = 2f;

	private const float NoteSizePx = 34f;

	private const float SpeedPx = 1300f;

	private const int NoteCount = 8;

	// Flight time to the target stays within these whatever the distance.
	private const float MinFlight = 0.35f;

	private const float MaxFlight = 0.8f;

	// How far in front of the origin (towards the camera) the wave is drawn.
	private const float DepthInFront = 3f;

	// #1B223A
	private static readonly Color CoreColor = new Color32(0x1B, 0x22, 0x3A, 0xFF);

	private static Shader s_Shader;

	private static Texture2D s_NoteTexture;

	public enum WaveStyle
	{
		PhysicallyAccurate = 0,
		Longitudinal = 1,
		Ribbon = 2
	}

	public const string StylePrefsKey = "KingDJ.SoundWaveStyle";

	// Which wave Fog Machine fires; read at every launch, so it can be switched mid-play.
	public static WaveStyle Style
	{
		get
		{
			var value = PlayerPrefs.GetInt(StylePrefsKey, (int)WaveStyle.Ribbon);
			return value >= 0 && value <= (int)WaveStyle.Ribbon ? (WaveStyle)value : WaveStyle.Ribbon;
		}
		set
		{
			PlayerPrefs.SetInt(StylePrefsKey, (int)value);
			PlayerPrefs.Save();
		}
	}

	// Fog Machine's dark blue, for things marking a cursed combatant (CharacterControlHUD's outline shader).
	public static Color FogColor
	{
		get { return CoreColor; }
	}

	// The rainbow of the thin outline on things marking a cursed combatant (CharacterControlHUD): like the wave's edges,
	// its hue keeps cycling, but it lingers on the forced move's colour (red = attack, green = support) and passes
	// quickly through the rest. phase shifts the cycle along the outline.
	public static Color ForcedMoveRainbow(float time, bool support, float phase)
	{
		var emphasis = support ? 1f / 3f : 0f;
		var d = Mathf.Repeat(time * 0.9f * 0.5f + phase, 1f) - 0.5f;
		var hue = Mathf.Repeat(emphasis + 4f * d * d * d, 1f);
		return Color.HSVToRGB(hue, 0.8f, 1f);
	}

	// Rainbow rims are at least this many screen pixels wide: thinner lines lose pixels without anti-aliasing and look
	// see-through.
	private const float MinRimScreenPixels = 1.5f;

	// The rainbow on each rim fades in and out (into the core's dark blue) along the line: this many fades per rig
	// pixel of line, drifting at RimFadeSpeed cycles per second. The two rims are half a cycle apart.
	private const float RimFadeWavesPerPx = 1f / 90f;

	private const float RimFadeSpeed = 1.2f;

	// The rim colour at a point: one hue across the line (both rims share it), faded towards the core blue in and out
	// along the line. side is 0 for one rim and 1 for the other; along is the position along the line in world units.
	private static Color RimColor(float hue, float along, float time, float pixelSize, int side)
	{
		var phase = along / Mathf.Max(pixelSize, 1e-6f) * RimFadeWavesPerPx - time * RimFadeSpeed + side * 0.5f;
		var shown = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * phase);
		shown = shown * shown * (3f - 2f * shown);
		return Color.Lerp(CoreColor, Color.HSVToRGB(hue, 0.8f, 1f), shown);
	}

	// Distance at which the sound starts to fade (rig pixels).
	private const float FalloffStartPx = 40f;

	// How strong the sound still is at distance d: like a pressure wave from a point source (1 / sqrt(d)), clamped so
	// the far end still reads. Shown as narrower lines and smaller swings.
	private static float Falloff(float distance, float start)
	{
		return Mathf.Clamp(1.3f * Mathf.Sqrt(start / Mathf.Max(distance, start)), 0.45f, 1f);
	}

	// World units per screen pixel at a point, for the camera that draws layer.
	private static float WorldPerScreenPixel(int layer, Vector3 at)
	{
		foreach (var camera in Camera.allCameras)
		{
			if ((camera.cullingMask & (1 << layer)) == 0 || camera.pixelHeight <= 0)
			{
				continue;
			}
			if (camera.orthographic)
			{
				return 2f * camera.orthographicSize / camera.pixelHeight;
			}
			var depth = Mathf.Abs(Vector3.Dot(at - camera.transform.position, camera.transform.forward));
			return 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / camera.pixelHeight;
		}
		return 0f;
	}

	// Starts the wave on host and returns the seconds until its leading front reaches target.
	public static float Launch(MonoBehaviour host, Vector3 origin, Vector3 target, float pixelSize, Transform parent, int layer, int renderQueue)
	{
		var distance = Vector2.Distance(origin, target);
		if (host == null || distance <= 0.0001f || pixelSize <= 0f)
		{
			return 0f;
		}
		var speed = Mathf.Clamp(SpeedPx * pixelSize, distance / MaxFlight, distance / MinFlight);
		host.StartCoroutine(Run(origin, target, pixelSize, speed, parent, layer, renderQueue));
		return distance / speed;
	}

	private static IEnumerator Run(Vector3 origin, Vector3 target, float pixelSize, float speed, Transform parent, int layer, int renderQueue)
	{
		if (s_Shader == null)
		{
			s_Shader = Shader.Find("Sprites/Default");
		}
		var root = new GameObject("SoundWave");
		root.layer = layer;
		root.transform.position = new Vector3(origin.x, origin.y, origin.z - DepthInFront);
		if (parent != null)
		{
			root.transform.SetParent(parent, true);
		}
		var waveMaterial = new Material(s_Shader) { renderQueue = renderQueue };
		var noteMaterial = new Material(s_Shader) { renderQueue = renderQueue, mainTexture = GetNoteTexture() };
		var waveMesh = CreateMeshObject(root, "Fronts", waveMaterial, layer);
		var noteMesh = CreateMeshObject(root, "Notes", noteMaterial, layer);
		var style = Style;
		var minRim = MinRimScreenPixels * WorldPerScreenPixel(layer, origin);
		IWave wave = style == WaveStyle.Ribbon ? (IWave)new Wave(origin, target, pixelSize, speed, minRim) : new ArcWave(origin, target, pixelSize, speed, style == WaveStyle.Longitudinal, minRim);
		wave.InitMeshes(waveMesh, noteMesh);
		var elapsed = 0f;
		while (elapsed < wave.Lifetime && root != null)
		{
			wave.Update(elapsed, root.transform, waveMesh, noteMesh);
			yield return null;
			elapsed += Time.deltaTime;
		}
		Object.Destroy(waveMesh);
		Object.Destroy(noteMesh);
		Object.Destroy(waveMaterial);
		Object.Destroy(noteMaterial);
		if (root != null)
		{
			Object.Destroy(root);
		}
	}

	private static Mesh CreateMeshObject(GameObject root, string name, Material material, int layer)
	{
		var child = new GameObject(name);
		child.layer = layer;
		child.transform.SetParent(root.transform, false);
		var mesh = new Mesh();
		mesh.MarkDynamic();
		child.AddComponent<MeshFilter>().sharedMesh = mesh;
		var renderer = child.AddComponent<MeshRenderer>();
		renderer.sharedMaterial = material;
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		return mesh;
	}

	private class Wave : IWave
	{
		private readonly Vector2 m_Origin;

		private readonly Vector2 m_Direction;

		private readonly Vector2 m_Across;

		private readonly float m_Distance;

		private readonly float m_Speed;

		private readonly float m_Wavelength;

		private readonly float m_Amplitude;

		private readonly float m_PhaseSpeed;

		private readonly float m_WindowLength;

		private readonly float m_CoreHalf;

		private readonly float m_Rim;

		private readonly float m_FalloffStart;

		private readonly float m_PixelSize;

		private readonly float m_NoteSize;

		private readonly float m_Depth;

		private Vector3[] m_WaveVertices;

		private Color[] m_WaveColors;

		private Vector3[] m_NoteVertices;

		private Color[] m_NoteColors;

		public float Lifetime { get; private set; }

		public Wave(Vector3 origin, Vector3 target, float pixelSize, float speed, float minRim)
		{
			m_Origin = origin;
			m_Direction = ((Vector2)target - (Vector2)origin).normalized;
			m_Across = new Vector2(-m_Direction.y, m_Direction.x);
			m_Distance = Vector2.Distance(origin, target);
			m_Speed = speed;
			m_Wavelength = WavelengthPx * pixelSize;
			m_Amplitude = AmplitudePx * pixelSize;
			m_PhaseSpeed = PhaseSpeedPx * pixelSize;
			m_WindowLength = WindowWaves * m_Wavelength;
			m_CoreHalf = CoreHalfPx * pixelSize;
			m_Rim = Mathf.Max(RimPx * pixelSize, minRim);
			m_PixelSize = pixelSize;
			m_FalloffStart = FalloffStartPx * pixelSize;
			m_NoteSize = NoteSizePx * pixelSize;
			m_Depth = origin.z - DepthInFront;
			// Over once the tail of the window has passed the clip limit.
			Lifetime = (m_Distance + 0.5f * m_Wavelength + m_WindowLength) / m_Speed;
		}

		public void InitMeshes(Mesh waveMesh, Mesh noteMesh)
		{
			m_WaveVertices = new Vector3[(Segments + 1) * Rows];
			m_WaveColors = new Color[m_WaveVertices.Length];
			var triangles = new int[Segments * (Rows - 1) * 6];
			var t = 0;
			for (var s = 0; s < Segments; s++)
			{
				for (var row = 0; row < Rows - 1; row++)
				{
					var a = s * Rows + row;
					var b = a + Rows;
					triangles[t++] = a;
					triangles[t++] = b;
					triangles[t++] = a + 1;
					triangles[t++] = a + 1;
					triangles[t++] = b;
					triangles[t++] = b + 1;
				}
			}
			waveMesh.vertices = m_WaveVertices;
			waveMesh.colors = m_WaveColors;
			waveMesh.triangles = triangles;
			m_NoteVertices = new Vector3[NoteCount * 4];
			m_NoteColors = new Color[m_NoteVertices.Length];
			var uvs = new Vector2[m_NoteVertices.Length];
			var noteTriangles = new int[NoteCount * 6];
			for (var n = 0; n < NoteCount; n++)
			{
				// The texture holds two glyphs side by side: a single note and a beamed pair.
				var u0 = n % 2 == 0 ? 0f : 0.5f;
				uvs[n * 4] = new Vector2(u0, 0f);
				uvs[n * 4 + 1] = new Vector2(u0 + 0.5f, 0f);
				uvs[n * 4 + 2] = new Vector2(u0, 1f);
				uvs[n * 4 + 3] = new Vector2(u0 + 0.5f, 1f);
				noteTriangles[n * 6] = n * 4;
				noteTriangles[n * 6 + 1] = n * 4 + 2;
				noteTriangles[n * 6 + 2] = n * 4 + 1;
				noteTriangles[n * 6 + 3] = n * 4 + 1;
				noteTriangles[n * 6 + 4] = n * 4 + 2;
				noteTriangles[n * 6 + 5] = n * 4 + 3;
			}
			noteMesh.vertices = m_NoteVertices;
			noteMesh.colors = m_NoteColors;
			noteMesh.uv = uvs;
			noteMesh.triangles = noteTriangles;
		}

		public void Update(float time, Transform space, Mesh waveMesh, Mesh noteMesh)
		{
			UpdateRibbon(time, space);
			UpdateNotes(time, space);
			waveMesh.vertices = m_WaveVertices;
			waveMesh.colors = m_WaveColors;
			waveMesh.RecalculateBounds();
			noteMesh.vertices = m_NoteVertices;
			noteMesh.colors = m_NoteColors;
			noteMesh.RecalculateBounds();
		}

		// u runs from the tail (0) to the head (1) of the visible window, whose head moves at the flight speed. Returns
		// the distance along the line of flight.
		private float DistanceAt(float time, float u)
		{
			return time * m_Speed - m_WindowLength * (1f - u);
		}

		// The window tapers to nothing at both ends.
		private static float Envelope(float u)
		{
			// sin(PI) comes out a hair below zero, and a fractional power of a negative is NaN (which hides the mesh).
			return Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(u))), 0.6f);
		}

		// Sideways swing at that point. The phase depends on the place along the path, not on the window, so the window
		// slides over the pattern and the crests in it shift phase.
		private float Swing(float time, float u)
		{
			return m_Amplitude * Falloff(Mathf.Max(0f, DistanceAt(time, u)), m_FalloffStart) * Envelope(u) * Mathf.Sin(2f * Mathf.PI * (DistanceAt(time, u) - time * m_PhaseSpeed) / m_Wavelength);
		}

		// The ribbon is solid (fully opaque, no feathered edges). It is cut cleanly at the muzzle and a little past the
		// target instead of fading: only distances in [0, ClipLimit] are drawn.
		private float ClipLimit
		{
			get { return m_Distance + 0.5f * m_Wavelength; }
		}

		private bool IsShown(float distance)
		{
			return distance >= 0f && distance <= ClipLimit;
		}

		private Vector2 PointAt(float time, float u)
		{
			return m_Origin + m_Direction * Mathf.Max(0f, DistanceAt(time, u)) + m_Across * Swing(time, u);
		}

		private void UpdateRibbon(float time, Transform space)
		{
			// The part of the window between the muzzle and the clip limit; all segments go there.
			var head = time * m_Speed;
			var uMin = Mathf.Clamp01(1f - head / m_WindowLength);
			var uMax = Mathf.Clamp01(1f - (head - ClipLimit) / m_WindowLength);
			var shown = uMax > uMin + 1e-4f;
			var step = Mathf.Max(1e-4f, (uMax - uMin) / Segments);
			for (var s = 0; s <= Segments; s++)
			{
				var u = Mathf.Lerp(uMin, uMax, (float)s / Segments);
				var point = PointAt(time, u);
				var tangent = PointAt(time, Mathf.Min(1f, u + step * 0.5f)) - PointAt(time, Mathf.Max(0f, u - step * 0.5f));
				var normal = tangent.sqrMagnitude > 1e-10f ? new Vector2(-tangent.y, tangent.x).normalized : m_Across;
				// Rounded ends: the ribbon narrows and turns rainbow towards both ends of the window.
				var end = Mathf.Clamp01(Mathf.Min(u, 1f - u) / 0.15f);
				var endShape = Mathf.Sin(end * Mathf.PI * 0.5f);
				// The core narrows at the ends and as the sound fades; the rainbow rims keep their width.
				var core = m_CoreHalf * Mathf.Lerp(0.35f, 1f, endShape) * Mathf.Lerp(0.5f, 1f, Falloff(Mathf.Max(0f, DistanceAt(time, u)), m_FalloffStart));
				var rowOffsets = new[] { core + m_Rim, core, 0f, -core, -(core + m_Rim) };
				var hue = Mathf.Repeat(time * 0.9f + u * 0.85f, 1f);
				var along = Mathf.Max(0f, DistanceAt(time, u));
				var rainbowOuter = RimColor(hue, along, time, m_PixelSize, 0);
				var rainbowInner = RimColor(hue, along, time, m_PixelSize, 1);
				for (var row = 0; row < Rows; row++)
				{
					var index = s * Rows + row;
					var world = point + normal * rowOffsets[row];
					m_WaveVertices[index] = space.InverseTransformPoint(new Vector3(world.x, world.y, m_Depth));
					Color color;
					if (row == 2)
					{
						color = Color.Lerp(Color.HSVToRGB(hue, 0.8f, 1f), CoreColor, endShape);
					}
					else
					{
						color = row < 2 ? rainbowOuter : rainbowInner;
					}
					color.a = shown ? 1f : 0f;
					m_WaveColors[index] = color;
				}
			}
		}

		private void UpdateNotes(float time, Transform space)
		{
			for (var n = 0; n < NoteCount; n++)
			{
				// Spread along the window, alternating sides, just outside the ribbon's swing.
				var u = (n + 0.5f) / NoteCount;
				var side = n % 2 == 0 ? 1f : -1f;
				var distance = DistanceAt(time, u);
				var bob = Mathf.Sin(time * 10f + n * 1.7f) * m_Amplitude * 0.3f;
				var center = PointAt(time, u) + m_Across * side * (m_Amplitude * 1.2f + m_NoteSize * 0.5f + bob);
				var pop = Mathf.Clamp01(distance / (0.5f * m_Wavelength));
				var size = m_NoteSize * Mathf.Lerp(0.8f, 1.2f, Mathf.Repeat(n * 0.37f, 1f)) * (pop < 1f ? pop * 1.15f : 1f);
				var tilt = (Mathf.Sin(time * 7f + n) * 18f) * Mathf.Deg2Rad;
				var right = Rotate(Vector2.right, tilt) * size * 0.5f;
				var up = Rotate(Vector2.up, tilt) * size * 0.5f;
				var color = Color.HSVToRGB(Mathf.Repeat(n * 0.21f + time * 0.6f, 1f), 0.7f, 1f);
				color.a = IsShown(distance) ? 1f : 0f;
				var corners = new[] { center - right - up, center + right - up, center - right + up, center + right + up };
				for (var c = 0; c < 4; c++)
				{
					m_NoteVertices[n * 4 + c] = space.InverseTransformPoint(new Vector3(corners[c].x, corners[c].y, m_Depth - 0.5f));
					m_NoteColors[n * 4 + c] = color;
				}
			}
		}

		private static Vector2 Rotate(Vector2 v, float radians)
		{
			var c = Mathf.Cos(radians);
			var s = Mathf.Sin(radians);
			return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
		}
	}

	private interface IWave
	{
		float Lifetime { get; }

		void InitMeshes(Mesh waveMesh, Mesh noteMesh);

		void Update(float time, Transform space, Mesh waveMesh, Mesh noteMesh);
	}

	// The two earlier styles, drawn with the current look (dark blue core, thin full rainbow rims, fully opaque).
	// Physically accurate: a point source sends a burst of FrontCount wavefronts; every front is a circular arc centred
	// on the muzzle whose radius grows at one speed, so the fronts stay a wavelength apart and flatten as they travel; their
	// strength (shown as width) falls off with distance (1 / sqrt(r), clamped) under a burst envelope.
	// Longitudinal: the same burst as a plane wave: straight compression fronts of one fixed length, square to the line of
	// flight, sliding along it without growing. Both cut off a little past the target.
	private class ArcWave : IWave
	{
		private const int FrontCount = 5;

		private const int ArcSegments = 24;

		// Half the opening angle of the physically accurate arcs, in degrees.
		private const float HalfAngle = 28f;

		private const float ArcWavelengthPx = 40f;

		// Half the length of every longitudinal front, across the line of flight.
		private const float HalfLengthPx = 60f;

		private readonly bool m_Plane;

		private readonly Vector2 m_Origin;

		private readonly Vector2 m_Direction;

		private readonly Vector2 m_Across;

		private readonly float m_Distance;

		private readonly float m_Speed;

		private readonly float m_Wavelength;

		private readonly float m_CoreHalf;

		private readonly float m_Rim;

		private readonly float m_FalloffStart;

		private readonly float m_PixelSize;

		private readonly float m_HalfLength;

		private readonly float m_NoteSize;

		private readonly float m_Depth;

		private readonly float m_Clip;

		private Vector3[] m_WaveVertices;

		private Color[] m_WaveColors;

		private Vector3[] m_NoteVertices;

		private Color[] m_NoteColors;

		public float Lifetime { get; private set; }

		public ArcWave(Vector3 origin, Vector3 target, float pixelSize, float speed, bool plane, float minRim)
		{
			m_Plane = plane;
			m_Origin = origin;
			m_Direction = ((Vector2)target - (Vector2)origin).normalized;
			m_Across = new Vector2(-m_Direction.y, m_Direction.x);
			m_Distance = Vector2.Distance(origin, target);
			m_Speed = speed;
			m_Wavelength = ArcWavelengthPx * pixelSize;
			m_CoreHalf = CoreHalfPx * pixelSize;
			m_Rim = Mathf.Max(RimPx * pixelSize, minRim);
			m_PixelSize = pixelSize;
			m_FalloffStart = FalloffStartPx * pixelSize;
			m_HalfLength = HalfLengthPx * pixelSize;
			m_NoteSize = NoteSizePx * pixelSize;
			m_Depth = origin.z - DepthInFront;
			m_Clip = m_Distance + 1.5f * m_Wavelength;
			// Over once the last front has passed the clip distance.
			Lifetime = (m_Clip + (FrontCount - 1) * m_Wavelength) / m_Speed;
		}

		public void InitMeshes(Mesh waveMesh, Mesh noteMesh)
		{
			var columns = ArcSegments + 1;
			m_WaveVertices = new Vector3[FrontCount * columns * Rows];
			m_WaveColors = new Color[m_WaveVertices.Length];
			var triangles = new int[FrontCount * ArcSegments * (Rows - 1) * 6];
			var t = 0;
			for (var front = 0; front < FrontCount; front++)
			{
				var frontStart = front * columns * Rows;
				for (var s = 0; s < ArcSegments; s++)
				{
					for (var row = 0; row < Rows - 1; row++)
					{
						var a = frontStart + s * Rows + row;
						var b = a + Rows;
						triangles[t++] = a;
						triangles[t++] = b;
						triangles[t++] = a + 1;
						triangles[t++] = a + 1;
						triangles[t++] = b;
						triangles[t++] = b + 1;
					}
				}
			}
			waveMesh.vertices = m_WaveVertices;
			waveMesh.colors = m_WaveColors;
			waveMesh.triangles = triangles;
			InitNoteMesh(noteMesh, out m_NoteVertices, out m_NoteColors);
		}

		public void Update(float time, Transform space, Mesh waveMesh, Mesh noteMesh)
		{
			UpdateFronts(time, space);
			UpdateNotes(time, space);
			waveMesh.vertices = m_WaveVertices;
			waveMesh.colors = m_WaveColors;
			waveMesh.RecalculateBounds();
			noteMesh.vertices = m_NoteVertices;
			noteMesh.colors = m_NoteColors;
			noteMesh.RecalculateBounds();
		}

		private void UpdateFronts(float time, Transform space)
		{
			var columns = ArcSegments + 1;
			var period = m_Wavelength / m_Speed;
			var baseAngle = Mathf.Atan2(m_Direction.y, m_Direction.x);
			for (var front = 0; front < FrontCount; front++)
			{
				var frontStart = front * columns * Rows;
				var age = time - front * period;
				var radius = Mathf.Max(0f, age * m_Speed);
				var shown = age > 0f && radius <= m_Clip;
				// Burst envelope: the middle of the pulse is the strongest.
				var strength = Mathf.Lerp(0.55f, 1f, Mathf.Sin(Mathf.PI * (front + 0.5f) / FrontCount));
				// The sound fades with distance (both styles).
				strength *= Falloff(radius, m_FalloffStart);
				for (var s = 0; s <= ArcSegments; s++)
				{
					var along = (float)s / ArcSegments;
					Vector2 point;
					Vector2 normal;
					if (m_Plane)
					{
						normal = m_Direction;
						point = m_Origin + m_Direction * radius + m_Across * (Mathf.Lerp(-1f, 1f, along) * m_HalfLength);
					}
					else
					{
						var angle = baseAngle + Mathf.Lerp(-HalfAngle, HalfAngle, along) * Mathf.Deg2Rad;
						normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
						point = m_Origin + normal * radius;
					}
					// Rounded ends: the band narrows and turns rainbow towards the ends of the front.
					var end = Mathf.Clamp01(Mathf.Min(along, 1f - along) / 0.2f);
					var endShape = Mathf.Sin(end * Mathf.PI * 0.5f);
					// The core narrows at the ends and as the sound fades; the rainbow rims keep their width.
					var core = m_CoreHalf * Mathf.Lerp(0.35f, 1f, endShape) * Mathf.Lerp(0.5f, 1f, strength);
					var rowOffsets = new[] { core + m_Rim, core, 0f, -core, -(core + m_Rim) };
					var hue = Mathf.Repeat(time * 0.9f + along * 0.85f + front * 0.17f, 1f);
					var rainbowOuter = RimColor(hue, along * 2f * m_HalfLength, time, m_PixelSize, 0);
					var rainbowInner = RimColor(hue, along * 2f * m_HalfLength, time, m_PixelSize, 1);
					for (var row = 0; row < Rows; row++)
					{
						var index = frontStart + s * Rows + row;
						var offset = rowOffsets[row];
						if (!m_Plane)
						{
							// Arcs never reach behind their centre.
							offset = Mathf.Max(offset, -radius);
						}
						var world = point + normal * offset;
						m_WaveVertices[index] = space.InverseTransformPoint(new Vector3(world.x, world.y, m_Depth));
						var color = row == 2 ? Color.Lerp(Color.HSVToRGB(hue, 0.8f, 1f), CoreColor, endShape) : (row < 2 ? rainbowOuter : rainbowInner);
						color.a = shown ? 1f : 0f;
						m_WaveColors[index] = color;
					}
				}
			}
		}

		private void UpdateNotes(float time, Transform space)
		{
			var emitSpan = (FrontCount - 1) * m_Wavelength / m_Speed;
			for (var n = 0; n < NoteCount; n++)
			{
				var seed = Mathf.Repeat(n * 0.618034f + 0.11f, 1f);
				var age = time - emitSpan * n / NoteCount;
				var radius = age * m_Speed + m_Wavelength * 0.3f;
				var bob = Mathf.Sin(time * 10f + n * 1.7f) * m_Wavelength * 0.35f;
				Vector2 center;
				if (m_Plane)
				{
					// Lanes across the beam, bobbing along the line of flight like the medium does.
					var lane = Mathf.Lerp(-0.85f, 0.85f, seed) * m_HalfLength;
					center = m_Origin + m_Direction * (radius + bob) + m_Across * lane;
				}
				else
				{
					var heading = Rotate(m_Direction, Mathf.Lerp(-1.15f, 1.15f, seed) * HalfAngle * Mathf.Deg2Rad);
					center = m_Origin + heading * radius + m_Across * bob;
				}
				var pop = Mathf.Clamp01(age / 0.12f);
				var size = m_NoteSize * Mathf.Lerp(0.8f, 1.2f, Mathf.Repeat(n * 0.37f, 1f)) * (pop < 1f ? pop * 1.15f : 1f);
				var tilt = (Mathf.Sin(time * 7f + n) * 18f) * Mathf.Deg2Rad;
				var right = Rotate(Vector2.right, tilt) * size * 0.5f;
				var up = Rotate(Vector2.up, tilt) * size * 0.5f;
				var color = Color.HSVToRGB(Mathf.Repeat(n * 0.21f + time * 0.6f, 1f), 0.7f, 1f);
				color.a = age > 0f && radius <= m_Clip ? 1f : 0f;
				var corners = new[] { center - right - up, center + right - up, center - right + up, center + right + up };
				for (var c = 0; c < 4; c++)
				{
					m_NoteVertices[n * 4 + c] = space.InverseTransformPoint(new Vector3(corners[c].x, corners[c].y, m_Depth - 0.5f));
					m_NoteColors[n * 4 + c] = color;
				}
			}
		}

		private static Vector2 Rotate(Vector2 v, float radians)
		{
			var c = Mathf.Cos(radians);
			var s = Mathf.Sin(radians);
			return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
		}
	}

	// Note quads: the texture holds two glyphs side by side (a single note and a beamed pair).
	private static void InitNoteMesh(Mesh noteMesh, out Vector3[] vertices, out Color[] colors)
	{
		vertices = new Vector3[NoteCount * 4];
		colors = new Color[vertices.Length];
		var uvs = new Vector2[vertices.Length];
		var triangles = new int[NoteCount * 6];
		for (var n = 0; n < NoteCount; n++)
		{
			var u0 = n % 2 == 0 ? 0f : 0.5f;
			uvs[n * 4] = new Vector2(u0, 0f);
			uvs[n * 4 + 1] = new Vector2(u0 + 0.5f, 0f);
			uvs[n * 4 + 2] = new Vector2(u0, 1f);
			uvs[n * 4 + 3] = new Vector2(u0 + 0.5f, 1f);
			triangles[n * 6] = n * 4;
			triangles[n * 6 + 1] = n * 4 + 2;
			triangles[n * 6 + 2] = n * 4 + 1;
			triangles[n * 6 + 3] = n * 4 + 1;
			triangles[n * 6 + 4] = n * 4 + 2;
			triangles[n * 6 + 5] = n * 4 + 3;
		}
		noteMesh.vertices = vertices;
		noteMesh.colors = colors;
		noteMesh.uv = uvs;
		noteMesh.triangles = triangles;
	}

	// Two glyphs with a dark blue fill and a white outline (the vertex colour turns the outline rainbow): an eighth note
	// and a beamed pair, 64 x 64 pixels each, side by side.
	private static Texture2D GetNoteTexture()
	{
		if (s_NoteTexture != null)
		{
			return s_NoteTexture;
		}
		const int cell = 64;
		const int samples = 4;
		const float outline = 5f;
		var texture = new Texture2D(cell * 2, cell, TextureFormat.RGBA32, false);
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = FilterMode.Bilinear;
		var pixels = new Color[cell * 2 * cell];
		for (var y = 0; y < cell; y++)
		{
			for (var x = 0; x < cell * 2; x++)
			{
				var glyph = x / cell;
				var inner = 0f;
				var outer = 0f;
				for (var sy = 0; sy < samples; sy++)
				{
					for (var sx = 0; sx < samples; sx++)
					{
						var px = x % cell + (sx + 0.5f) / samples;
						var py = y + (sy + 0.5f) / samples;
						if (InsideGlyph(glyph, px, py, 0f))
						{
							inner += 1f;
						}
						if (InsideGlyph(glyph, px, py, outline))
						{
							outer += 1f;
						}
					}
				}
				inner /= samples * samples;
				outer /= samples * samples;
				// Dark fill (the wave's core blue) and a white outline that the vertex colour turns rainbow.
				var color = Color.Lerp(Color.white, CoreColor, outer > 0f ? inner / outer : 0f);
				color.a = outer;
				pixels[y * cell * 2 + x] = color;
			}
		}
		texture.SetPixels(pixels);
		texture.Apply(false, true);
		s_NoteTexture = texture;
		return s_NoteTexture;
	}

	// Glyph shapes in a 64 x 64 cell, y up; grow inflates them for the outline.
	private static bool InsideGlyph(int glyph, float x, float y, float grow)
	{
		if (glyph == 0)
		{
			return InsideHead(x, y, 22f, 15f, grow) || InsideRect(x, y, 29.5f, 15f, 33.5f, 55f, grow) || NearPolyline(x, y, new[] { new Vector2(31.5f, 54f), new Vector2(42f, 47f), new Vector2(46f, 39f), new Vector2(42f, 31f) }, 3.2f + grow);
		}
		if (InsideHead(x, y, 14f, 13f, grow) || InsideHead(x, y, 43f, 17f, grow))
		{
			return true;
		}
		if (InsideRect(x, y, 21.5f, 13f, 25.5f, 51f, grow) || InsideRect(x, y, 50.5f, 17f, 54.5f, 55f, grow))
		{
			return true;
		}
		// Beam: a slanted band joining the stem tops.
		if (x < 21.5f - grow || x > 54.5f + grow)
		{
			return false;
		}
		var bottom = Mathf.Lerp(45f, 49f, (x - 21.5f) / 33f);
		return y >= bottom - grow && y <= bottom + 7f + grow;
	}

	private static bool InsideHead(float x, float y, float cx, float cy, float grow)
	{
		// Tilted ellipse, like a printed note head.
		var dx = x - cx;
		var dy = y - cy;
		var c = Mathf.Cos(0.35f);
		var s = Mathf.Sin(0.35f);
		var u = dx * c + dy * s;
		var v = -dx * s + dy * c;
		var a = 10.5f + grow;
		var b = 7.5f + grow;
		return u * u / (a * a) + v * v / (b * b) <= 1f;
	}

	private static bool InsideRect(float x, float y, float x0, float y0, float x1, float y1, float grow)
	{
		return x >= x0 - grow && x <= x1 + grow && y >= y0 - grow && y <= y1 + grow;
	}

	private static bool NearPolyline(float x, float y, Vector2[] points, float radius)
	{
		var p = new Vector2(x, y);
		for (var i = 0; i < points.Length - 1; i++)
		{
			var a = points[i];
			var ab = points[i + 1] - a;
			var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
			if ((a + ab * t - p).sqrMagnitude <= radius * radius)
			{
				return true;
			}
		}
		return false;
	}
}
