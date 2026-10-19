using System.Linq;
using SmoothMoves;
using UnityEngine;

// A shadow that stays on the ground under a character while it is lifted and carried off (Chuck in the intro raid):
// drawn with the rig's own Shadow texture where the rig's shadow was, follows the character along x and z, and shrinks
// the higher the character is above where it started, like Wiz Pig's GroundShadow. The rig's own shadow is collapsed.
public class CarriedShadow : MonoBehaviour
{
	// The character is this high above its starting height when the shadow has shrunk away.
	private const float FullHeight = 300f;

	private Transform m_Owner;

	private Vector3 m_Offset;

	private float m_GroundY;

	private float m_StartHeight;

	public static CarriedShadow Attach(Transform owner, BoneAnimation bones)
	{
		var bone = bones != null ? bones.GetBoneTransform("Shadow") : null;
		var sprite = bone != null ? bone.Find("Shadow_Sprite") ?? bone : null;
		var atlas = bones != null ? bones.textureAtlases.FirstOrDefault(a => a != null && a.textureNames.Contains("Shadow")) : null;
		if (owner == null || sprite == null || atlas == null)
		{
			return null;
		}
		var shadow = new GameObject("CarriedShadow_" + owner.name).AddComponent<CarriedShadow>();
		shadow.m_Owner = owner;
		shadow.m_Offset = sprite.position - owner.position;
		shadow.m_GroundY = sprite.position.y;
		shadow.m_StartHeight = owner.position.y;
		shadow.transform.position = sprite.position;
		shadow.transform.rotation = sprite.rotation;
		var quad = new GameObject("Quad");
		quad.transform.SetParent(shadow.transform, false);
		quad.transform.localScale = sprite.lossyScale;
		var mesh = quad.AddComponent<CHMeshSprite>();
		mesh.m_AtlasType = AtlasTypes.SmoothMoves;
		mesh.m_SmoothMovesAtlas = atlas;
		mesh.m_SpriteName = "Shadow";
		mesh.UpdateSprite(true, true);
		UnityHelper.SetLayerRecusively(shadow.gameObject, bones.gameObject.layer);
		BoneCollapse.Add(sprite);
		return shadow;
	}

	private void LateUpdate()
	{
		if (m_Owner == null)
		{
			Destroy(gameObject);
			return;
		}
		var size = Mathf.Clamp01(1f - (m_Owner.position.y - m_StartHeight) / FullHeight);
		transform.position = new Vector3(m_Owner.position.x + m_Offset.x, m_GroundY, m_Owner.position.z + m_Offset.z);
		transform.localScale = new Vector3(size, size, 1f);
	}
}
