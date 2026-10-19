using System.Collections.Generic;
using System.Linq;
using SmoothMoves;
using UnityEngine;

// A ground shadow for a flying character whose own shadow is hidden while it flies (Wiz Pig's FlyingState). Drawn with the rig's own Shadow texture on its battle slot's ground line, under the character, and scaled by how
// high above that line the character is: full (battle) size on the ground, nothing at the height of its first flight
// pose (its entrance starts offscreen). Hidden whenever the rig's own shadow is showing or the character is hidden.
public class GroundShadow : MonoBehaviour
{
	private Transform m_Character;

	private Transform m_Rig;

	private BoneAnimation m_Bones;

	private Vector3 m_Ground;

	private float m_FullHeight;

	private GameObject m_Quad;

	// The rig's own shadow follows a state change one frame late (SmoothMoves applies FlyingState's hidden shadow in the
	// mesh a frame after the switch), so the ground shadow follows it one frame late too: no gap and no double shadow at
	// take-off or landing.
	private bool m_WasFlying;

	// Slightly behind the character (further from the camera) so it draws under it.
	private const float DepthBehind = 2f;

	// Characters whose shadow something else draws for now (an ActionTree MovePath with its own ground shadow).
	private static readonly HashSet<Transform> s_Suppressed = new HashSet<Transform>();

	public static void Suppress(Transform character)
	{
		s_Suppressed.RemoveWhere(t => t == null);
		s_Suppressed.Add(character);
	}

	private bool IsSuppressed()
	{
		for (var t = m_Rig; t != null; t = t.parent)
		{
			if (s_Suppressed.Contains(t))
			{
				return true;
			}
		}
		return false;
	}

	public static GroundShadow Attach(CharacterControllerBattleGroundBase character)
	{
		return character == null ? null : Attach(character.transform, character.m_AssetController);
	}

	public static GroundShadow Attach(Transform character, CharacterAssetController asset)
	{
		if (asset == null || asset.m_BoneAnimation == null)
		{
			return null;
		}
		var atlas = asset.m_BoneAnimation.textureAtlases.FirstOrDefault(a => a != null && a.textureNames.Contains("Shadow"));
		var bone = asset.m_BoneAnimation.GetBoneTransform("Shadow");
		if (atlas == null || bone == null)
		{
			return null;
		}
		var shadow = new GameObject("GroundShadow_" + character.name).AddComponent<GroundShadow>();
		shadow.m_Character = character;
		shadow.m_Rig = asset.transform;
		shadow.m_Bones = asset.m_BoneAnimation;
		shadow.m_Ground = asset.transform.position;
		shadow.m_Quad = new GameObject("Quad");
		shadow.m_Quad.transform.SetParent(shadow.transform, false);
		var sprite = shadow.m_Quad.AddComponent<CHMeshSprite>();
		sprite.m_AtlasType = AtlasTypes.SmoothMoves;
		sprite.m_SmoothMovesAtlas = atlas;
		sprite.m_SpriteName = "Shadow";
		sprite.UpdateSprite(true, true);
		UnityHelper.SetLayerRecusively(shadow.gameObject, asset.gameObject.layer);
		shadow.m_Quad.SetActive(false);
		return shadow;
	}

	private void LateUpdate()
	{
		if (m_Character == null)
		{
			Destroy(gameObject);
			return;
		}
		// FlyingState hides the rig's own shadow inside the mesh (its Shadow bone transform keeps its scale), so check the
		// state itself; HoveringState (same layer) ends it.
		var flying = m_WasFlying;
		m_WasFlying = m_Bones.IsPlaying("FlyingState");
		if (!flying || !m_Character.gameObject.activeInHierarchy || IsSuppressed())
		{
			m_Quad.SetActive(false);
			return;
		}
		var height = m_Rig.position.y - m_Ground.y;
		if (m_FullHeight <= 0f && height > 1f)
		{
			m_FullHeight = height;
		}
		var size = m_FullHeight > 0f ? Mathf.Clamp01(1f - height / m_FullHeight) : 1f;
		m_Quad.SetActive(size > 0.01f);
		transform.position = new Vector3(m_Rig.position.x, m_Ground.y, m_Ground.z + DepthBehind);
		transform.rotation = m_Rig.rotation;
		transform.localScale = m_Rig.lossyScale * size;
	}
}
