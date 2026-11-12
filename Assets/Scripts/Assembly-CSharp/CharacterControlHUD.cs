using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

public class CharacterControlHUD : MonoBehaviour
{
	private ICombatant m_Source;

	private ICombatant m_Target;

	private Vector3 m_TargetPos;

	[SerializeField]
	private Transform m_RotationControl;

	[SerializeField]
	private Transform m_BaseLine;

	[SerializeField]
	private Transform m_OrginMarker;

	[SerializeField]
	private Transform m_Marker;

	[SerializeField]
	private Material m_baseMaterial;

	[SerializeField]
	private Color m_supportColor;

	[SerializeField]
	private Color m_attackColor;

	[SerializeField]
	private Color m_neutraColor;

	// Marker colour over a target the skill lacks the ammo for
	[SerializeField]
	private Color m_noAmmoColor = new Color(0.9019608f, 0f, 0f, 1f);

	private Material m_groupSharedMaterial;

	private Quaternion m_BaseRotation;

	[SerializeField]
	private float m_LineOffsetToMarker = 90f;

	[SerializeField]
	private float m_LineOffsetToOrigin = 60f;

	[SerializeField]
	private float m_MaxLineOvalCutoff = 20f;

	private CharacterControllerBattleGroundBase m_CachedTargetCharacter;

	private CharacterControllerBattleGroundBase m_SourceCharacter;

	private Quaternion m_MarkerInitialRotation;

	private Vector3 targetPos;

	private bool m_Initialized;

	// Fog Machine (AttackAndForceMoveOrder): while the source is cursed, every piece of the HUD is drawn like the sound
	// wave: its shape filled with the fog's dark blue and a thin rainbow outline (Hidden/ABH/FogMachineOutline). The
	// outline's colour comes from the vertex colours of a copy of the mesh (SoundWaveVisual.ForcedMoveRainbow), whose
	// phase runs around the ring (by angle from its centre) and along the line (by distance from the source), so it
	// flows smoothly across the dashes. Blocked targets keep the usual invalid / no-ammo colour. The meshes must be readable (Mesh/Part_*,
	// SelectionLine, SelectionOrigin).
	private const float LinePhaseLength = 400f;

	private Renderer[] m_Renderers;

	private List<FogPiece> m_FogPieces;

	private Material m_FogMaterial;

	private bool m_FogApplied;

	private class FogPiece
	{
		public Renderer Renderer;

		public MeshFilter Filter;

		public Mesh Original;

		public Mesh Instance;

		public Vector3[] Vertices;

		public Color[] Colors;

		// Ring pieces: the ring's centre and the two local axes of its plane; null for line pieces.
		public Transform Center;

		public bool UseZ;
	}

	private bool m_ShowsBlockedTarget;

	// Over a target the Fog Machine curse forbids, the marker's ring (the Part_* dashes) gives way to this X.
	private GameObject m_ForbiddenX;

	private Renderer[] m_MarkerRingRenderers;

	private bool m_ShowsForbiddenX;

	// The X: built like the HUD's line and ring pieces, a flat face (the white bar of CharacterControlHUD.png) on upright
	// walls (its gray bar), so it is as 3D as they are; the Fog Machine shader fills the face dark blue with a rainbow
	// outline along the X's silhouette. Sizes match SelectionLine: faces 24 wide, walls inset 4 from the face edge.
	private const float ForbiddenXHalfLength = 62f;

	private const float ForbiddenXHalfWidth = 12f;

	private const float ForbiddenXFaceToWall = 4f;

	private const float ForbiddenXWallInset = 8f;

	private void Awake()
	{
		m_BaseRotation = m_RotationControl.rotation;
		m_groupSharedMaterial = new Material(m_baseMaterial);
		m_groupSharedMaterial.name = m_groupSharedMaterial.name + "_" + base.gameObject.name;
		m_MarkerInitialRotation = m_Marker.rotation;
		m_MarkerRingRenderers = m_Marker.GetComponentsInChildren<Renderer>(true);
		CreateForbiddenX();
		var componentsInChildren = GetComponentsInChildren<Renderer>(true);
		m_Renderers = componentsInChildren;
		var array = componentsInChildren;
		foreach (var renderer in array)
		{
			renderer.sharedMaterial = m_groupSharedMaterial;
		}
	}

	private void CreateForbiddenX()
	{
		m_ForbiddenX = new GameObject("FogMachineForbiddenX");
		m_ForbiddenX.layer = m_Marker.gameObject.layer;
		m_ForbiddenX.transform.SetParent(m_Marker, false);
		// Where the ring's dashes sit: their faces are at height 6 in the marker's xz plane, their walls below.
		m_ForbiddenX.transform.localPosition = new Vector3(0f, -8f, 8f);
		var vertices = new List<Vector3>();
		var uvs = new List<Vector2>();
		var triangles = new List<int>();
		var arms = new[] { 45f, 135f, 225f, 315f };
		// Walls first, so the faces drawn after them cover them (like SelectionLine / Part_*).
		foreach (var degrees in arms)
		{
			Vector3 along;
			Vector3 side;
			ArmAxes(degrees, out along, out side);
			// Long sides: from the inner corner to the tip, inset like the line's walls.
			AddWall(vertices, uvs, triangles, along * ForbiddenXWallInset + side * ForbiddenXWallInset, along * (ForbiddenXHalfLength - ForbiddenXFaceToWall) + side * ForbiddenXWallInset);
			AddWall(vertices, uvs, triangles, along * (ForbiddenXHalfLength - ForbiddenXFaceToWall) - side * ForbiddenXWallInset, along * ForbiddenXWallInset - side * ForbiddenXWallInset);
			// Across the tip.
			AddWall(vertices, uvs, triangles, along * (ForbiddenXHalfLength - ForbiddenXFaceToWall) + side * ForbiddenXWallInset, along * (ForbiddenXHalfLength - ForbiddenXFaceToWall) - side * ForbiddenXWallInset);
		}
		// Faces: a centre square from the bar's solid middle (no outline inside the X) and four arms whose root sits on the
		// bar's seam end, so their outline runs only along the X's silhouette.
		var visibleHalf = ForbiddenXHalfWidth * (7.5f / 11.75f);
		var h = new Vector3(0f, 6f, 0f);
		Vector3 a;
		Vector3 b;
		ArmAxes(45f, out a, out b);
		AddQuad(vertices, uvs, triangles, h - a * visibleHalf - b * visibleHalf, h + a * visibleHalf - b * visibleHalf, h - a * visibleHalf + b * visibleHalf, h + a * visibleHalf + b * visibleHalf, Texel(10f, 114f), Texel(22f, 114f), Texel(10f, 107f), Texel(22f, 107f));
		foreach (var degrees in arms)
		{
			Vector3 along;
			Vector3 side;
			ArmAxes(degrees, out along, out side);
			var root = h + along * visibleHalf;
			var tip = h + along * ForbiddenXHalfLength;
			AddQuad(vertices, uvs, triangles, root - side * ForbiddenXHalfWidth, tip - side * ForbiddenXHalfWidth, root + side * ForbiddenXHalfWidth, tip + side * ForbiddenXHalfWidth, Texel(1f, 122.7f), Texel(31f, 122.7f), Texel(1f, 99.2f), Texel(31f, 99.2f));
		}
		var mesh = new Mesh();
		mesh.name = "FogMachineForbiddenX";
		mesh.SetVertices(vertices);
		mesh.SetUVs(0, uvs);
		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateBounds();
		m_ForbiddenX.AddComponent<MeshFilter>().sharedMesh = mesh;
		m_ForbiddenX.AddComponent<MeshRenderer>();
		m_ForbiddenX.SetActive(false);
	}

	private static void ArmAxes(float degrees, out Vector3 along, out Vector3 side)
	{
		var radians = degrees * Mathf.Deg2Rad;
		along = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
		side = new Vector3(-along.z, 0f, along.x);
	}

	// UV of a texel of CharacterControlHUD.png (x right, y down from the top-left).
	private static Vector2 Texel(float x, float y)
	{
		return new Vector2(x / 256f, 1f - y / 256f);
	}

	// An upright wall from p0 to p1 (in the face plane), from height 9 down to -3, textured with the gray wall bar like
	// SelectionLine's walls.
	private static void AddWall(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, Vector3 p0, Vector3 p1)
	{
		var top = new Vector3(0f, 9f, 0f);
		var bottom = new Vector3(0f, -3f, 0f);
		AddQuad(vertices, uvs, triangles, p0 + bottom, p1 + bottom, p0 + top, p1 + top, Texel(65f, 126.7f), Texel(95f, 126.7f), Texel(65f, 115.2f), Texel(95f, 115.2f));
	}

	// Corners: 0 = start low, 1 = end low, 2 = start high, 3 = end high (the shader draws both sides).
	private static void AddQuad(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
	{
		var b = vertices.Count;
		vertices.Add(v0);
		vertices.Add(v1);
		vertices.Add(v2);
		vertices.Add(v3);
		uvs.Add(uv0);
		uvs.Add(uv1);
		uvs.Add(uv2);
		uvs.Add(uv3);
		triangles.Add(b);
		triangles.Add(b + 2);
		triangles.Add(b + 1);
		triangles.Add(b + 1);
		triangles.Add(b + 2);
		triangles.Add(b + 3);
	}

	private void ShowForbiddenX(bool show)
	{
		if (m_ForbiddenX == null || show == m_ShowsForbiddenX)
		{
			return;
		}
		m_ShowsForbiddenX = show;
		m_ForbiddenX.SetActive(show);
		foreach (var renderer in m_MarkerRingRenderers)
		{
			if (renderer != null)
			{
				renderer.enabled = !show;
			}
		}
	}

	public CharacterControlHUD SetCharacter(CharacterControllerBattleGround character)
	{
		m_SourceCharacter = character;
		m_Initialized = true;
		return this;
	}

	private void LateUpdate()
	{
		if (m_Initialized)
		{
			base.transform.position = m_SourceCharacter.transform.position;
		}
		UpdateFogMachineTint();
	}

	private void UpdateFogMachineTint()
	{
		var move = m_Initialized && m_SourceCharacter != null && m_SourceCharacter.GetModel() != null ? ABH.GameDatas.Battle.Skills.AttackAndForceMoveOrder.GetOrderedMove(m_SourceCharacter.GetModel()) : null;
		if (move.HasValue && !m_ShowsBlockedTarget && m_Renderers != null)
		{
			if (!m_FogApplied)
			{
				BeginFog();
			}
			m_FogMaterial.mainTexture = m_groupSharedMaterial.mainTexture;
			m_FogMaterial.color = new Color(1f, 1f, 1f, m_groupSharedMaterial.color.a);
			m_FogMaterial.SetColor("_CoreColor", SoundWaveVisual.FogColor);
			var time = Time.time;
			foreach (var piece in m_FogPieces)
			{
				if (piece.Renderer == null || piece.Instance == null)
				{
					continue;
				}
				var pieceTransform = piece.Renderer.transform;
				for (var v = 0; v < piece.Vertices.Length; v++)
				{
					var world = pieceTransform.TransformPoint(piece.Vertices[v]);
					float phase;
					if (piece.Center != null)
					{
						var local = piece.Center.InverseTransformPoint(world);
						phase = Mathf.Atan2(piece.UseZ ? local.z : local.y, local.x) / (2f * Mathf.PI);
					}
					else
					{
						phase = Vector3.Distance(world, base.transform.position) / LinePhaseLength;
					}
					piece.Colors[v] = SoundWaveVisual.ForcedMoveRainbow(time, move.Value, phase);
				}
				piece.Instance.colors = piece.Colors;
			}
		}
		else if (m_FogApplied)
		{
			EndFog();
		}
	}

	private void BeginFog()
	{
		if (m_FogMaterial == null)
		{
			m_FogMaterial = new Material(Shader.Find("Hidden/ABH/FogMachineOutline") ?? Shader.Find("Sprites/Default"));
			m_FogMaterial.name = "FogMachineHUD_" + base.gameObject.name;
			m_FogMaterial.renderQueue = m_groupSharedMaterial.renderQueue;
		}
		m_FogPieces = new List<FogPiece>();
		foreach (var renderer in m_Renderers)
		{
			var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
			if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
			{
				continue;
			}
			var piece = new FogPiece
			{
				Renderer = renderer,
				Filter = filter,
				Original = filter.sharedMesh,
				Instance = Instantiate(filter.sharedMesh)
			};
			piece.Vertices = piece.Instance.vertices;
			piece.Colors = new Color[piece.Vertices.Length];
			// Ring dashes turn around their frame; the origin ring is one mesh centred on itself.
			if (renderer.name.StartsWith("Part_"))
			{
				piece.Center = renderer.transform.parent;
			}
			else if (renderer.name.Contains("Origin"))
			{
				piece.Center = renderer.transform;
			}
			filter.sharedMesh = piece.Instance;
			renderer.sharedMaterial = m_FogMaterial;
			m_FogPieces.Add(piece);
		}
		// Each ring lies in its centre's local xy or xz plane: pick the one its vertices spread over.
		foreach (var group in m_FogPieces.Where(p => p.Center != null).GroupBy(p => p.Center))
		{
			var y = new Vector2(float.MaxValue, float.MinValue);
			var z = new Vector2(float.MaxValue, float.MinValue);
			foreach (var piece in group)
			{
				foreach (var vertex in piece.Vertices)
				{
					var local = group.Key.InverseTransformPoint(piece.Renderer.transform.TransformPoint(vertex));
					y = new Vector2(Mathf.Min(y.x, local.y), Mathf.Max(y.y, local.y));
					z = new Vector2(Mathf.Min(z.x, local.z), Mathf.Max(z.y, local.z));
				}
			}
			var useZ = z.y - z.x > y.y - y.x;
			foreach (var piece in group)
			{
				piece.UseZ = useZ;
			}
		}
		m_FogApplied = true;
	}

	private void EndFog()
	{
		if (m_FogPieces != null)
		{
			foreach (var piece in m_FogPieces)
			{
				if (piece.Filter != null)
				{
					piece.Filter.sharedMesh = piece.Original;
				}
				if (piece.Renderer != null)
				{
					piece.Renderer.sharedMaterial = m_groupSharedMaterial;
				}
				if (piece.Instance != null)
				{
					Destroy(piece.Instance);
				}
			}
		}
		m_FogPieces = null;
		m_FogApplied = false;
	}

	private void OnDestroy()
	{
		EndFog();
		if (m_FogMaterial != null)
		{
			Destroy(m_FogMaterial);
		}
	}

	public void SetState(Transform sourceTransform, CharacterControllerBattleGroundBase target, Vector3 targetP, BattleMgrBase battleMgr, bool isInvalidTarget = false, bool noAmmo = false)
	{
		var flag = target != null;
		if (m_CachedTargetCharacter && m_CachedTargetCharacter != target && m_CachedTargetCharacter.GetControlHUD() && !battleMgr.CharacterIsActing(m_CachedTargetCharacter.GetModel().CombatantNameId))
		{
			m_CachedTargetCharacter.GetControlHUD().gameObject.SetActive(!m_CachedTargetCharacter.GetModel().ActedThisTurn);
		}
		m_CachedTargetCharacter = target;
		var faction = !flag ? Faction.None : target.GetModel().CombatantFaction;
		var position = sourceTransform.position;
		targetPos = !flag ? targetP : target.transform.position;
		if (!flag && targetPos.y > battleMgr.m_UpperBorder.position.y)
		{
			targetPos = new Vector3(targetPos.x, Mathf.Min(targetPos.y, battleMgr.m_UpperBorder.position.y), Mathf.Min(targetPos.z, battleMgr.m_UpperBorder.position.z));
		}
		// A target the Fog Machine curse forbids keeps the fog look, and the marker becomes an X.
		var sourceModel = m_SourceCharacter != null ? m_SourceCharacter.GetModel() : null;
		var forbidden = flag && sourceModel != null && sourceTransform != target.transform && battleMgr != null && ABH.GameDatas.Battle.Skills.AttackAndForceMoveOrder.IsMoveBlocked(battleMgr.Model, sourceModel, target.GetModel().CombatantFaction == sourceModel.CombatantFaction);
		ShowForbiddenX(forbidden);
		m_ShowsBlockedTarget = flag && (noAmmo || (isInvalidTarget && !forbidden));
		if (noAmmo && flag)
		{
			m_groupSharedMaterial.color = m_noAmmoColor;
		}
		else if (faction == Faction.Birds && !isInvalidTarget)
		{
			m_groupSharedMaterial.color = m_supportColor;
		}
		else if (faction == Faction.Pigs && !isInvalidTarget)
		{
			m_groupSharedMaterial.color = m_attackColor;
		}
		else
		{
			m_groupSharedMaterial.color = m_neutraColor;
		}
		var right = sourceTransform.right;
		var to = targetPos - sourceTransform.position;
		var flag2 = flag && sourceTransform == target.transform;
		m_BaseLine.gameObject.SetActive(!flag2);
		m_OrginMarker.gameObject.SetActive(!flag2);
		m_Marker.localPosition = new Vector3(to.magnitude, 0f, 0f);
		m_Marker.rotation = m_MarkerInitialRotation;
		m_RotationControl.localRotation = Quaternion.Euler(new Vector3(m_RotationControl.localRotation.eulerAngles.x, Vector3.Angle(right, to) * (float)(targetPos.z <= sourceTransform.position.z ? 1 : -1)));
		m_BaseLine.localPosition = new Vector3(Mathf.Max(25f, m_MaxLineOvalCutoff * (1f - GetOvalCutoff(m_RotationControl.rotation.eulerAngles.y))), 0f, 0f);
		m_BaseLine.localScale = new Vector3(to.magnitude - m_LineOffsetToOrigin - m_LineOffsetToMarker - 1f * (m_MaxLineOvalCutoff * (1f - GetOvalCutoff(m_RotationControl.rotation.eulerAngles.y))), 1f, 1f);
		if (target && target.GetControlHUD() && target.GetControlHUD() != this) //&& target.GetModel().ActedThisTurn)
		{
			target.GetControlHUD().gameObject.SetActive(false);
		}
	}

	public float GetOvalCutoff(float eulerAngle)
	{
		if (eulerAngle <= 90f)
		{
			return eulerAngle / 90f;
		}
		if (eulerAngle <= 180f)
		{
			return (90f - (eulerAngle - 90f)) / 90f;
		}
		if (eulerAngle <= 270f)
		{
			return (eulerAngle - 180f) / 90f;
		}
		return (90f - (eulerAngle - 270f)) / 90f;
	}

	public void ResetControlHUD()
	{
		m_BaseLine.gameObject.SetActive(false);
		m_OrginMarker.gameObject.SetActive(false);
		m_BaseLine.localScale = new Vector3(0f, 1f, 1f);
		m_groupSharedMaterial.color = m_neutraColor;
		m_ShowsBlockedTarget = false;
		ShowForbiddenX(false);
		m_Marker.localPosition = Vector3.zero;
		m_Marker.rotation = m_MarkerInitialRotation;
		if (m_CachedTargetCharacter && m_CachedTargetCharacter.GetControlHUD() && !m_CachedTargetCharacter.m_BattleMgr.CharacterIsActing(m_CachedTargetCharacter.GetModel().CombatantNameId))
		{
			m_CachedTargetCharacter.GetControlHUD().gameObject.SetActive(!m_CachedTargetCharacter.GetModel().ActedThisTurn);
		}
		m_CachedTargetCharacter = null;
	}
}
