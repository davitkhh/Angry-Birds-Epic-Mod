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

	public float m_BobHeight = 6f;

	public float m_BobSpeed = 1.6f;

	public float m_GlowSize = 150f;

	private readonly List<Material> m_Materials = new List<Material>();

	// The podium's stars swap colors with the crystal (its texture pair is red-first / blue-first).
	private readonly List<Material> m_PodiumMaterials = new List<Material>();

	private readonly List<Mesh> m_Meshes = new List<Mesh>();

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
		SetAlpha(0f);
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
	public static Color[] GetPigTintColors(bool blue)
	{
		var hue = blue ? 0.6f : 0f;
		return new[] { Hsl(hue, 0.75f, 0.3f), Hsl(hue, 0.75f, 0.66f), Hsl(hue, 1f, 0.8f) };
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
