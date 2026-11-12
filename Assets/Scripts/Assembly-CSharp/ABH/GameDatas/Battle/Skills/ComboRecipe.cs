using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// The ability a side's combo moves add up to. One SkillBalancingData row per combination:
	//   NameId   "combo_" + the moves' combo ids (ComboMove.ComboId), sorted and joined with "_", e.g. combo_spin_spin
	//   template a ComboRecipe subclass (its Perform is the ability), TargetType Combo
	//   LocaId   <LocaId>_name is shown as "Combo: <name>" when it goes off
	// SkillParameters are the recipe's own attributes (damage_in_percent, stun_duration, ...).
	//
	// Damage rule shared by the recipes: damage_in_percent of the comboers' AVERAGE ModifiedAttack, so every comboer
	// contributes (two rogues at 200% = 100% of each rogue's attack).
	public abstract class ComboRecipe : SkillBattleDataBase
	{
		public const string NamePrefix = "combo_";

		public static string KeyFor(IEnumerable<string> comboIds)
		{
			return NamePrefix + string.Join("_", comboIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
		}

		public static bool Exists(string key)
		{
			SkillBalancingData balancing;
			return DIContainerBalancing.Service.TryGetBalancingData<SkillBalancingData>(key, out balancing) && balancing != null;
		}

		public static ComboRecipe Create(string key)
		{
			if (!Exists(key))
			{
				return null;
			}
			ComboRecipe recipe = null;
			try
			{
				recipe = new SkillGameData(key).GenerateSkillBattleData() as ComboRecipe;
			}
			catch (Exception ex)
			{
				DebugLog.Error("[ComboRecipe] Could not create " + key + ": " + ex);
			}
			if (recipe == null)
			{
				DebugLog.Error("[ComboRecipe] " + key + " exists but its SkillTemplateType is not a ComboRecipe");
			}
			return recipe;
		}

		// The ability itself. comboers are in the order they joined (initiative order) and are all able to act.
		// runner hosts any coroutines the ability starts alongside its own.
		public abstract IEnumerator Perform(BattleGameData battle, List<ICombatant> comboers, MonoBehaviour runner);

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			yield break;
		}

		protected float Param(string key, float fallback)
		{
			float value;
			return base.Model.SkillParameters != null && base.Model.SkillParameters.TryGetValue(key, out value) ? value : fallback;
		}

		protected float ComboDamage(List<ICombatant> comboers, float fallbackPercent)
		{
			var attackers = comboers.Where(c => c != null).ToList();
			if (attackers.Count == 0)
			{
				return 0f;
			}
			return attackers.Average(c => c.ModifiedAttack) * Param("damage_in_percent", fallbackPercent) / 100f;
		}

		// Through the regular damage triggers (buffs, shields, dodge, ...), with source as the attacker.
		protected static float DealDamage(BattleGameData battle, ICombatant source, ICombatant target, float baseDamage)
		{
			if (target == null || !target.IsParticipating || baseDamage <= 0f)
			{
				return 0f;
			}
			var service = DIContainerLogic.GetBattleService();
			service.ApplyEffectsOnTriggerType(1f, EffectTriggerType.BeforeDealDamage, source, source);
			service.ApplyEffectsOnTriggerType(1f, EffectTriggerType.BeforeReceiveDamage, target, source);
			var dealt = service.ApplyEffectsOnTriggerType(1f, EffectTriggerType.OnDealDamage, source, target);
			var received = service.ApplyEffectsOnTriggerType(1f, EffectTriggerType.OnReceiveDamage, target, source);
			if (received <= 0f)
			{
				return 0f;
			}
			var damage = baseDamage * dealt * received;
			target.ReceiveDamage(damage, source);
			service.DealDamageFromCurrentTurn(target, battle, source);
			service.ApplyEffectsOnTriggerType(damage, EffectTriggerType.AfterReceiveDamage, target, source);
			return damage;
		}

		// The regular stun (skips the target's next turn per point of duration).
		protected void Stun(BattleGameData battle, ICombatant source, ICombatant target, int duration)
		{
			if (target == null || !target.IsParticipating || duration <= 0)
			{
				return;
			}
			if (target.CombatantView != null)
			{
				target.CombatantView.PlayStunnedAnimation();
			}
			// Effects count down when their source's side starts its turn, so a pig stunning itself (or an ally) would
			// recover before it ever missed a turn. Same +1 as the game's own ChanceToStunSelf.
			if (source != null && source.CombatantFaction == target.CombatantFaction && target.CombatantFaction == Faction.Pigs)
			{
				duration++;
			}
			var effect = new BattleEffectGameData(source, target, new List<BattleEffect>
			{
				new BattleEffect
				{
					EffectTrigger = EffectTriggerType.BeforeStartOfTurn,
					EffectType = BattleEffectType.Stun,
					AfflicionType = SkillEffectTypes.Curse,
					Values = new List<float> { 1f },
					Duration = duration,
					EffectAssetId = "Stun",
					EffectAtlasId = "Skills_Generic"
				}
			}, duration, battle, "Stun", SkillEffectTypes.Curse, GetLocalizedName(), base.Model.SkillNameId);
			effect.AddEffect();
			effect.EffectRemovedAction = delegate(BattleEffectGameData e)
			{
				if (e.m_Target.CombatantView != null)
				{
					e.m_Target.CombatantView.PlayIdle();
				}
			};
		}

		protected static ICombatant RandomFoe(BattleGameData battle, ICombatant of)
		{
			var foes = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction != of.CombatantFaction && c.IsParticipating && c.IsAlive && !c.IsBanner && c.CombatantView != null).ToList();
			return foes.Count == 0 ? null : foes[UnityEngine.Random.Range(0, foes.Count)];
		}

		protected static bool CanStillAct(ICombatant combatant)
		{
			return combatant != null && combatant.IsParticipating && combatant.IsAlive && combatant.CombatantView != null;
		}

		// Tooltip tokens for a combo move: every recipe it appears in gives {<recipe ids>.<parameter>} (e.g.
		// {spin.stun_duration}) and, when it has damage_in_percent, {<recipe ids>.damage}: that percent of attack.
		public static void AddDescriptionValues(Dictionary<string, string> values, string comboId, float attack)
		{
			if (string.IsNullOrEmpty(comboId))
			{
				return;
			}
			IEnumerable<SkillBalancingData> rows;
			try
			{
				rows = DIContainerBalancing.Service.GetBalancingDataList<SkillBalancingData>();
			}
			catch (Exception)
			{
				return;
			}
			if (rows == null)
			{
				return;
			}
			foreach (var row in rows)
			{
				if (row == null || row.NameId == null || !row.NameId.StartsWith(NamePrefix, StringComparison.Ordinal))
				{
					continue;
				}
				var ids = row.NameId.Substring(NamePrefix.Length);
				if (!ids.Split('_').Contains(comboId) || row.SkillParameters == null)
				{
					continue;
				}
				foreach (var pair in row.SkillParameters)
				{
					values["{" + ids + "." + pair.Key + "}"] = pair.Value.ToString("0.##");
				}
				float percent;
				if (row.SkillParameters.TryGetValue("damage_in_percent", out percent))
				{
					values["{" + ids + ".damage}"] = Convert.ToInt32(attack * percent / 100f).ToString();
				}
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			if (base.Model.SkillParameters != null)
			{
				foreach (var pair in base.Model.SkillParameters)
				{
					dictionary["{" + pair.Key + "}"] = pair.Value.ToString("0.##");
				}
			}
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, new Dictionary<string, string>());
		}
	}
}
