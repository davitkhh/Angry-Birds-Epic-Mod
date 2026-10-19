using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The swap crystal prop (Resources/Prefabs/0_Generic/3_SwapCrystal/SwapCrystal, the character asset of a
// SwapCrystalCombatant): a red/blue crystal floating over a podium with a ground shadow like a pig's. Both colors show
// on the crystal; the primary one fills its front faces and its glow. Driven by SwapCrystalDirector.
public class SwapCrystalView : MonoBehaviour
{
	public static readonly Color Red = new Color(1f, 0.22f, 0.2f);

	public static readonly Color Blue = new Color(0.22f, 0.5f, 1f);

	// Floating part (crystal and glow); bobs around its start position.
	public Transform m_Crystal;

	public Renderer m_CrystalRenderer;

	public Renderer[] m_PodiumRenderers;

	public Texture2D m_GlowTexture;

	// Soft dark ellipse under the podium.
	public Texture2D m_ShadowTexture;

	public Vector2 m_ShadowSize = new Vector2(170f, 46f);

	// Where the shadow sits, in the podium's space.
	public Vector3 m_ShadowPosition = new Vector3(0f, -24f, 2f);

	// How far the crystal rises over the podium while it swaps.
	public float m_LiftHeight = 18f;

	// Used by PigSwapMorph for the pigs this crystal transforms.
	public Material m_PigMorphMaterial;

	// Saturation of the transforming pigs' fill: 0 = grey, 1 = default, 2 = fully saturated (see GetPigTintColors).
	// A crystal's "pig_saturation" skill parameter overrides it.
	public float m_PigTintSaturation = 1f;

	public float m_BobHeight = 6f;

	public float m_BobSpeed = 1.6f;

	public float m_GlowSize = 150f;

	// Swap beam (PlayBeam): a ray of light from the crystal that turns about the X axis, tipping toward the camera
	// from straight up to straight down and on around, flickering red/blue, for as long as its sound plays.
	public Texture2D m_BeamTexture;

	public float m_BeamLength = 520f;

	public float m_BeamBaseWidth = 26f;

	public float m_BeamTipWidth = 120f;

	// Seconds from pointing straight up to straight down (constant speed).
	public float m_BeamHalfTurnSeconds = 1f;

	// Red/blue switches per second.
	public float m_BeamFlickerRate = 8f;

	public float m_BeamOpacity = 0.85f;

	public string m_BeamSoundId = "SkillFX_Blessing_MagicalConcealment_Start";

	// How long the beam stays if its sound can't be found.
	public float m_BeamFallbackSeconds = 1.5f;

	// How long before the crystal's highest point (end of the swap's tint-in) the beam starts.
	public float m_BeamLeadSeconds = 0.15f;

	private readonly List<Material> m_Materials = new List<Material>();

	// The podium's stars swap colors with the crystal (its texture pair is red-first / blue-first).
	private readonly List<Material> m_PodiumMaterials = new List<Material>();

	private readonly List<Mesh> m_Meshes = new List<Mesh>();

	// Not faded with the rest: their color is the beam's flicker.
	private readonly List<Material> m_BeamMaterials = new List<Material>();

	private Transform m_Beam;

	private Coroutine m_BeamRoutine;

	private Material m_CrystalMaterial;

	private Material m_GlowMaterial;

	private Vector3 m_CrystalStart;

	private float m_BlueAmount;

	private float m_Flash;

	private float m_Lift;

	private float m_Alpha;

	private bool m_AboutToSwap;

	private float m_Time;

	private void Awake()
	{
		m_CrystalStart = m_Crystal.localPosition;
		m_CrystalMaterial = m_CrystalRenderer.material;
		m_Materials.Add(m_CrystalMaterial);
		foreach (var podium in m_PodiumRenderers)
		{
			m_PodiumMaterials.Add(podium.material);
		}
		m_Materials.AddRange(m_PodiumMaterials);
		CreateGlow();
		CreateGroundShadow();
		CreateBeam();
		SetAlpha(0f);
	}

	private void CreateBeam()
	{
		m_Beam = new GameObject("Beam").transform;
		m_Beam.gameObject.layer = base.gameObject.layer;
		m_Beam.parent = m_Crystal;
		// Pivot at the crystal's center, just behind it, so the crystal draws over the beam's base.
		m_Beam.localPosition = new Vector3(0f, 0f, 0.5f);
		m_Beam.localRotation = Quaternion.identity;
		m_Beam.localScale = Vector3.one;
		// A wide colored ray with a narrow, paler core.
		CreateBeamPart("Ray", m_BeamBaseWidth, m_BeamTipWidth, 0f);
		CreateBeamPart("Core", m_BeamBaseWidth * 0.35f, m_BeamTipWidth * 0.3f, -0.1f);
		m_Beam.gameObject.SetActive(false);
	}

	// A quad tapering from baseWidth at the pivot to tipWidth at m_BeamLength straight up.
	private void CreateBeamPart(string name, float baseWidth, float tipWidth, float z)
	{
		var part = new GameObject(name);
		part.layer = base.gameObject.layer;
		part.transform.parent = m_Beam;
		part.transform.localPosition = new Vector3(0f, 0f, z);
		part.transform.localRotation = Quaternion.identity;
		part.transform.localScale = Vector3.one;
		var mesh = new Mesh
		{
			name = "SwapCrystalBeam" + name,
			vertices = new[] { new Vector3(-baseWidth * 0.5f, 0f, 0f), new Vector3(-tipWidth * 0.5f, m_BeamLength, 0f), new Vector3(tipWidth * 0.5f, m_BeamLength, 0f), new Vector3(baseWidth * 0.5f, 0f, 0f) },
			uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) },
			colors = new[] { Color.white, Color.white, Color.white, Color.white },
			triangles = new[] { 0, 1, 2, 0, 2, 3 }
		};
		part.AddComponent<MeshFilter>().sharedMesh = mesh;
		var material = new Material(m_CrystalMaterial);
		material.SetTexture("_MainTex", m_BeamTexture);
		material.SetTexture("_AltTex", m_BeamTexture);
		material.SetFloat("_GlintStrength", 0f);
		part.AddComponent<MeshRenderer>().sharedMaterial = material;
		m_BeamMaterials.Add(material);
		m_Meshes.Add(mesh);
	}

	// Starts the beam and its sound after delay; it lasts as long as the sound.
	public void PlayBeam(float delay)
	{
		if (m_BeamRoutine != null)
		{
			StopCoroutine(m_BeamRoutine);
		}
		m_BeamRoutine = StartCoroutine(BeamRoutine(delay));
	}

	private IEnumerator BeamRoutine(float delay)
	{
		if (delay > 0f)
		{
			yield return new WaitForSeconds(delay);
		}
		var duration = SoundLength(m_BeamSoundId);
		if (duration <= 0f)
		{
			duration = m_BeamFallbackSeconds;
		}
		DIContainerInfrastructure.AudioManager.PlaySound(m_BeamSoundId);
		var startBlue = m_BlueAmount > 0.5f;
		m_Beam.gameObject.SetActive(true);
		for (var t = 0f; t < duration; t += Time.deltaTime)
		{
			// Negative angles tip the top of the beam toward the camera (-z) first.
			m_Beam.localRotation = Quaternion.Euler(-180f * t / Mathf.Max(0.01f, m_BeamHalfTurnSeconds), 0f, 0f);
			var blue = ((int)(t * m_BeamFlickerRate) % 2 == 0) == startBlue;
			var fade = Mathf.Clamp01(t / 0.1f) * Mathf.Clamp01((duration - t) / 0.15f);
			SetBeamColor(blue ? Blue : Red, m_BeamOpacity * fade * m_Alpha);
			yield return null;
		}
		m_Beam.gameObject.SetActive(false);
		m_BeamRoutine = null;
	}

	private void SetBeamColor(Color color, float alpha)
	{
		for (var i = 0; i < m_BeamMaterials.Count; i++)
		{
			// The core is paler than the ray.
			var c = i == 0 ? color : Color.Lerp(color, Color.white, 0.6f);
			c.a = alpha;
			m_BeamMaterials[i].SetColor("_Color", c);
		}
	}

	// Length of the clip behind a sound key, or 0 if it can't be found.
	private static float SoundLength(string soundId)
	{
		var audio = DIContainerInfrastructure.AudioManager;
		var sound = audio != null ? audio.GetSound(soundId) as Sound : null;
		if (sound == null)
		{
			return 0f;
		}
		var assetName = sound.AssetNames.Count > 0 ? sound.AssetNames[0] : sound.NameId;
		var provider = DIContainerInfrastructure.GetAudioAssetProvider(sound.AssetPrioritiy);
		var clip = provider != null ? provider.GetAudioClip(assetName) : null;
		return clip != null ? clip.length : 0f;
	}

	private void CreateGlow()
	{
		// Behind the crystal (larger z is further from the camera).
		m_GlowMaterial = CreateQuad("Glow", m_Crystal, new Vector3(0f, 0f, 1f), new Vector2(m_GlowSize, m_GlowSize), m_GlowTexture);
	}

	private void CreateGroundShadow()
	{
		var podium = m_PodiumRenderers.Length > 0 ? m_PodiumRenderers[0].transform.parent : base.transform;
		CreateQuad("GroundShadow", podium, m_ShadowPosition, m_ShadowSize, m_ShadowTexture);
	}

	// A textured quad drawn with the crystal's shader; returns its material (faded with the rest).
	private Material CreateQuad(string name, Transform parent, Vector3 localPosition, Vector2 size, Texture2D texture)
	{
		var quad = new GameObject(name);
		quad.layer = base.gameObject.layer;
		quad.transform.parent = parent;
		quad.transform.localPosition = localPosition;
		quad.transform.localRotation = Quaternion.identity;
		quad.transform.localScale = Vector3.one;
		var half = size * 0.5f;
		var mesh = new Mesh
		{
			name = "SwapCrystal" + name,
			vertices = new[] { new Vector3(0f - half.x, 0f - half.y, 0f), new Vector3(0f - half.x, half.y, 0f), new Vector3(half.x, half.y, 0f), new Vector3(half.x, 0f - half.y, 0f) },
			uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) },
			colors = new[] { Color.white, Color.white, Color.white, Color.white },
			triangles = new[] { 0, 1, 2, 0, 2, 3 }
		};
		quad.AddComponent<MeshFilter>().sharedMesh = mesh;
		var material = new Material(m_CrystalMaterial);
		material.SetTexture("_MainTex", texture);
		material.SetTexture("_AltTex", texture);
		material.SetFloat("_GlintStrength", 0f);
		quad.AddComponent<MeshRenderer>().sharedMaterial = material;
		m_Materials.Add(material);
		m_Meshes.Add(mesh);
		return material;
	}

	private void OnDestroy()
	{
		foreach (var material in m_Materials)
		{
			Destroy(material);
		}
		foreach (var material in m_BeamMaterials)
		{
			Destroy(material);
		}
		foreach (var mesh in m_Meshes)
		{
			Destroy(mesh);
		}
	}

	public void SetBlueAmount(float blueAmount)
	{
		m_BlueAmount = blueAmount;
	}

	public void SetFlash(float flash)
	{
		m_Flash = flash;
	}

	// 0 = resting, 1 = risen by m_LiftHeight.
	public void SetLift(float lift)
	{
		m_Lift = lift;
	}

	// The next pig turn swaps: the glow pulses faster and stronger.
	public void SetAboutToSwap(bool aboutToSwap)
	{
		m_AboutToSwap = aboutToSwap;
	}

	public IEnumerator Appear(float duration)
	{
		for (var t = 0f; t < duration; t += Time.deltaTime)
		{
			SetAlpha(Mathf.SmoothStep(0f, 1f, t / duration));
			yield return null;
		}
		SetAlpha(1f);
	}

	// Fades out, then destroys destroyTarget (the crystal's battle character view, which holds this asset).
	public void Vanish(float duration, GameObject destroyTarget)
	{
		StartCoroutine(VanishAndDestroy(duration, destroyTarget));
	}

	private IEnumerator VanishAndDestroy(float duration, GameObject destroyTarget)
	{
		var start = m_Alpha;
		for (var t = 0f; t < duration; t += Time.deltaTime)
		{
			SetAlpha(Mathf.Lerp(start, 0f, t / duration));
			yield return null;
		}
		Destroy(destroyTarget != null ? destroyTarget : base.gameObject);
	}

	private void SetAlpha(float alpha)
	{
		m_Alpha = alpha;
		var color = new Color(1f, 1f, 1f, alpha);
		foreach (var material in m_Materials)
		{
			if (material != m_GlowMaterial)
			{
				material.SetColor("_Color", color);
			}
		}
	}

	private void Update()
	{
		m_Time += Time.deltaTime;
		m_Crystal.localPosition = m_CrystalStart + new Vector3(0f, Mathf.Sin(m_Time * m_BobSpeed) * m_BobHeight + m_Lift * m_LiftHeight, 0f);
		m_CrystalMaterial.SetFloat("_Blend", m_BlueAmount);
		foreach (var material in m_PodiumMaterials)
		{
			material.SetFloat("_Blend", m_BlueAmount);
		}
		m_CrystalMaterial.SetFloat("_Flash", m_Flash);
		var pulse = m_AboutToSwap ? 0.6f + 0.4f * Mathf.Sin(m_Time * 7f) : 0.45f + 0.12f * Mathf.Sin(m_Time * 2f);
		var glow = Color.Lerp(Red, Blue, m_BlueAmount);
		glow.a = m_Alpha * Mathf.Clamp01(pulse + m_Flash);
		m_GlowMaterial.SetColor("_Color", glow);
	}

	// Crystal colors: a dark fill for the inside of a transforming pig, a lighter one for its rim, and an even
	// lighter, more saturated border (all HSL, so the border really is both lighter and more saturated).
	// saturation: 0 = grey, 1 = default, 2 = fully saturated (in between, each color moves toward full saturation).
	public static Color[] GetPigTintColors(bool blue, float saturation)
	{
		var hue = blue ? 0.6f : 0f;
		return new[] { Hsl(hue, Saturate(0.75f, saturation), 0.3f), Hsl(hue, Saturate(0.75f, saturation), 0.66f), Hsl(hue, Saturate(1f, saturation), 0.8f) };
	}

	private static float Saturate(float s, float saturation)
	{
		return saturation <= 1f ? s * Mathf.Max(0f, saturation) : Mathf.Lerp(s, 1f, Mathf.Clamp01(saturation - 1f));
	}

	private static Color Hsl(float h, float s, float l)
	{
		var c = (1f - Mathf.Abs(2f * l - 1f)) * s;
		var hp = h * 6f;
		var x = c * (1f - Mathf.Abs(hp % 2f - 1f));
		Color rgb;
		if (hp < 1f)
		{
			rgb = new Color(c, x, 0f);
		}
		else if (hp < 2f)
		{
			rgb = new Color(x, c, 0f);
		}
		else if (hp < 3f)
		{
			rgb = new Color(0f, c, x);
		}
		else if (hp < 4f)
		{
			rgb = new Color(0f, x, c);
		}
		else if (hp < 5f)
		{
			rgb = new Color(x, 0f, c);
		}
		else
		{
			rgb = new Color(c, 0f, x);
		}
		var m = l - c / 2f;
		return new Color(rgb.r + m, rgb.g + m, rgb.b + m, 1f);
	}
}
