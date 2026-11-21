using System;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.Shared.Generic;
using UnityEngine;

public class ResourceNodeManager : MonoBehaviour, IResourceNodeManager
{
	public float m_UpdateTime;

	private List<HotSpotWorldMapViewResource> m_spotList = new List<HotSpotWorldMapViewResource>();

	private List<DateTime> m_timeStampList = new List<DateTime>();

	private int m_amountResourcesPossible;

	private int m_currentAmount;

	public void ClearSpotList()
	{
		m_spotList.Clear();
	}

	public void AddResourceSpot(HotSpotWorldMapViewResource spot)
	{
		m_UpdateTime = DIContainerBalancing.GameConstantsBalancingDataProvider.TimeForResourceRespawn;
		if (!m_spotList.Contains(spot))
		{
			m_spotList.Add(spot);
		}
	}

	public void RemoveResourceSpot(HotSpotWorldMapViewResource spot)
	{
		if (m_spotList.Contains(spot))
		{
			m_spotList.Remove(spot);
		}
	}

	public void SpawnResource(GameObject go, Vector3 offset)
	{
		var component = go.GetComponent<HotSpotWorldMapViewResource>();
		var loot = DIContainerLogic.GetLootOperationService().GenerateLoot(component.Model.BalancingData.HotspotContents, DIContainerInfrastructure.GetCurrentPlayer().Data.Level);
		var list = DIContainerLogic.GetLootOperationService().RewardLoot(DIContainerInfrastructure.GetCurrentPlayer().InventoryGameData, 0, loot, new Dictionary<string, string>
		{
			{ "TypeOfUse", "resource_node" },
			{
				"tax_01",
				component.Model.BalancingData.NameId
			}
		});
		for (var i = 0; i < list.Count; i++)
		{
			var inventoryItemGameData = list[i];
			var craftingItemGameData = inventoryItemGameData as CraftingItemGameData;
			var battleLootVisualization = UnityEngine.Object.Instantiate(component.m_ResourcePrefab);
			var componentInChildren = battleLootVisualization.GetComponentInChildren<CHMeshSprite>();
			if (craftingItemGameData.BalancingData.ItemType == InventoryItemType.Ingredients)
			{
				if (DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().ContainsAsset("Ingredients"))
				{
					var gameObject = DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().GetObject("Ingredients") as GameObject;
					componentInChildren.m_NguiAtlas = gameObject.GetComponent<UIAtlas>();
				}
			}
			else if (craftingItemGameData.BalancingData.ItemType == InventoryItemType.Resources && DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().ContainsAsset("Resources"))
			{
				var gameObject2 = DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().GetObject("Resources") as GameObject;
				componentInChildren.m_NguiAtlas = gameObject2.GetComponent<UIAtlas>();
			}
			componentInChildren.m_SpriteName = craftingItemGameData.BalancingData.AssetBaseId;
			componentInChildren.UpdateSprite(true);
			battleLootVisualization.transform.position = go.transform.position + offset;
			// A node shown in a shop menu (HotSpotWorldMapViewResource.ShowInMenu) is on the interface layer, above the menu.
			if (go.layer != battleLootVisualization.gameObject.layer)
			{
				var nodeRenderer = go.GetComponentInChildren<Renderer>();
				foreach (var t in battleLootVisualization.GetComponentsInChildren<Transform>(true))
				{
					t.gameObject.layer = go.layer;
				}
				foreach (var r in battleLootVisualization.GetComponentsInChildren<Renderer>(true))
				{
					r.sortingOrder = nodeRenderer != null ? nodeRenderer.sortingOrder + 1 : 0;
				}
			}
			UnityEngine.Object.Destroy(battleLootVisualization.gameObject, battleLootVisualization.SpawnDelay + battleLootVisualization.MoveTime * 2f);
		}
		DebugLog.Log("Spawn Resource");
		NodeHarvested(component.Model);
	}

	// The unlock state (Active while it has supply, Resolved once empty) is kept by ResourceNodeSupply.Consume.
	public void NodeHarvested(HotspotGameData hotspot)
	{
		DIContainerLogic.GetTimingService().GetTrustedTimeEx(delegate(DateTime trustedTime)
		{
			hotspot.Data.LastVisitDateTime = trustedTime;
			if (DIContainerInfrastructure.GetCurrentPlayer() != null)
			{
				DIContainerInfrastructure.GetCurrentPlayer().SavePlayerData();
			}
		});
	}

	private int GetCurrentMaxResources(int currentSpotAmount)
	{
		return DIContainerBalancing.GameConstantsBalancingDataProvider.MaximumSpawnableNodes - currentSpotAmount;
	}

	// Called on entering the world map and every minute after (WorldMapStateMgr.UpdateResourceNodeManager). Nodes no
	// longer respawn on a timer: each unlocked node gets the refills the stars earned since its last one paid for
	// (ResourceNodeSupply). The old TimeForResourceRespawn / MaximumSpawnableNodes constants are unused.
	public void CheckGlobalCoolDown()
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		if (player == null)
		{
			return;
		}
		var changed = false;
		foreach (var spot in m_spotList.ToList())
		{
			if (spot == null || spot.Model == null)
			{
				continue;
			}
			changed |= spot.ApplyStarRefills();
		}
		if (changed)
		{
			player.SavePlayerData();
		}
	}

	private void SpawnNewNode()
	{
		if (m_spotList.Count == 0)
		{
			DebugLog.Error("spotlist is 0");
			return;
		}
		var num = 0;
		do
		{
			var index = UnityEngine.Random.Range(0, m_spotList.Count);
			if (!m_spotList[index].IsResourceAvaible())
			{
				DebugLog.Log(m_spotList[index].gameObject.name + " respawn ");
				m_spotList[index].Respawn();
				if (DIContainerInfrastructure.GetCurrentPlayer() != null)
				{
					DIContainerInfrastructure.GetCurrentPlayer().SavePlayerData();
				}
				break;
			}
			num++;
		}
		while (num < 1000);
	}
}

// Star-driven resource node supply. Counting starts when a node unlocks: from then on, every StarsPerRefill(type) stars the
// player earns (WorldMapService.GetTotalAccumulatedStars: world map battles + chronicle cave, better replays included)
// adds one full supply (ResourceSpawnAmountPerNode harvests). An emptied node fills back up; a node that still has
// supply left gets the full amount on top. Supply persists between visits (one tap = one harvest).
// Saved per node in the player's EquippedSkins map as "resnode:<hotspot nameId>" = "<star baseline>|<supply>".
public static class ResourceNodeSupply
{
	public const string KeyPrefix = "resnode:";

	// Stars per refill, per material type of node (the node's AnimationPrefix without the "_": Rock, TreeStump, Seashell).
	// GameConstants "ResourceNodeStarsPerRefill_<type>" (FloatValue): -1 (any negative) or missing = use the shared
	// "ResourceNodeStarsPerRefill"; 0 = needs no stars (the node refills the moment it is emptied, so it never runs out).
	// The shared value works the same way, with negative/missing falling back to DefaultStarsPerRefill.
	// Balancing values apply once the .bytes is re-encoded.
	public const string StarsPerRefillConstant = "ResourceNodeStarsPerRefill";

	public const int DefaultStarsPerRefill = 3;

	public static int StarsPerRefill(string nodeType)
	{
		var provider = DIContainerBalancing.GameConstantsBalancingDataProvider;
		if (!string.IsNullOrEmpty(nodeType))
		{
			var typed = provider.GetBalancingData(StarsPerRefillConstant + "_" + nodeType);
			if (typed != null && typed.FloatValue >= 0f)
			{
				return (int)Math.Round(typed.FloatValue);
			}
		}
		var shared = provider.GetBalancingData(StarsPerRefillConstant);
		return shared != null && shared.FloatValue >= 0f ? (int)Math.Round(shared.FloatValue) : DefaultStarsPerRefill;
	}

	public static int FullSupply
	{
		get { return Math.Max(1, DIContainerBalancing.GameConstantsBalancingDataProvider.ResourceSpawnAmountPerNode); }
	}

	public static bool IsUnlocked(HotspotGameData hotspot)
	{
		return hotspot != null && hotspot.Data.UnlockState > HotspotUnlockState.Hidden;
	}

	// Current supply after paying out any refills earned. The first call for an unlocked node starts its star count:
	// a node that is up for harvest (just unlocked, or an Active one from an older save) starts full, an emptied one at 0.
	// Keeps UnlockState in step (Active with supply, Resolved without). Does not save.
	public static int Refresh(PlayerGameData player, HotspotGameData hotspot, string nodeType)
	{
		if (player == null || !IsUnlocked(hotspot))
		{
			return 0;
		}
		var stars = DIContainerLogic.WorldMapService.GetTotalAccumulatedStars(player);
		int baseline;
		int supply;
		if (!TryRead(player, hotspot, out baseline, out supply))
		{
			baseline = stars;
			supply = hotspot.Data.UnlockState == HotspotUnlockState.Resolved ? 0 : FullSupply;
		}
		var perRefill = StarsPerRefill(nodeType);
		if (perRefill <= 0)
		{
			// Needs no stars: an empty node is full again right away.
			baseline = stars;
			if (supply <= 0)
			{
				supply = FullSupply;
			}
		}
		else if (stars > baseline)
		{
			var refills = (stars - baseline) / perRefill;
			baseline += refills * perRefill;
			supply += refills * FullSupply;
		}
		Write(player, hotspot, baseline, supply);
		hotspot.Data.UnlockState = supply > 0 ? HotspotUnlockState.Active : HotspotUnlockState.Resolved;
		return supply;
	}

	// Takes one harvest; returns what is left. Saves.
	public static int Consume(PlayerGameData player, HotspotGameData hotspot, string nodeType)
	{
		var supply = Math.Max(0, Refresh(player, hotspot, nodeType) - 1);
		if (supply <= 0 && StarsPerRefill(nodeType) <= 0)
		{
			supply = FullSupply;
		}
		int baseline;
		int unused;
		TryRead(player, hotspot, out baseline, out unused);
		Write(player, hotspot, baseline, supply);
		hotspot.Data.UnlockState = supply > 0 ? HotspotUnlockState.Active : HotspotUnlockState.Resolved;
		player.SavePlayerData();
		return supply;
	}

	// Stars still needed for the next refill (for UI / debugging).
	public static int StarsUntilNextRefill(PlayerGameData player, HotspotGameData hotspot, string nodeType)
	{
		int baseline;
		int supply;
		if (player == null || !TryRead(player, hotspot, out baseline, out supply))
		{
			return StarsPerRefill(nodeType);
		}
		return Math.Max(0, baseline + StarsPerRefill(nodeType) - DIContainerLogic.WorldMapService.GetTotalAccumulatedStars(player));
	}

	private static bool TryRead(PlayerGameData player, HotspotGameData hotspot, out int baseline, out int supply)
	{
		baseline = 0;
		supply = 0;
		string saved;
		if (player.Data.EquippedSkins == null || !player.Data.EquippedSkins.TryGetValue(KeyPrefix + hotspot.BalancingData.NameId, out saved) || saved == null)
		{
			return false;
		}
		var parts = saved.Split('|');
		return parts.Length >= 2 && int.TryParse(parts[0], out baseline) && int.TryParse(parts[1], out supply);
	}

	private static void Write(PlayerGameData player, HotspotGameData hotspot, int baseline, int supply)
	{
		if (player.Data.EquippedSkins == null)
		{
			player.Data.EquippedSkins = new Dictionary<string, string>();
		}
		player.Data.EquippedSkins[KeyPrefix + hotspot.BalancingData.NameId] = baseline + "|" + Math.Max(0, supply);
	}
}
