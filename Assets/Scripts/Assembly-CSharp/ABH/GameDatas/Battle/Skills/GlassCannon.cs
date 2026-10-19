using System.Collections;
using System.Collections.Generic;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;

namespace ABH.GameDatas.Battle.Skills
{
	// Set skill of Piggy McCool's Dynamite + Pyrobolist powder keg (CustomEquipmentSets):
	// - every charge of his skills is "charge_reduction" turns shorter, never below 1 (BirdCombatant.GetSkill);
	// - his equipment adds no health: off hand stat, Vitality perks and item upgrades' health % (BirdGameData);
	// - he takes "damage_taken_in_percent" more damage, and goes off with a small explosion whenever he is hurt.
	public class GlassCannon : SkillBattleDataBase
	{
		private float m_ChargeReduction;

		private float m_DamageTakenPercent;

		private static VisualEffectSetting s_HurtExplosion;

		public float ChargeReduction
		{
			get { return m_ChargeReduction; }
		}

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			model.SkillParameters.TryGetValue("charge_reduction", out m_ChargeReduction);
			model.SkillParameters.TryGetValue("damage_taken_in_percent", out m_DamageTakenPercent);
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DoActionInstant(battle, source, target);
			yield break;
		}

		public override void DoActionInstant(BattleGameData battle, ICombatant source, ICombatant target)
		{
			m_Source = source;
			if (m_DamageTakenPercent <= 0f)
			{
				return;
			}
			new BattleEffectGameData(source, source, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.OnReceiveDamage,
					EffectType = BattleEffectType.IncreaseDamageReceived,
					Values = new List<float> { m_DamageTakenPercent },
					AfflicionType = base.Model.Balancing.EffectType,
					Duration = base.Model.Balancing.EffectDuration,
					EffectAssetId = base.Model.Balancing.EffectIconAssetId,
					EffectAtlasId = base.Model.Balancing.EffectIconAtlasId
				}
			}, base.Model.Balancing.EffectDuration, battle, base.Model.Balancing.AssetId, SkillEffectTypes.SetPassive, GetLocalizedName(), base.Model.SkillNameId).AddEffect();
		}

		// BattleService.DealDamageFromCurrentTurn: the wearer just lost health.
		public static void OnDamageTaken(BattleGameData battle, ICombatant target)
		{
			var glassCannon = target.GetSetItemSkill(battle.IsPvP) as GlassCannon;
			if (glassCannon == null || target.CombatantView == null)
			{
				return;
			}
			glassCannon.m_Source = target;
			glassCannon.SpawnVisualEffects(VisualEffectSpawnTiming.Impact, GetHurtExplosion(), new List<ICombatant> { target });
		}

		private static VisualEffectSetting GetHurtExplosion()
		{
			if (s_HurtExplosion == null)
			{
				s_HurtExplosion = new VisualEffectSetting();
				s_HurtExplosion.BalancingId = "GlassCannonHurt";
				s_HurtExplosion.VisualEffects = new List<VisualEffect>
				{
					new VisualEffect
					{
						SpawnTiming = VisualEffectSpawnTiming.Impact,
						Type = VisualEffectType.Instant,
						TargetCombatant = VisualEffectTargetCombatant.Target,
						TargetAnchor = VisualEffectTargetAnchor.Center,
						SoundId = string.Empty,
						PrefabName = "SkillFX_Hit_Single_Ranged_Explosive",
						OnHitPrefix = string.Empty,
						EffectTextIdent = string.Empty
					}
				};
			}
			return s_HurtExplosion;
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", m_ChargeReduction.ToString("0"));
			dictionary.Add("{value_2}", m_DamageTakenPercent.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
