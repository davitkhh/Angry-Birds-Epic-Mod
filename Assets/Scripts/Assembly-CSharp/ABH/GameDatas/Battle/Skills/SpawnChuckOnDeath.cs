using System.Collections;
using System.Linq;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

namespace ABH.GameDatas.Battle.Skills
{
	public class SpawnChuckOnDeath : SkillBattleDataBase
	{
		private const string ChuckBirdNameId = "bird_yellow";

		private const string ChuckRageSkillNameId = "bird_rage_yellowbird";

		private bool m_ChuckSpawned;

		public override IEnumerator DoAction(BattleGameData battle, ICombatant source, ICombatant target, bool shared = false, bool illusion = false)
		{
			DoActionInstant(battle, source, target);
			yield break;
		}

		public override void DoActionInstant(BattleGameData battle, ICombatant source, ICombatant target)
		{
			m_Battle = battle;
			m_Source = source;
			source.Defeated -= OnSourceDefeated;
			source.Defeated += OnSourceDefeated;
		}

		private void OnSourceDefeated()
		{
			m_Source.Defeated -= OnSourceDefeated;
			if (m_ChuckSpawned)
			{
				return;
			}
			m_ChuckSpawned = true;
			m_Source.CombatantView.m_BattleMgr.StartCoroutine(PlayAttentionThenSpawnChuck());
		}

		private IEnumerator PlayAttentionThenSpawnChuck()
		{
			yield return new WaitForSeconds(m_Source.CombatantView.PlayAttentionAnimation());
			yield return m_Source.CombatantView.m_BattleMgr.StartCoroutine(SpawnChuckAndUseRage());
		}

		private IEnumerator SpawnChuckAndUseRage()
		{
			var battleMgr = m_Source.CombatantView.m_BattleMgr;
			var chuck = new BirdCombatant(new BirdGameData(ChuckBirdNameId, m_Battle.m_CombatantsPerFaction[Faction.Birds][0].CharacterModel.Level));
			m_Battle.m_CombatantsPerFaction[Faction.Birds].Add(chuck);
			// BattleMgr's per-turn gate (BattleMgr.cs ~1013) polls every frame for any Bird with
			// ActedThisTurn == false; chuck bypasses the normal StartCombatantTurn dispatch that
			// would otherwise flip this, so it must be set here or that gate spins forever.
			chuck.ActedThisTurn = true;
			DIContainerLogic.GetBattleService().ReCalculateInitiative(m_Battle);
			yield return battleMgr.StartCoroutine(battleMgr.PlaceCharacter(battleMgr.m_BirdCenterPosition, Faction.Birds));
			battleMgr.StartCoroutine(battleMgr.EnterBirds());
			chuck.CombatantView.SpawnHealthBar();

			var rageSkill = chuck.GetSkill(ChuckRageSkillNameId) as RageYellowBird;
			var rageTarget = m_Battle.m_CombatantsPerFaction[Faction.Birds].FirstOrDefault(b => b.IsAlive);
			if (rageSkill != null && rageTarget != null)
			{
				rageSkill.SkipCastAnimation = true;
				yield return chuck.CombatantView.StartCoroutine(rageSkill.DoAction(m_Battle, chuck, rageTarget));
			}
		}

		public override string GetLocalizedDescription(ICombatant invoker)
		{
			return string.Empty;
		}

		public override string GetLocalizedName()
		{
			return string.Empty;
		}
	}
}
