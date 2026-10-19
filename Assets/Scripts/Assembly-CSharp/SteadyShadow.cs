using System.Linq;
using SmoothMoves;
using UnityEngine;

// Draws a rig's shadow as its own quad, following the Shadow bone's animated sprite, and collapses the rig's own shadow
// quad. Red's rig has fixed submesh runs; texture changes during some clips (Hit, Action_Attack_1) push his shadow quad
// into a run with another atlas's material, where it draws nothing. Collapsing (not hiding) keeps the quad count, so the
// other parts' runs are unchanged.
[DefaultExecutionOrder(100)]
public class SteadyShadow : MonoBehaviour
{
	// Written over the rig's shadow sprite scale each frame; a different value means the animation rewrote it.
	private static readonly Vector3 Collapsed = new Vector3(0.0001f, 0.0001f, 1f);

	// Slightly behind the rig (further from the camera) so the shadow draws under it.
	private const float DepthBehind = 1f;

	private Transform m_Sprite;

	private Transform m_Quad;

	private Vector3 m_AnimatedScale = Vector3.one;

	public static SteadyShadow Attach(CharacterAssetController asset)
	{
		if (asset == null || asset.m_BoneAnimation == null)
		{
			return null;
		}
		var atlas = asset.m_BoneAnimation.textureAtlases.FirstOrDefault(a => a != null && a.textureNames.Contains("Shadow"));
		var bone = asset.m_BoneAnimation.GetBoneTransform("Shadow");
		var sprite = bone != null ? bone.Find("Shadow_Sprite") : null;
		if (atlas == null || sprite == null || asset.GetComponent<SteadyShadow>() != null)
		{
			return null;
		}
		var steady = asset.gameObject.AddComponent<SteadyShadow>();
		steady.m_Sprite = sprite;
		steady.m_AnimatedScale = sprite.localScale;
		var quad = new GameObject("SteadyShadow");
		quad.transform.SetParent(bone, false);
		var mesh = quad.AddComponent<CHMeshSprite>();
		mesh.m_AtlasType = AtlasTypes.SmoothMoves;
		mesh.m_SmoothMovesAtlas = atlas;
		mesh.m_SpriteName = "Shadow";
		mesh.UpdateSprite(true, true);
		UnityHelper.SetLayerRecusively(quad, asset.gameObject.layer);
		steady.m_Quad = quad.transform;
		return steady;
	}

	private void LateUpdate()
	{
		if (m_Sprite == null || m_Quad == null)
		{
			return;
		}
		if (m_Sprite.localScale != Collapsed)
		{
			m_AnimatedScale = m_Sprite.localScale;
		}
		m_Quad.localPosition = m_Sprite.localPosition + new Vector3(0f, 0f, DepthBehind);
		m_Quad.localRotation = m_Sprite.localRotation;
		m_Quad.localScale = m_AnimatedScale;
		m_Sprite.localScale = Collapsed;
	}
}
