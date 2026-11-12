using System;
using System.Collections;
using System.Collections.Generic;
using SmoothMoves;
using UnityEngine;

public class AssetControllerWithBoneAnimationBase : MonoBehaviour
{
	public BoneAnimation m_BoneAnimation;

	public bool HasNoBoneAnimation { get; private set; }

	// Hurt state (set by the battle view from HP): while hurt, a request for clip X plays the rig's X_Hurt instead
	// whenever the rig has one. Names match ignoring case (Chuck's attack is "Action_attack_1_Hurt").
	public const string HurtSuffix = "_Hurt";

	public const string HitToHurtClip = "Hit_to_Hurt";

	private bool m_Hurt;

	private Dictionary<string, string> m_ClipNames;

	private Dictionary<string, string> m_HurtVariants;

	public bool IsHurt
	{
		get { return m_Hurt; }
	}

	private string m_IdleOverride;

	// While set (and the rig has the clip), every request for "Idle" plays this clip instead, e.g. King DJ walks in place
	// (Move_Loop) while charging (ChargeTurnAround). An Idle on screen is swapped right away, both ways.
	public string IdleOverride
	{
		get { return m_IdleOverride; }
		set
		{
			if (value == m_IdleOverride)
			{
				return;
			}
			var wasIdle = !HasNoBoneAnimation && m_BoneAnimation != null && IsPlayingAnimation("Idle");
			m_IdleOverride = value;
			if (wasIdle)
			{
				PlayAnimation("Idle");
			}
		}
	}

	// Flips the hurt state; a base Idle/Blink (or its hurt twin) currently on screen is swapped to the new state's clip.
	public virtual void SetHurt(bool hurt)
	{
		if (m_Hurt == hurt)
		{
			return;
		}
		var wasIdle = !HasNoBoneAnimation && m_BoneAnimation != null && IsPlayingAnimation("Idle");
		m_Hurt = hurt;
		if (wasIdle && HasHurtVariant("Idle"))
		{
			PlayAnimation("Idle");
		}
	}

	// The rig's real name for a clip, matched ignoring case; null when the rig doesn't have it.
	public string FindClipName(string animationName)
	{
		if (string.IsNullOrEmpty(animationName) || HasNoBoneAnimation || m_BoneAnimation == null)
		{
			return null;
		}
		CacheClipNames();
		string name;
		return m_ClipNames.TryGetValue(animationName.ToLowerInvariant(), out name) ? name : null;
	}

	public bool HasHurtVariant(string animationName)
	{
		if (string.IsNullOrEmpty(animationName) || HasNoBoneAnimation || m_BoneAnimation == null)
		{
			return false;
		}
		CacheClipNames();
		return m_HurtVariants.ContainsKey(animationName.ToLowerInvariant());
	}

	// The clip that actually plays for a request: X_Hurt while hurt (if the rig has it), else X unchanged.
	protected string ResolveClip(string animationName)
	{
		if (m_IdleOverride != null && string.Equals(animationName, "Idle", StringComparison.OrdinalIgnoreCase) && FindClipName(m_IdleOverride) != null)
		{
			animationName = FindClipName(m_IdleOverride);
		}
		if (!m_Hurt || string.IsNullOrEmpty(animationName) || HasNoBoneAnimation || m_BoneAnimation == null)
		{
			return animationName;
		}
		CacheClipNames();
		string hurt;
		return m_HurtVariants.TryGetValue(animationName.ToLowerInvariant(), out hurt) ? hurt : animationName;
	}

	private void CacheClipNames()
	{
		if (m_ClipNames != null)
		{
			return;
		}
		m_ClipNames = new Dictionary<string, string>();
		m_HurtVariants = new Dictionary<string, string>();
		// The legacy Animation lookup is silent; SmoothMoves' clip indexer logs an error for unknown names.
		var clips = m_BoneAnimation.GetComponent<Animation>();
		if (clips == null)
		{
			return;
		}
		foreach (AnimationState state in clips)
		{
			if (state != null && !string.IsNullOrEmpty(state.name))
			{
				m_ClipNames[state.name.ToLowerInvariant()] = state.name;
			}
		}
		foreach (var name in m_ClipNames.Values)
		{
			if (name.Length > HurtSuffix.Length && name.EndsWith(HurtSuffix, StringComparison.OrdinalIgnoreCase))
			{
				m_HurtVariants[name.Substring(0, name.Length - HurtSuffix.Length).ToLowerInvariant()] = name;
			}
		}
	}

	private void Awake()
	{
		if (!m_BoneAnimation)
		{
			HasNoBoneAnimation = true;
			m_BoneAnimation = base.gameObject.AddComponent<ProxyBoneAnimation>();
		}
	}

	public IEnumerator WaitThenPlayAnimation(string animationNameToStop, string animationName, float waitTime)
	{
		yield return new WaitForSeconds(waitTime);
		if (HasNoBoneAnimation)
		{
			base.gameObject.PlayAnimationOrAnimatorState(animationName);
			yield break;
		}
		StopAnimation(animationNameToStop);
		PlayAnimation(animationName);
	}

	public IEnumerator PlayAnimationAndWaitForFinish(string animationName)
	{
		if (HasNoBoneAnimation)
		{
			yield return new WaitForSeconds(base.gameObject.PlayAnimationOrAnimatorState(animationName));
			yield break;
		}
		PlayAnimation(animationName);
		yield return new WaitForSeconds(GetAnimationLength(animationName));
	}

	public void PlayAnimationForTime(string animationName, string animationToContinue, float time)
	{
		if (HasNoBoneAnimation)
		{
			base.gameObject.PlayAnimationOrAnimatorState(animationName);
			StartCoroutine(WaitThenPlayAnimation(animationName, animationToContinue, time));
		}
		else
		{
			PlayAnimation(animationName);
			StartCoroutine(WaitThenPlayAnimation(animationName, animationToContinue, time));
		}
	}

	public void PlayAnimationsQueued(List<string> animationNames)
	{
		if (HasNoBoneAnimation)
		{
			gameObject.PlayAnimationOrAnimatorStateQueued(animationNames, this);
		}
		else
		{
			foreach (var requested in animationNames)
			{
				var animName = ResolveClip(requested);
				if (m_BoneAnimation != null && m_BoneAnimation[animName] != null)
				{
					m_BoneAnimation.PlayQueued(animName);
				}
			}
		}
	}
	
	public void PlayAnimationQueued(string animationName)
	{
		animationName = ResolveClip(animationName);
		try
		{
			if (HasNoBoneAnimation)
			{
				base.gameObject.PlayAnimationOrAnimatorStateQueued(new List<string> { animationName }, this);
			}
			else if (m_BoneAnimation != null && m_BoneAnimation[animationName] != null)
			{
				m_BoneAnimation.PlayQueued(animationName);
			}
			else
			{
				DebugLog.Error("Animation " + animationName + " not found!");
			}
		}
		catch (ArgumentOutOfRangeException)
		{
			DebugLog.Error("Animation " + animationName + " not found!");
		}
	}

	protected void StopAnimation(string animationName)
	{
		try
		{
			if (HasNoBoneAnimation)
			{
				base.gameObject.StopAnimationOrAnimatorState(animationName);
			}
			else if (m_BoneAnimation != null && m_BoneAnimation[animationName] != null)
			{
				m_BoneAnimation.Stop(animationName);
				var hurt = FindClipName(animationName + HurtSuffix);
				if (hurt != null)
				{
					m_BoneAnimation.Stop(hurt);
				}
			}
			else
			{
				DebugLog.Error("Animation " + animationName + " not found!");
			}
		}
		catch (ArgumentOutOfRangeException)
		{
			DebugLog.Error("Animation " + animationName + " not found!");
		}
	}

	public bool IsPlayingAnimation(string animationName)
	{
		if (HasNoBoneAnimation)
		{
			return base.gameObject.IsAnimationOrAnimatorPlaying(animationName);
		}
		if (m_BoneAnimation != null)
		{
			// Either variant counts: a clip queued before the hurt state flipped is still "the" Idle.
			if (m_BoneAnimation.IsPlaying(animationName))
			{
				return true;
			}
			// The override stands in for Idle (Idle itself still counts, e.g. one queued before it was set).
			if (m_IdleOverride != null && string.Equals(animationName, "Idle", StringComparison.OrdinalIgnoreCase))
			{
				var stand = FindClipName(m_IdleOverride);
				if (stand != null && IsPlayingAnimation(stand))
				{
					return true;
				}
			}
			var hurt = FindClipName(animationName + HurtSuffix);
			return hurt != null && m_BoneAnimation.IsPlaying(hurt);
		}
		return false;
	}

	public bool HasAnimation(string animationName)
	{
		if (HasNoBoneAnimation)
		{
			return base.gameObject.GetAnimationOrAnimatorStateLength(animationName) > 0f;
		}
		if (m_BoneAnimation != null)
		{
			return m_BoneAnimation[animationName] != null;
		}
		return false;
	}

	public void PlayAnimation(string animationName)
	{
		animationName = ResolveClip(animationName);
		try
		{
			if (HasNoBoneAnimation)
			{
				base.gameObject.PlayAnimationOrAnimatorState(animationName);
			}
			else if (m_BoneAnimation != null && m_BoneAnimation[animationName] != null)
			{
				m_BoneAnimation.Play(animationName);
			}
			else
			{
				DebugLog.Error("Animation " + animationName + " not found!");
			}
		}
		catch (ArgumentOutOfRangeException)
		{
			DebugLog.Error("Animation " + animationName + " not found!");
		}
	}

	public float GetAnimationLength(string animationName)
	{
		animationName = ResolveClip(animationName);
		try
		{
			if (HasNoBoneAnimation)
			{
				return base.gameObject.GetAnimationOrAnimatorStateLength(animationName);
			}
			var result = 0f;
			if (m_BoneAnimation != null && m_BoneAnimation[animationName] != null)
			{
				result = m_BoneAnimation[animationName].length;
			}
			return result;
		}
		catch (ArgumentOutOfRangeException)
		{
			DebugLog.Error("Animation " + animationName + " not found!");
			return 0f;
		}
	}
}
