using System.Collections;
using ABH.GameDatas.Interfaces;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// King DJ's back-cannon skills (AttackAndForceMoveOrder, MusicShrapnelVolley): he turns his back on the birds when
	// the skill's charge starts and stays turned through every charging turn until the attack animation is over. A skill
	// without a charge turns right before its animation. The turn is an instant flip of the rig's x scale, as elsewhere in the game, in the
	// middle of a single step (half a Move_Loop cycle, one footfall). Use Run around
	// the skill's DoAction, TurnAndPlay as its attack animation and TurnBack in ActionsOnEnd.
	public class ChargeTurnAround
	{
		// He takes one step while turning: half of his Move_Loop (a walk cycle is two footfalls), kept within these
		// bounds. The flip itself is instant, like every other turn in the game (DuelDirector, world map walkers,
		// FacingLock), halfway through the step.
		private const float MinStepTime = 0.15f;

		private const float MaxStepTime = 0.4f;

		private const string StepClip = "Move_Loop";

		private readonly SkillBattleDataBase m_Skill;

		private readonly string m_Clip;

		private CharacterAssetController m_TurnedRig;

		// Bumped whenever a new turn starts, so a turn still running stops.
		private int m_TurnVersion;

		// Bumped to stop the watcher that keeps him turned between charging turns.
		private int m_HoldVersion;

		private bool m_InAction;

		public ChargeTurnAround(SkillBattleDataBase skill, string clip)
		{
			m_Skill = skill;
			m_Clip = clip;
		}

		public IEnumerator Run(ICombatant source, IEnumerator action)
		{
			m_InAction = true;
			try
			{
				// AI combatants start their charge at the top of the turn, before the skill runs.
				if (source.ChachedSkill == m_Skill)
				{
					yield return new WaitForSeconds(BeginTurn(source));
				}
				yield return DIContainerInfrastructure.GetCoreStateMgr().StartCoroutine(action);
			}
			finally
			{
				m_InAction = false;
				if (source.ChachedSkill == m_Skill && source.IsParticipating)
				{
					// Still charging (a charge a player-picked skill started inside the action turns him here).
					BeginTurn(source);
					Hold(source, 0f);
				}
				else
				{
					// Covers aborted skills that never reach ActionsOnEnd and a defeat mid-skill; a no-op once he has
					// turned back.
					TurnBack(source.IsAlive && source.IsParticipating);
				}
			}
		}

		// Top of the AI turn, right before the charge starts: turn first, so the charge bubble and animation already
		// play with his back turned. The watcher waits out the turn, after which the charge has set ChachedSkill.
		public float BeforeChargeStart(ICombatant source)
		{
			var time = BeginTurn(source);
			Hold(source, time + 0.1f);
			return time;
		}

		// Turns (unless already turned), then plays the clip; returns the time until playDelay into the clip, or a
		// negative value when the rig can't do it (the caller falls back to its usual attack animation).
		public float TurnAndPlay(ICombatant source, float playDelay)
		{
			var asset = GetRig(source);
			if (asset == null)
			{
				return -1f;
			}
			var turnTime = BeginTurn(source);
			source.CombatantView.StartCoroutine(PlayAfter(asset, turnTime, m_TurnVersion));
			return turnTime + playDelay;
		}

		// step = false turns him back without the step (when he is defeated).
		public float TurnBack(bool step = true)
		{
			m_HoldVersion++;
			if (m_TurnedRig == null)
			{
				return 0f;
			}
			var asset = m_TurnedRig;
			m_TurnedRig = null;
			asset.StartCoroutine(Turn(asset, ++m_TurnVersion, true, step));
			return step ? StepTime(asset) : 0f;
		}

		private static float StepTime(CharacterAssetController asset)
		{
			if (asset == null || !asset.HasClipSilently(StepClip))
			{
				return 0f;
			}
			return Mathf.Clamp(asset.GetAnimationLength(StepClip) * 0.5f, MinStepTime, MaxStepTime);
		}

		private CharacterAssetController GetRig(ICombatant source)
		{
			var asset = source.CombatantView != null ? source.CombatantView.m_AssetController : null;
			return asset != null && !asset.HasNoBoneAnimation && asset.HasClipSilently(m_Clip) ? asset : null;
		}

		private float BeginTurn(ICombatant source)
		{
			var asset = GetRig(source);
			if (asset == null || m_TurnedRig == asset)
			{
				return 0f;
			}
			m_TurnedRig = asset;
			asset.StartCoroutine(Turn(asset, ++m_TurnVersion, false, true));
			return StepTime(asset);
		}

		// Between his charging turns: turns him back if the charge is lost (cleansed, replaced, ...) without firing, or
		// when he is defeated while turned (he faces his own side again as he goes down).
		private void Hold(ICombatant source, float grace)
		{
			if (m_TurnedRig != null)
			{
				m_TurnedRig.StartCoroutine(WatchCharge(source, m_TurnedRig, ++m_HoldVersion, grace));
			}
		}

		private IEnumerator WatchCharge(ICombatant source, CharacterAssetController asset, int hold, float grace)
		{
			if (grace > 0f)
			{
				yield return new WaitForSeconds(grace);
			}
			while (hold == m_HoldVersion && asset != null && m_TurnedRig == asset)
			{
				if (!source.IsAlive || !source.IsParticipating || (!m_InAction && source.ChachedSkill != m_Skill))
				{
					TurnBack(source.IsAlive && source.IsParticipating);
					yield break;
				}
				yield return null;
			}
		}

		private IEnumerator PlayAfter(CharacterAssetController asset, float delay, int version)
		{
			if (delay > 0f)
			{
				yield return new WaitForSeconds(delay);
			}
			if (asset != null && version == m_TurnVersion)
			{
				// The charge is over: stop walking in place, so the Idle after the attack is the real one.
				asset.IdleOverride = null;
				asset.PlayAnimation(m_Clip);
				asset.PlayAnimationQueued("Idle");
			}
		}

		// RigFacing keeps the facing applied every frame (the battleground's ScaleMgr resets the rig's scale each Update),
		// so he stays turned between the turn and the turn back.
		private IEnumerator Turn(CharacterAssetController asset, int version, bool back, bool step)
		{
			if (asset == null || version != m_TurnVersion)
			{
				yield break;
			}
			if (back)
			{
				asset.IdleOverride = null;
			}
			// Never over his death animation.
			step &= asset.HasClipSilently(StepClip) && !asset.IsPlayingAnimation("Defeated");
			var stepTime = step ? StepTime(asset) : 0f;
			if (step)
			{
				asset.PlayAnimation(StepClip);
				yield return new WaitForSeconds(stepTime * 0.5f);
				if (asset == null || version != m_TurnVersion)
				{
					yield break;
				}
			}
			var rigFacing = RigFacing.Get(asset.transform);
			if (back)
			{
				rigFacing.Release();
			}
			else
			{
				rigFacing.Facing = -1f;
				rigFacing.Apply();
				// While turned (charging), he walks in place instead of standing idle: every Idle (after a hit, the
				// charge bubble, ...) plays Move_Loop until the attack or the turn back.
				asset.IdleOverride = StepClip;
			}
			if (step)
			{
				yield return new WaitForSeconds(stepTime * 0.5f);
				// Back to Idle only if nothing else (the attack, the charge, his death) has taken over meanwhile.
				// (Turned, his Idle is the same walk: let it run on instead of restarting it.)
				if (asset != null && asset.IdleOverride == null && asset.IsPlayingAnimation(StepClip))
				{
					asset.PlayAnimation("Idle");
				}
			}
		}
	}
}
