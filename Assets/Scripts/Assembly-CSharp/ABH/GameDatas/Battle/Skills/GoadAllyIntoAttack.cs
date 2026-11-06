using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using SmoothMoves;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Prince Porky gets mad, hops behind an ally and smacks it. The ally is surprised instead of hurt and attacks the birds
	// with its own attack for "ally_damage_in_percent" damage, animating a bit faster. Porky never picks an ally his hit
	// ("damage_in_percent" of his attack) would knock out; with no such ally the AI does not pick this skill at all
	// (hooks in BattleService.GetCurrentSkillFromComboAndUpdateCombo and GetNextTarget).
	public class GoadAllyIntoAttack : SkillBattleDataBase
	{
		// Jump is 8 frames at 24 fps: the body leaves the ground on frame 1 and is back down on frame 7.
		private const string HopAnimation = "Jump";

		private const float HopFps = 24f;

		private const float HopTakeOffFrame = 1f;

		private const float HopLandFrame = 7f;

		// Time in the air, whatever the distance. He only travels during these frames.
		private const float HopAirTime = 0.3f;

		// Jump starts and ends on a squashed Body (1.2 x 0.8) while the clips around it are at full size, so it is
		// faded in from and out to Idle over this long instead of popping between the two.
		private const float HopBlendTime = 0.1f;

		// The goaded ally's animations play this much faster.
		private const float AllyAnimationSpeed = 1.25f;

		private float m_PorkyDamagePercent;

		private float m_AllyDamagePercent;

		private bool m_AwaitingImpact;

		private bool m_Impacted;

		// Set once he has hopped back to his base position, so the end of his turn doesn't walk him there again.
		public bool ReturnedToBasePosition { get; private set; }

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			m_PorkyDamagePercent = GetPorkyDamagePercent(model);
			if (!base.Model.SkillParameters.TryGetValue("ally_damage_in_percent", out m_AllyDamagePercent))
			{
				m_AllyDamagePercent = 200f;
			}
		}

		public static bool IsGoadSkill(SkillGameData skill)
		{
			return skill != null && skill.Balancing.SkillTemplateType == typeof(GoadAllyIntoAttack).Name;
		}

		// A random ally that survives Porky's hit and can attack right away, or null.
		public static ICombatant PickAlly(BattleGameData battle, ICombatant source, SkillGameData skill)
		{
			var allies = battle.m_CombatantsByInitiative.Where(c => IsValidAlly(battle, source, c, skill)).ToList();
			return allies.Count == 0 ? null : allies[Random.Range(0, allies.Count)];
		}

		private static bool IsValidAlly(BattleGameData battle, ICombatant source, ICombatant ally, SkillGameData skill)
		{
			if (ally == null || ally == source || ally.CombatantFaction != source.CombatantFaction || !ally.IsParticipating || !ally.IsAlive || ally.IsStunned || ally.IsBanner || ally.CombatantView == null || ally.ChachedSkill != null)
			{
				return false;
			}
			var attack = ally.GetSkill(0) as AttackSkillTemplate;
			if (attack == null || attack.Model.Balancing.TargetType != SkillTargetTypes.Attack || attack.Model.SkillParameters.ContainsKey("charge") || attack is AttackAndSelfDestruct || !Ammo.CanAfford(ally, attack))
			{
				return false;
			}
			if (!battle.m_CombatantsByInitiative.Any(c => c.CombatantFaction != ally.CombatantFaction && c.IsParticipating))
			{
				return false;
			}
			return ally.CurrentHealth > PorkyDamage(source, GetPorkyDamagePercent(skill));
		}

		private static float GetPorkyDamagePercent(SkillGameData skill)
		{
			float percent;
			return skill.SkillParameters != null && skill.SkillParameters.TryGetValue("damage_in_percent", out percent) ? percent : 100f;
		}

		private static float PorkyDamage(ICombatant source, float percent)
		{
			return source.ModifiedAttack * percent / 100f;
		}

		public override void BoneAnimationUserTrigger(UserTriggerEvent triggerEvent)
		{
			// The Rage clip has its own Impact tag, so only the attack's counts.
			if (m_AwaitingImpact && triggerEvent.tag == "Impact")
			{
				m_Impacted = true;
			}
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger support skill: " + base.Model.Balancing.NameId + "; Target: " + target.CombatantName);
			m_Source = source;
			m_Battle = battle;
			ReturnedToBasePosition = false;
			m_InitialTarget = target;
			var view = source.CombatantView;
			var pacing = DIContainerLogic.GetPacingBalancing();
			// The target was picked at the start of the turn; pick again if that ally no longer qualifies.
			var ally = IsValidAlly(battle, source, target, base.Model) ? target : PickAlly(battle, source, base.Model);

			// 1. Porky gets mad.
			yield return new WaitForSeconds(view.PlayRageAnimation());
			if (ally == null)
			{
				yield break;
			}
			m_Targets = new List<ICombatant> { ally };
			var allyView = ally.CombatantView;

			// 2. He hops behind the ally (the side away from the birds), at the z offset regular melee attacks use.
			var behind = new Vector3(view.m_AssetController.OffsetFromEnemyX + allyView.m_AssetController.OffsetFromEnemyX * Mathf.Abs(view.m_AssetController.transform.localScale.x), 0f, view.OffsetFromEnemyZ);
			yield return view.StartCoroutine(Hop(view, allyView.transform, behind));

			// 3. And smacks it. The ally is surprised instead of hurt, and the hit never knocks it out.
			m_Impacted = false;
			m_AwaitingImpact = true;
			var attackLength = view.PlayAttackAnimation();
			var elapsed = 0f;
			while (!m_Impacted && elapsed < pacing.FallBackTimeWaitForSkillImpact)
			{
				elapsed += Time.deltaTime;
				yield return null;
			}
			m_AwaitingImpact = false;
			var service = DIContainerLogic.GetBattleService();
			var damage = Mathf.Min(PorkyDamage(source, m_PorkyDamagePercent), ally.CurrentHealth - 1f);
			ally.OverrideHitAnimation = true;
			if (damage > 0f)
			{
				ally.ReceiveDamage(damage, source);
				service.DealDamageFromCurrentTurn(ally, battle, source, true);
			}
			ally.OverrideHitAnimation = false;
			SpawnVisualEffects(VisualEffectSpawnTiming.Impact, m_VisualEffectSetting, m_Targets);
			var surprisedLength = allyView.PlaySurprisedAnimation();

			// 4. Porky hops back to his spot once his swing is over, while the ally attacks.
			var hopBack = view.StartCoroutine(HopBackAfter(view, attackLength - elapsed));
			yield return new WaitForSeconds(surprisedLength);
			if (ally.IsParticipating && !view.m_BattleMgr.IsBattleEnded())
			{
				yield return view.StartCoroutine(AllyAttack(battle, ally));
			}
			yield return hopBack;
			ReturnedToBasePosition = true;
		}

		private IEnumerator AllyAttack(BattleGameData battle, ICombatant ally)
		{
			var attack = ally.GetSkill(0);
			var service = DIContainerLogic.GetBattleService();
			if (!service.GetNextTarget(battle, ally, attack.Model, ally.CombatantFaction))
			{
				yield break;
			}
			var allyView = ally.CombatantView;
			var boneAnimation = allyView.m_AssetController.m_BoneAnimation;
			boneAnimation.UnregisterUserTriggerDelegate(attack.BoneAnimationUserTrigger);
			boneAnimation.RegisterUserTriggerDelegate(attack.BoneAnimationUserTrigger);
			var cachedDamageModifier = ally.DamageModifier;
			ally.DamageModifier = cachedDamageModifier * m_AllyDamagePercent / 100f;
			var attackTemplate = attack as AttackSkillTemplate;
			var cachedSpeeds = SpeedUpAnimations(allyView, AllyAnimationSpeed);
			if (attackTemplate != null)
			{
				attackTemplate.AttackAnimationSpeed = AllyAnimationSpeed;
			}
			Ammo.OnSkillStarted(ally, attack);
			try
			{
				// Runs on Porky's view: the ally's could be destroyed if it gets knocked out mid-attack.
				yield return m_Source.CombatantView.StartCoroutine(attack.DoAction(battle, ally, ally.AttackTarget));
			}
			finally
			{
				Ammo.OnSkillFinished(ally, attack);
				ally.DamageModifier = cachedDamageModifier;
				if (attackTemplate != null)
				{
					attackTemplate.AttackAnimationSpeed = 1f;
				}
				RestoreAnimationSpeeds(cachedSpeeds);
				if (boneAnimation != null)
				{
					boneAnimation.UnregisterUserTriggerDelegate(attack.BoneAnimationUserTrigger);
				}
			}
			yield return new WaitForSeconds(DIContainerLogic.GetPacingBalancing().TimeForShowDamageAndReturnToBasePosInSec);
			if (ally.IsParticipating && allyView != null)
			{
				yield return new WaitForSeconds(allyView.PlayGoToBasePosition());
			}
		}

		private IEnumerator HopBackAfter(CharacterControllerBattleGroundBase view, float delay)
		{
			if (delay > 0f)
			{
				yield return new WaitForSeconds(delay);
			}
			yield return view.StartCoroutine(Hop(view, view.m_BattleMgr.m_PigCenterPosition, view.CachedUnfocusedPos));
		}

		// Plays Jump and moves him while he is in the air (also Porky's hop before his Duel charge).
		internal static IEnumerator Hop(CharacterControllerBattleGroundBase view, Transform endTransform, Vector3 endOffset)
		{
			var assetController = view.m_AssetController;
			var state = assetController.m_BoneAnimation != null ? assetController.m_BoneAnimation[HopAnimation] : null;
			if (state == null)
			{
				yield return new WaitForSeconds(view.PlayGoToPosition(endOffset, DIContainerLogic.GetPacingBalancing().TimeFromFocusPosToAttackPosInSec, true, endTransform));
				yield break;
			}
			var bones = assetController.m_BoneAnimation;
			var originalSpeed = state.speed;
			var originalWrapMode = state.wrapMode;
			// Hold the last (squashed) frame so it can be faded out of instead of the clip just ending.
			state.wrapMode = WrapMode.ClampForever;
			bones.CrossFade(HopAnimation, HopBlendTime);
			yield return new WaitForSeconds(HopTakeOffFrame / HopFps);
			// The airborne frames play over HopAirTime, and he moves over exactly that window.
			state.speed = originalSpeed * (HopLandFrame - HopTakeOffFrame) / HopFps / HopAirTime;
			var tween = view.m_TweenMovement;
			tween.m_StartTransform = view.transform;
			tween.m_StartOffset = Vector3.zero;
			tween.m_EndTransform = endTransform;
			tween.m_EndOffset = endOffset;
			tween.m_DurationInSeconds = HopAirTime;
			tween.Play();
			yield return new WaitForSeconds(HopAirTime);
			state.speed = originalSpeed;
			yield return new WaitForSeconds(Mathf.Max(0f, assetController.GetAnimationLength(HopAnimation) - HopLandFrame / HopFps));
			bones.CrossFade("Idle", HopBlendTime);
			yield return new WaitForSeconds(HopBlendTime);
			state.wrapMode = originalWrapMode;
		}

		// Raw Unity states run at the clip's fps (see SmoothMoves BoneAnimation.Awake), so scale them rather than set them.
		private static Dictionary<AnimationState, float> SpeedUpAnimations(CharacterControllerBattleGroundBase view, float factor)
		{
			var speeds = new Dictionary<AnimationState, float>();
			var boneAnimation = view.m_AssetController.m_BoneAnimation;
			var animation = boneAnimation != null && !view.m_AssetController.HasNoBoneAnimation ? boneAnimation.GetComponent<Animation>() : null;
			if (animation == null)
			{
				return speeds;
			}
			foreach (AnimationState state in animation)
			{
				speeds[state] = state.speed;
				state.speed *= factor;
			}
			return speeds;
		}

		private static void RestoreAnimationSpeeds(Dictionary<AnimationState, float> speeds)
		{
			foreach (var pair in speeds)
			{
				if (pair.Key != null)
				{
					pair.Key.speed = pair.Value;
				}
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", m_AllyDamagePercent.ToString("0"));
			dictionary.Add("{value_2}", m_PorkyDamagePercent.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
