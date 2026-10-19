using System.Collections.Generic;
using SmoothMoves;
using UnityEngine;

// Shrinks a pig's shadow while the pig is off the ground (hops in attacks, supports, hits). The shadow keeps its
// animated shape, but its size is capped by how high the rig's Bottom bone is above its rest height: full size (plus
// some room for take-off and landing stretches) on the ground, smaller the higher the pig goes. Clips that already
// shrink the shadow themselves while the pig is up (the brute's attack) are left entirely to their animation, and so
// are shadows a clip hides on purpose (Wiz Pig's FlyingState, the bomb's explosion).
[DefaultExecutionOrder(90)]
public class AirborneShadow : MonoBehaviour
{
	// Rig units of height at which the cap would reach zero; the brute's own attack keys 0.7 at 210 units up.
	private const float ShrinkHeight = 650f;

	private const float MinSize = 0.35f;

	// Animated stretches up to this much over the rest size are kept on the ground.
	private const float StretchAllowance = 1.15f;

	// Below this (relative to rest), the clip is hiding the shadow; leave it hidden.
	private const float HiddenBelow = 0.1f;

	// A clip shrinks the shadow itself if, at some frame with the pig at least this high, the shadow is under
	// OwnShrinkBelow of its rest size on both axes (ground squashes keep one axis at full size or more).
	private const float OwnShrinkHeight = 30f;

	private const float OwnShrinkBelow = 0.95f;

	// Per clip: does it shrink the shadow itself while the pig is up? Sampled once, the first time a rig plays it.
	private static readonly Dictionary<AnimationClip, bool> s_ShrinksOwnShadow = new Dictionary<AnimationClip, bool>();

	private Animation m_Animation;

	private Transform m_Sprite;

	private Transform m_Bottom;

	private Transform m_Space;

	private float m_RestBottomY;

	private Vector3 m_RestScale;

	// What the animation last wrote and what this wrote over it. A sprite still holding what this wrote was not
	// animated this frame (the clip has no curve for it), so the animation's last value stands.
	private Vector3 m_AnimatedScale;

	private Vector3 m_WrittenScale;

	private bool m_HasWritten;

	public static AirborneShadow Attach(CharacterAssetController asset)
	{
		return asset == null ? null : Attach(asset.m_BoneAnimation);
	}

	public static AirborneShadow Attach(BoneAnimation bones)
	{
		if (bones == null || bones.GetComponent<AirborneShadow>() != null)
		{
			return null;
		}
		var shadow = bones.GetBoneTransform("Shadow");
		var bottom = bones.GetBoneTransform("Bottom");
		var sprite = shadow != null ? shadow.Find("Shadow_Sprite") : null;
		var animation = bones.GetComponent<Animation>();
		if (sprite == null || bottom == null || shadow.parent == null || animation == null)
		{
			return null;
		}
		var airborne = bones.gameObject.AddComponent<AirborneShadow>();
		airborne.m_Animation = animation;
		airborne.m_Sprite = sprite;
		airborne.m_Bottom = bottom;
		airborne.m_Space = shadow.parent;
		// A rig attached here is fresh from its prefab, in its rest pose.
		airborne.m_RestBottomY = airborne.Height();
		var rest = sprite.localScale;
		airborne.m_RestScale = Mathf.Abs(rest.x) < 0.5f || Mathf.Abs(rest.y) < 0.5f ? Vector3.one : new Vector3(Mathf.Abs(rest.x), Mathf.Abs(rest.y), 1f);
		return airborne;
	}

	private float Height()
	{
		return m_Space.InverseTransformPoint(m_Bottom.position).y - m_RestBottomY;
	}

	private float Size(Vector3 scale)
	{
		return Mathf.Max(Mathf.Abs(scale.x) / m_RestScale.x, Mathf.Abs(scale.y) / m_RestScale.y);
	}

	private bool PlayingClipShrinksShadow()
	{
		foreach (AnimationState state in m_Animation)
		{
			if (state.enabled && state.weight > 0f && state.clip != null && ShrinksOwnShadow(state.clip))
			{
				return true;
			}
		}
		return false;
	}

	private bool ShrinksOwnShadow(AnimationClip clip)
	{
		bool shrinks;
		if (s_ShrinksOwnShadow.TryGetValue(clip, out shrinks))
		{
			return shrinks;
		}
		// Sample the clip frame by frame on this rig, then put every transform back as it was.
		var transforms = GetComponentsInChildren<Transform>(true);
		var poses = new Vector3[transforms.Length * 2];
		var rotations = new Quaternion[transforms.Length];
		for (var i = 0; i < transforms.Length; i++)
		{
			poses[i * 2] = transforms[i].localPosition;
			poses[i * 2 + 1] = transforms[i].localScale;
			rotations[i] = transforms[i].localRotation;
		}
		var step = 1f / Mathf.Max(1f, clip.frameRate);
		for (var time = 0f; time <= clip.length + 0.0001f && !shrinks; time += step)
		{
			clip.SampleAnimation(gameObject, time);
			var scale = m_Sprite.localScale;
			shrinks = Height() >= OwnShrinkHeight && Mathf.Abs(scale.x) / m_RestScale.x < OwnShrinkBelow && Mathf.Abs(scale.y) / m_RestScale.y < OwnShrinkBelow;
		}
		for (var i = 0; i < transforms.Length; i++)
		{
			transforms[i].localPosition = poses[i * 2];
			transforms[i].localScale = poses[i * 2 + 1];
			transforms[i].localRotation = rotations[i];
		}
		s_ShrinksOwnShadow[clip] = shrinks;
		return shrinks;
	}

	private void LateUpdate()
	{
		if (m_Sprite == null || m_Bottom == null || m_Space == null)
		{
			return;
		}
		var scale = m_Sprite.localScale;
		if (m_HasWritten && scale == m_WrittenScale)
		{
			scale = m_AnimatedScale;
		}
		m_AnimatedScale = scale;
		var size = Size(scale);
		var cap = StretchAllowance * Mathf.Max(MinSize, 1f - Mathf.Max(0f, Height()) / ShrinkHeight);
		var hidden = Mathf.Min(Mathf.Abs(scale.x) / m_RestScale.x, Mathf.Abs(scale.y) / m_RestScale.y) < HiddenBelow;
		if (size > cap && !hidden && !PlayingClipShrinksShadow())
		{
			var shrink = cap / size;
			scale = new Vector3(scale.x * shrink, scale.y * shrink, scale.z);
		}
		m_WrittenScale = scale;
		m_Sprite.localScale = scale;
		m_HasWritten = true;
	}
}
