using System.Collections.Generic;
using SmoothMoves;
using UnityEngine;

// Draws one rig sprite as two parts of its own, e.g. Chuck's mage hat while he lies knocked out by the beach: the hat
// just in front of his body as usual, its feather at a set world depth (under the waves, over the wet sand under them).
// The parts are Unity sprites cut from the sprite's texture (same size, centred, 1 pixel per rig unit) and follow the
// animated sprite; the rig's own sprite is collapsed (keeping its quad count, like SteadyShadow). Once the character
// is lifted off the ground (carried off), the rig's sprite comes back and the parts go.
[DefaultExecutionOrder(100)]
public class SplitBoneSprite : MonoBehaviour
{
	private static readonly Vector3 Collapsed = new Vector3(0.0001f, 0.0001f, 1f);

	// How much higher than where it lay the character is when it counts as lifted.
	private const float LiftedHeight = 5f;

	private class Part
	{
		public Transform Quad;

		public bool FixedDepth;

		public float Depth;
	}

	private readonly List<Part> m_Parts = new List<Part>();

	private Transform m_Sprite;

	private Transform m_Owner;

	private float m_GroundHeight;

	private Vector3 m_AnimatedScale = Vector3.one;

	public static SplitBoneSprite Split(BoneAnimation bones, string boneName, Transform owner, UnityEngine.Sprite frontPart, UnityEngine.Sprite depthPart, float depth)
	{
		var bone = bones != null ? bones.GetBoneTransform(boneName) : null;
		var sprite = bone != null ? bone.Find(boneName + "_Sprite") : null;
		if (sprite == null || frontPart == null || depthPart == null || bone.GetComponent<SplitBoneSprite>() != null)
		{
			return null;
		}
		var split = bone.gameObject.AddComponent<SplitBoneSprite>();
		split.m_Sprite = sprite;
		split.m_Owner = owner;
		split.m_GroundHeight = owner.position.y;
		split.m_AnimatedScale = sprite.localScale;
		split.AddPart(bones, frontPart, false, -0.5f);
		split.AddPart(bones, depthPart, true, depth);
		split.LateUpdate();
		return split;
	}

	private void AddPart(BoneAnimation bones, UnityEngine.Sprite image, bool fixedDepth, float depth)
	{
		var quad = new GameObject("Split_" + image.name);
		quad.transform.SetParent(transform, false);
		quad.AddComponent<SpriteRenderer>().sprite = image;
		quad.layer = bones.gameObject.layer;
		m_Parts.Add(new Part { Quad = quad.transform, FixedDepth = fixedDepth, Depth = depth });
	}

	private void LateUpdate()
	{
		if (m_Sprite == null || m_Owner == null)
		{
			return;
		}
		if (m_Sprite.localScale != Collapsed)
		{
			m_AnimatedScale = m_Sprite.localScale;
		}
		if (m_Owner.position.y > m_GroundHeight + LiftedHeight)
		{
			m_Sprite.localScale = m_AnimatedScale;
			foreach (var part in m_Parts)
			{
				Destroy(part.Quad.gameObject);
			}
			Destroy(this);
			return;
		}
		foreach (var part in m_Parts)
		{
			part.Quad.localPosition = m_Sprite.localPosition;
			part.Quad.localRotation = m_Sprite.localRotation;
			part.Quad.localScale = m_AnimatedScale;
			var z = part.FixedDepth ? part.Depth : m_Owner.position.z + part.Depth;
			part.Quad.position = new Vector3(part.Quad.position.x, part.Quad.position.y, z);
		}
		m_Sprite.localScale = Collapsed;
	}
}
