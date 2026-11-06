using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using SmoothMoves;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Prince Porky's Duel: he hops straight from his slot to the front of the pigs on his target's line and charges it with
	// his regular attack (animation and dash). The charge plays in slow motion with fading after-images behind him, and
	// the blow (Red's rage dust cloud) does no damage:
	// it knocks the bird back one battleground, where the two fight on their own for "duel_turns" of Porky's turns
	// (DuelDirector runs the split battle). The AI never picks it while a duel is on, without a battleground to the left
	// of the current one, or against the last bird standing (hooks in BattleService.GetCurrentSkillFromComboAndUpdateCombo
	// and GetNextTarget).
	public class DuelKnockback : SkillBattleDataBase
	{
		// Gap between him and the frontmost pig, edge to edge.
		private const float FrontMargin = 30f;

		// From the start of his dash up to the blow (at most SlowMotionRealSeconds) the game runs at SlowMotionTimeScale.
		private const float SlowMotionRealSeconds = 1.5f;

		private const float SlowMotionTimeScale = 1f / 7f;

		private const string SlowMotionTag = "duel_charge";

		private const float AfterImageInterval = 0.3f;

		private const float AfterImageFadeTime = 0.6f;

		private const string DustCloudEffects = "HeroicStrike";

		private int m_DuelTurns;

		private VisualEffectSetting m_DustCloud;

		private bool m_AwaitingImpact;

		private bool m_Impacted;

		// The attack animation's Movement tag dashes him from his hop spot to the bird.
		private bool m_AwaitingMovement;

		private bool m_Dashed;

		private ICombatant m_DashTarget;

		// Set once he stands on his duel spot, so the end of his turn doesn't walk him anywhere.
		public bool ReturnedToBasePosition { get; private set; }

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			var turns = 3f;
			base.Model.SkillParameters.TryGetValue("duel_turns", out turns);
			m_DuelTurns = Mathf.Max(1, Mathf.RoundToInt(turns));
			VisualEffectSetting heroicStrike;
			if (DIContainerLogic.GetVisualEffectsBalancing().TryGetVisualEffectSetting(DustCloudEffects, out heroicStrike))
			{
				m_DustCloud = ImpactEffectsLeftOnTheGround(heroicStrike);
			}
		}

		public static bool IsDuelSkill(SkillGameData skill)
		{
			return skill != null && skill.Balancing.SkillTemplateType == typeof(DuelKnockback).Name;
		}

		private static DuelDirector DirectorOf(ICombatant source)
		{
			var battleMgr = source != null && source.CombatantView != null ? source.CombatantView.m_BattleMgr as BattleMgr : null;
			return battleMgr != null ? DuelDirector.For(battleMgr) : null;
		}

		public static bool CanStart(ICombatant source)
		{
			var director = DirectorOf(source);
			return director != null && director.CanStartDuel(source, null);
		}

		public static ICombatant PickTarget(ICombatant source)
		{
			var director = DirectorOf(source);
			return director != null ? director.PickTarget(source) : null;
		}

		public override void BoneAnimationUserTrigger(UserTriggerEvent triggerEvent)
		{
			if (m_AwaitingMovement && triggerEvent.tag == "Movement")
			{
				m_AwaitingMovement = false;
				Dash();
			}
			if (m_AwaitingImpact && triggerEvent.tag == "Impact")
			{
				m_Impacted = true;
			}
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger attack skill: " + base.Model.Balancing.NameId + "; Target: " + target.CombatantName);
			m_Source = source;
			m_Battle = battle;
			m_InitialTarget = target;
			ReturnedToBasePosition = false;
			var director = DirectorOf(source);
			if (director == null)
			{
				yield break;
			}
			// The target was picked at the start of the turn; pick again if it no longer works.
			var bird = director.CanStartDuel(source, target) ? target : director.PickTarget(source);
			Vector3 offset;
			if (bird == null || !director.TryGetDuelAreaOffset(out offset))
			{
				yield break;
			}
			m_Targets = new List<ICombatant> { bird };
			var view = source.CombatantView;
			var birdView = bird.CombatantView;
			var pacing = DIContainerLogic.GetPacingBalancing();

			// 1. Hop straight from his slot to the front of the pigs, on the bird's line (his turn skips the focus point).
			var line = birdView.transform.position;
			var front = new Vector3(Mathf.Min(view.transform.position.x, director.FrontOfPigs(source, FrontMargin)), line.y, line.z + view.OffsetFromEnemyZ);
			yield return view.StartCoroutine(GoadAllyIntoAttack.Hop(view, null, front - view.transform.position));

			// 2. The charge: his regular attack, dashing in from here on its Movement tag (the regular dash starts from the
			// focus point instead). The dash plays in slow motion with after-images until the blow lands.
			m_DashTarget = bird;
			m_Dashed = false;
			m_Impacted = false;
			m_AwaitingMovement = true;
			m_AwaitingImpact = true;
			var elapsed = 0f;
			var attackLength = 0f;
			AfterImageTrail trail = null;
			var slowMotionDone = false;
			var slowStart = 0f;
			try
			{
				attackLength = view.PlayAttackAnimation();
				while (!m_Impacted && elapsed < pacing.FallBackTimeWaitForSkillImpact)
				{
					if (m_Dashed && !slowMotionDone && trail == null)
					{
						if (DIContainerInfrastructure.TimeScaleMgr != null)
						{
							DIContainerInfrastructure.TimeScaleMgr.AddTimeScale(SlowMotionTag, SlowMotionTimeScale);
						}
						trail = AfterImageTrail.Begin(view.m_AssetController.transform, view.m_BattleMgr.m_BattleArea);
						trail.Interval = AfterImageInterval;
						trail.FadeTime = AfterImageFadeTime;
						slowStart = Time.unscaledTime;
					}
					if (trail != null && Time.unscaledTime - slowStart >= SlowMotionRealSeconds)
					{
						StopSlowMotion(ref trail);
						slowMotionDone = true;
					}
					elapsed += Time.deltaTime;
					yield return null;
				}
			}
			finally
			{
				StopSlowMotion(ref trail);
				m_AwaitingMovement = false;
				m_AwaitingImpact = false;
			}
			if (!m_Dashed)
			{
				// No Movement tag on this rig's attack: he still has to reach the bird.
				Dash();
				yield return new WaitForSeconds(pacing.TimeFromFocusPosToAttackPosInSec);
			}

			// 3. The blow. No damage: the bird flies back one battleground instead.
			SpawnVisualEffects(VisualEffectSpawnTiming.Impact, m_DustCloud, m_Targets);
			var knockback = director.StartCoroutine(director.Knockback(source, bird, m_DuelTurns, offset));

			// 4. Porky follows at his own pace once his swing is over (the camera stays with the bird).
			if (attackLength - elapsed > 0f)
			{
				yield return new WaitForSeconds(attackLength - elapsed);
			}
			yield return director.StartCoroutine(director.WalkToDuelSpot(source));
			yield return knockback;
			ReturnedToBasePosition = true;
		}

		// The regular attack dash (same duration), but from wherever he stands.
		private void Dash()
		{
			m_Dashed = true;
			var view = m_Source != null ? m_Source.CombatantView : null;
			var birdView = m_DashTarget != null ? m_DashTarget.CombatantView : null;
			if (view == null || birdView == null || view.m_TweenMovement == null)
			{
				return;
			}
			var asset = view.m_AssetController;
			var spot = birdView.transform.position + new Vector3(asset.OffsetFromEnemyX + birdView.m_AssetController.OffsetFromEnemyX * asset.transform.localScale.x, 0f, view.OffsetFromEnemyZ);
			var tween = view.m_TweenMovement;
			tween.m_StartTransform = view.transform;
			tween.m_StartOffset = Vector3.zero;
			tween.m_EndTransform = null;
			tween.m_EndOffset = spot - view.transform.position;
			tween.m_DurationInSeconds = DIContainerLogic.GetPacingBalancing().TimeFromFocusPosToAttackPosInSec;
			tween.Play();
		}

		private static void StopSlowMotion(ref AfterImageTrail trail)
		{
			if (DIContainerInfrastructure.TimeScaleMgr != null)
			{
				DIContainerInfrastructure.TimeScaleMgr.RemoveTimeScale(SlowMotionTag);
			}
			if (trail != null)
			{
				trail.End();
				trail = null;
			}
		}

		// Red's rage impact effects, left where the blow landed instead of riding along with the bird.
		private static VisualEffectSetting ImpactEffectsLeftOnTheGround(VisualEffectSetting setting)
		{
			if (setting == null || setting.VisualEffects == null)
			{
				return null;
			}
			var copy = new VisualEffectSetting { BalancingId = setting.BalancingId, VisualEffects = new List<VisualEffect>() };
			foreach (var effect in setting.VisualEffects.Where(e => e.SpawnTiming == VisualEffectSpawnTiming.Impact))
			{
				var effectCopy = JsonUtility.FromJson<VisualEffect>(JsonUtility.ToJson(effect));
				effectCopy.DoNotParentOnBone = true;
				copy.VisualEffects.Add(effectCopy);
			}
			return copy;
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", string.Empty + m_DuelTurns);
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
