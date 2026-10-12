#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ABH.GameDatas;
using ABH.GameDatas.Battle;
using ABH.Shared.BalancingData;
using SmoothMoves;
using UnityEngine;

namespace ActionTreeEditor.Runtime.StateManagers
{
	// mostly taken from OnlyBattleStateMgr
	public class ActionTreeBattlePreviewMgr : ActionTreePreviewMgr
	{
		public int BirdsLevel = 1;

		public List<string> m_BirdBalancingNameIds;

		public string m_BattlegroundNameId = "DefaultBattleground";

		public string m_DefaultInventoryNameId = "player_inventory";

		public InventoryView AdditionalPlayerItems;

		private bool m_balancingDataInitialized;

		public List<string> m_PvPBirdBalancingNameIds = new List<string>();

		protected override void Awake()
		{
			AdditionalPlayerItems = gameObject.AddComponent<InventoryView>();
			
			SceneLoadingMgr.AddUILevel("LoadingScreen", DeleteUISceneHelpers);
			SceneLoadingMgr.AddUILevel("DisplayElements", DeleteUISceneHelpers);
			
			base.Awake();
			
			InitAndEnterBattle();
		}

		protected override IEnumerator Start()
		{
			yield return base.Start();
		}

		private void InitAndEnterBattle()
		{
			var battleStartGameData = new BattleStartGameData();
			battleStartGameData.callback = OnBattleDone;
			battleStartGameData.m_BackgroundAssetId = m_BattlegroundNameId;
			battleStartGameData.m_InvokerLevel = BirdsLevel;
			battleStartGameData.m_BattleRandomSeed = UnityEngine.Random.Range(1, int.MaxValue);
			battleStartGameData.m_Inventory = new InventoryGameData(m_DefaultInventoryNameId);
			DIContainerLogic.GetLootOperationService().RewardLoot(battleStartGameData.m_Inventory, 0, DIContainerLogic.GetLootOperationService().GenerateLoot(battleStartGameData.m_Inventory.BalancingData.DefaultInventoryContent, 1), "DebugLoot");
			AdditionalPlayerItems.AddInventoryItemsToInventory(battleStartGameData.m_Inventory);
			DIContainerInfrastructure.GetCurrentPlayer().Data.Level = BirdsLevel;
			var list = new List<BirdGameData>();
			foreach (var birdBalancingNameId in m_BirdBalancingNameIds)
			{
				BirdBalancingData balancing = null;
				if (DIContainerBalancing.Service.TryGetBalancingData(birdBalancingNameId, out balancing))
				{
					DebugLog.Log("Success: " + birdBalancingNameId);
					var birdGameData = new BirdGameData(balancing.NameId);
					birdGameData.Data.Level = BirdsLevel;
					list.Add(birdGameData);
				}
			}

			var list2 = new List<BirdGameData>();
			foreach (var pvPBirdBalancingNameId in m_PvPBirdBalancingNameIds)
			{
				BirdBalancingData balancing2 = null;
				if (DIContainerBalancing.Service.TryGetBalancingData(pvPBirdBalancingNameId, out balancing2))
				{
					DebugLog.Log("Success: " + pvPBirdBalancingNameId);
					var birdGameData2 = new BirdGameData(balancing2.NameId);
					birdGameData2.Data.Level = BirdsLevel;
					list2.Add(birdGameData2);
				}
			}

			battleStartGameData.m_Birds = list;
			battleStartGameData.m_PvPBirds = list2;
			battleStartGameData.m_BirdBanner = new BannerGameData("bird_banner");
			battleStartGameData.m_PigBanner = new BannerGameData("bird_banner");
			ClientInfo.CurrentBattleStartGameData = battleStartGameData;
		}

		public override void GotoWorldMap()
		{
		}

		public void OnBattleDone(IAsyncResult result)
		{
			DebugLog.Log("Battle callback received!");
			if (result != null)
			{
				DebugLog.Log("Battle Async Result != null!");
				var battleEndGameData = DIContainerLogic.GetBattleService().EndBattle(result);
				DebugLog.Log("Battle EndData is null: " + (battleEndGameData == null));
				InitAndEnterBattle();
			}
		}

		public override void GotoCampScreen()
		{
			base.SceneLoadingMgr.LoadGameScene("Arena", new List<string> { "Menu_Arena" });
		}

		public override void GotoDefaultBattle()
		{
			base.SceneLoadingMgr.LoadGameScene("DefaultBattleground",
				new List<string> { "Menu_Battleground", "Popup_BattlePaused" });
		}

		public void EndBattle()
		{
			if (ClientInfo.CurrentBattleGameData != null)
			{
				DIContainerLogic.GetBattleService().RegisterBattleEnded(ClientInfo.CurrentBattleGameData);
				ClientInfo.CurrentBattleGameData = null;
			}
		}

		public override void ReturnFromBattle()
		{
			EndBattle();
		}

		public override ITutorialMgr InstantiateNullTutorialMgr()
		{
			if (m_TutorialNullMgr == null)
				m_TutorialNullMgr = gameObject.AddComponent<TutorialMgrNullImpl>();
			
			var tutorialMgrNullImpl = UnityEngine.Object.Instantiate(m_TutorialNullMgr);
			UnityEngine.Object.DontDestroyOnLoad(tutorialMgrNullImpl);
			return tutorialMgrNullImpl;
		}

		public override ITutorialMgr InstantiateTutorialMgr()
		{
			if (m_TutorialNullMgr == null)
				m_TutorialNullMgr = gameObject.AddComponent<TutorialMgrNullImpl>();
			
			var tutorialMgrNullImpl = UnityEngine.Object.Instantiate(m_TutorialNullMgr);
			UnityEngine.Object.DontDestroyOnLoad(tutorialMgrNullImpl);
			return tutorialMgrNullImpl;
		}

		private static FieldInfo EBATActionTreeField =
			typeof(ExecuteBattleActionTree).GetField("m_ActionTree", BindingFlags.NonPublic | BindingFlags.Instance);
		private static FieldInfo EBATBattleIdField =
			typeof(ExecuteBattleActionTree).GetField("m_BattleID", BindingFlags.NonPublic | BindingFlags.Instance);
		private static FieldInfo EBATWaveIndexField =
			typeof(ExecuteBattleActionTree).GetField("m_WaveIndex", BindingFlags.NonPublic | BindingFlags.Instance);
		
		public (string NameId, int Wave, ExecuteBattleActionTree Instance) GetTreeInfo(ExecuteBattleActionTree[] actionTrees)
		{
			if (EBATActionTreeField == null || EBATBattleIdField == null || EBATWaveIndexField == null)
				return (null, -1, null);
			
			foreach (var executeTree in actionTrees)
			{
				var tree = EBATActionTreeField.GetValue(executeTree) as ActionTree;
				if (tree?.name != m_actionTree.name) 
					continue;
				
				var battleId = (string)EBATBattleIdField.GetValue(executeTree);
				var waveIndex = (int)EBATWaveIndexField.GetValue(executeTree);
				return (battleId, waveIndex, executeTree);
			}

			return (null, -1, null);
		}
	}
}

#endif