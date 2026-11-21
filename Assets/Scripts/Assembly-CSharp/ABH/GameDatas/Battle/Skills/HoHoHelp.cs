using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	// Santa Pig's "Ho ho help!": after "charge" turns he summons "summon_<table>" pigs from that
	// BattleParticipantTable (e.g. summon_elf: 2 Santa's Little Helpers), then plays Repeat After Me with Helpers as
	// the repeaters: one of his other moves at "effectiveness_in_percent", then every Helper he just summoned plus up
	// to "helper_count" other Helpers already on his side each play one of their own moves of the same kind at that
	// strength. Helpers are pigs whose NameId starts with <prefix> when a "helper_prefix_<prefix>" key is set,
	// otherwise pig_minion_elf. Being a RepeatAfterMe, the moves it plays are scaled, charge-free copies and it is
	// never repeated itself.
	public class HoHoHelp : RepeatAfterMe
	{
		private const string DefaultHelperPrefix = "pig_minion_elf";

		private float m_ChargeTurns;

		private float m_SummonedAmount;

		private string m_TableKey;

		private float m_OtherHelperCount;

		private string m_HelperPrefix = DefaultHelperPrefix;

		private List<ICombatant> m_Summoned = new List<ICombatant>();

		public override void Init(SkillGameData model)
		{
			base.Init(model);
			base.Model.SkillParameters.TryGetValue("charge", out m_ChargeTurns);
			if (!base.Model.SkillParameters.TryGetValue("effectiveness_in_percent", out m_EffectivenessPercent))
			{
				m_EffectivenessPercent = 70f;
			}
			if (!base.Model.SkillParameters.TryGetValue("helper_count", out m_OtherHelperCount))
			{
				m_OtherHelperCount = 2f;
			}
			m_TableKey = base.Model.SkillParameters.Keys.FirstOrDefault(k => k.StartsWith("summon_"));
			if (m_TableKey != null)
			{
				base.Model.SkillParameters.TryGetValue(m_TableKey, out m_SummonedAmount);
			}
			var prefixKey = base.Model.SkillParameters.Keys.FirstOrDefault(k => k.StartsWith("helper_prefix_"));
			if (prefixKey != null)
			{
				m_HelperPrefix = prefixKey.Substring("helper_prefix_".Length);
			}
		}

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DebugLog.Log("Trigger support skill: " + base.Model.Balancing.NameId + "; Target: " + (target != null ? target.CombatantName : "none"));
			m_Source = source;
			m_Battle = battle;
			m_InitialTarget = target;
			m_Targets = new List<ICombatant>();
			m_Summoned = new List<ICombatant>();
			ReturnedToBasePosition = false;
			if (EvaluateCharge(battle, source, m_Targets, target))
			{
				m_Source.CombatantView.targetSheltered = null;
				yield break;
			}
			var view = source.CombatantView;
			yield return view.StartCoroutine(SummonHelpers(battle, source));
			if (!source.IsParticipating || view == null || view.m_BattleMgr.IsBattleEnded())
			{
				yield break;
			}
			yield return view.StartCoroutine(PlayRepeatSequence(battle, source));
		}

		// Same summon as SummonCombatant (weighted table, pig limit, enter, passives, health bars). Nothing to summon
		// (table missing or the pig limit reached) just skips to the move.
		private IEnumerator SummonHelpers(BattleGameData battle, ICombatant source)
		{
			if (m_TableKey == null || m_SummonedAmount <= 0f)
			{
				yield break;
			}
			var waveBalancing = DIContainerBalancing.Service.GetBalancingData<BattleParticipantTableBalancingData>(m_TableKey);
			if (waveBalancing == null)
			{
				DebugLog.Log("[HoHoHelp] no BattleParticipantTable " + m_TableKey);
				yield break;
			}
			yield return new WaitForSeconds(source.CombatantView.PlaySupportAnimation());
			var piggies = battle.m_CombatantsByInitiative.Where(c => c.CombatantFaction == source.CombatantFaction).ToList();
			piggies.AddRange(battle.m_CombatantsPerFaction[Faction.Pigs].Where(p => p.IsKnockedOut));
			var maxPigsInBattle = DIContainerBalancing.GameConstantsBalancingDataProvider.MaxPigsInBattle;
			var summonedList = DIContainerLogic.GetBattleService().GenerateSummonsWeighted(piggies, waveBalancing, battle, (int)m_SummonedAmount, maxPigsInBattle);
			if (summonedList.Count == 0)
			{
				yield break;
			}
			foreach (var summon in summonedList)
			{
				if (!battle.m_CombatantsPerFaction.ContainsKey(summon.CombatantFaction))
				{
					battle.m_CombatantsPerFaction.Add(summon.CombatantFaction, new List<ICombatant>());
				}
				summon.CurrentInitiative = source.CurrentInitiative + 1;
				battle.m_CombatantsPerFaction[summon.CombatantFaction].Add(summon);
				summon.HasUsageDelay = true;
				summon.summoningType = SummoningType.Summoned;
			}
			var battleMgr = source.CombatantView.m_BattleMgr;
			yield return battleMgr.StartCoroutine(battleMgr.PlaceCharacter(battleMgr.m_PigCenterPosition, Faction.Pigs));
			yield return battleMgr.StartCoroutine(battleMgr.EnterPigs());
			foreach (var summon in summonedList)
			{
				var pig = summon as PigCombatant;
				if (pig != null && pig.PassiveSkill != null)
				{
					pig.CombatantView.StartCoroutine(pig.PassiveSkill.DoAction(battle, pig, pig));
					pig.IsAttacking = false;
				}
			}
			DIContainerLogic.GetBattleService().AddPassiveEffects(battle);
			battleMgr.SpawnHealthBars();
			m_Summoned = summonedList;
			DebugLog.Log("[HoHoHelp] " + source.CombatantNameId + " summoned " + summonedList.Count + " from " + m_TableKey);
		}

		// Every freshly summoned Helper that can repeat, then up to helper_count other Helpers, in random order.
		protected override List<ICombatant> PickRepeaters(BattleGameData battle, ICombatant source, SkillTargetTypes kind)
		{
			var fresh = m_Summoned.Where(c => IsReadyRepeater(source, c) && RepeatableMoves(c, kind).Count > 0).ToList();
			var others = battle.m_CombatantsByInitiative.Where(c => !m_Summoned.Contains(c) && IsReadyRepeater(source, c) && RepeatableMoves(c, kind).Count > 0).OrderBy(c => Random.value).Take(Mathf.Max(0, (int)m_OtherHelperCount));
			return fresh.Concat(others).ToList();
		}

		protected override bool IsReadyRepeater(ICombatant source, ICombatant c)
		{
			return IsReadyAlly(source, c, m_HelperPrefix);
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_1}", m_EffectivenessPercent.ToString("0"));
			dictionary.Add("{value_2}", m_ChargeTurns.ToString("0"));
			dictionary.Add("{value_3}", m_SummonedAmount.ToString("0"));
			dictionary.Add("{value_4}", m_OtherHelperCount.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillDescriptions(base.Model.SkillDescription, dictionary);
		}

		public override string GetLocalizedName()
		{
			var dictionary = new Dictionary<string, string>();
			dictionary.Add("{value_2}", m_ChargeTurns.ToString("0"));
			return DIContainerInfrastructure.GetLocaService().GetSkillName(base.Model.SkillDescription, dictionary);
		}
	}
}
