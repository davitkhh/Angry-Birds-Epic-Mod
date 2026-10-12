#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.GameDatas.Battle;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using UnityEngine;

namespace ActionTreeEditor.Runtime.StateManagers
{
	public class ActionTreePreviewBattleMgr : BattleMgr
	{
		private int m_skipToWaveId = -1;

		private ExecuteBattleActionTree m_executeTree;

		public bool PlayBattleMusic = false;

		public event Action BattleInitialized;

		protected override void Awake()
		{
			m_SceneryCamera = Camera.allCameras.FirstOrDefault(c => c.CompareTag("SceneryCamera"));
			m_InterfaceCamera = Camera.allCameras.FirstOrDefault(c => !c.CompareTag("SceneryCamera"));
			m_ActionTrees = m_StorySequencesRoot.GetComponentsInChildren<ExecuteBattleActionTree>();

			
			if (DIContainerInfrastructure.GetCoreStateMgr() is not ActionTreeBattlePreviewMgr mgr)
				return;
			
			var (battleId, waveIndex, executeTree) = mgr.GetTreeInfo(m_ActionTrees);
			if (battleId == null || waveIndex == -1 || executeTree == null)
			{
				Debug.LogError("[ActionTreePreviewer - Battle] No action tree info found! Cannot play cutscene");
				return;
			}

			ClientInfo.CurrentBattleStartGameData.m_BattleBalancingNameId = battleId;
			m_skipToWaveId = waveIndex;
			m_executeTree = executeTree;

			StartCoroutine(InitializeBattle());
		}

		public IEnumerator InitializeBattle()
		{
			base.Awake();
			
			if (!PlayBattleMusic)
			{
				DIContainerInfrastructure.AudioManager.StopSound(Model.Balancing.SoundAssetId);
			}
			
			if (m_skipToWaveId != -1)
			{
				// kill all first wave combatants to skip to our new wave
				Model.m_CombatantsPerFaction[Faction.Pigs].ForEach(c => c.CurrentHealth = 0);
				DIContainerLogic.GetBattleService().ReCalculateInitiative(Model, true);
				
				var failed = DIContainerLogic.GetBattleService().HandleNextWaveOrFinishBattle(Model, m_skipToWaveId);
				if (failed)
				{
					Debug.LogError($"[ActionTreePreviewer - Battle] Failed to skip to wave index {m_skipToWaveId}!");
					yield break;
				}

				var waveSetting = GetWaveSettingForWaveIndex(m_skipToWaveId);
				m_BattleCameraTween.transform.position = waveSetting.SceneryRoot.position;
				m_BattleSetup.transform.position = waveSetting.SceneryRoot.position;
				m_BattleArea.position = m_BattleAreaPositioning.position;

				if (!m_executeTree.m_ExecuteBeforeWave)
				{
					// if executing after the wave, figure out what state the battle should be in
					// based on victory condition
					
					if (DIContainerBalancing.Service.TryGetBalancingData(Model.Balancing.BattleParticipantsIds[m_skipToWaveId], out BattleParticipantTableBalancingData balancing))
					{
						if (balancing.VictoryCondition.Type == VictoryConditionTypes.DefeatAll)
						{
							// defeat all pigs
							
							Model.m_CombatantsPerFaction[Faction.Pigs].ForEach(c => c.CurrentHealth = 0);
							DIContainerLogic.GetBattleService().ReCalculateInitiative(Model, true);
						}
						else if (balancing.VictoryCondition.Type == VictoryConditionTypes.DefeatExplicit)
						{
							// defeat Value amount of NameId pigs
							
							var pigsToKill = Model.m_CombatantsPerFaction[Faction.Pigs]
								.Where(c =>
									c.CombatantNameId.Equals(Model.m_CurrentVictoryCondition.NameId, StringComparison.OrdinalIgnoreCase) &&
									!c.CombatantNameId.Contains("_cage")
								)
								.Take((int)balancing.VictoryCondition.Value)
								.ToList();

							pigsToKill.ForEach(c => c.CurrentHealth = 0);
							
							// DON'T recalculate initiative this time
							// avoiding it means that the pigs are dead, so the ccontr won't enter
							// BUT they still exist im CombatantsByInitative so everything is placed as if they were alive and then killed
							// PLUS caged enemies will still enter
							// its prfct (niche ref)
							
							// DIContainerLogic.GetBattleService().ReCalculateInitiative(Model, true);
						}
					}
				}
			}

			RemoveHandlers();
			RegisterHandlers();
			DebugLog.Log("Battle Preview started!");
			
			yield return StartCoroutine(SetupInitialPositions());

			BattleInitialized?.Invoke();
		}

		private SceneryPerWaveSetting GetWaveSettingForWaveIndex(int index)
		{
			var remaining = index;

			foreach (var setting in m_ScenePerWaveSettings)
			{
				if (setting.RepeatCount == -1)
					return setting;

				if (remaining < setting.RepeatCount)
					return setting;

				remaining -= setting.RepeatCount;
			}

			return null;
		}

		protected override IEnumerator Start()
		{
			RemoveHandlers();
			RegisterHandlers();
			
			yield break;
			// DebugLog.Log("Battle Preview started!");
			// yield return StartCoroutine(SetupInitialPositions());
			// StartCoroutine("DoNextStep");
		}

		private void OnDestroy()
		{
			RemoveHandlers();
		}

		private void RegisterHandlers()
		{
		}

		private void RemoveHandlers()
		{
			if (m_Model == null)
			{
				return;
			}

			foreach (var item in base.Model.m_CombatantsByInitiative)
			{
				if (item.CombatantView != null)
				{
					item.CombatantView.DeregisterEventHandler();
				}
			}
		}

		public override void SpawnLootEffects(List<IInventoryItemGameData> pigDefeatedLoot, Vector3 position,
			Vector3 scale, bool useBonus)
		{
		}

		public override IEnumerator SpawnCoin(Vector3 position, Vector3 scale, float delay, bool adv)
		{
			yield break;
		}

		public override IEnumerator SpawnExp(Vector3 position, Vector3 scale, float delay, bool adv)
		{
			yield break;
		}

		public override void EnterConsumableButton()
		{
		}

		public override void LeaveConsumableButton()
		{
		}

		public override IEnumerator DoNextStep()
		{
			yield break;
		}

		private IEnumerator EnterBirds_Custom()
		{
			var duration = 0f;
			for (var i = 0; i < m_Model.m_CombatantsByInitiative.Count; i++)
			{
				var c = m_Model.m_CombatantsByInitiative[i];
				if (c.CombatantFaction != Faction.Birds || c.Entered) 
					continue;
				
				c.Entered = true;

				duration = c.CombatantView.PlayGoToBasePositionFromStartPosition();

				yield return new WaitForSeconds(DIContainerLogic.GetPacingBalancing().PerCombatantEnterDelay);
			}

			yield return new WaitForSeconds(duration);
		}

		protected override IEnumerator SetupInitialPositions()
		{
			yield return StartCoroutine(PlaceCharacter(m_BirdCenterPosition, Faction.Birds));
			yield return StartCoroutine(EnterBirds_Custom());
			
			yield return StartCoroutine(PlaceCharacter(m_PigCenterPosition, Faction.Pigs));
			
			if (base.Model.Balancing.EnvironmentalStartWave == base.Model.CurrentWaveIndex + 1)
			{
				var skillEffectId = string.Empty;
				if (base.Model.Balancing.EnvironmentalEffects != null && base.Model.Balancing.EnvironmentalEffects.TryGetValue(Faction.Birds, out skillEffectId))
				{
					var effect = new SkillGameData(skillEffectId);
					if (effect.Balancing.SkillTemplateType == "AddPrincePorky")
					{
						yield return StartCoroutine(effect.GenerateSkillBattleData().DoAction(base.Model, base.Model.m_CombatantsPerFaction[Faction.Birds][0], base.Model.m_CombatantsPerFaction[Faction.Birds][0]));
						base.Model.m_CombatantsPerFaction[Faction.Birds][0].IsAttacking = false;
					}
				}
			}
			
			yield return StartCoroutine(EnterPigs());
		}
    }
}

#endif