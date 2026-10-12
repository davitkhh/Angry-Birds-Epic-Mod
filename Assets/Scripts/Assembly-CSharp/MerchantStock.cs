using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;

// Merchant offers with a purchase limit that restocks as the player clears new levels.
// The starting limit is the offer's NotHaveItem requirement on a "merchant_stock_*" counter item that the
// offer grants once per purchase; every restock raises that limit by RestockAmount. A new level is the
// first clear of a world map battle hotspot, counted from when the merchant first showed the offer.
public static class MerchantStock
{
	private class Definition
	{
		public string OfferId;

		public int RestockAmount;

		public int LevelsPerRestock;
	}

	private static readonly Definition[] s_Definitions = new Definition[]
	{
		new Definition { OfferId = "offer_merchant_crates", RestockAmount = 5, LevelsPerRestock = 12 },
		new Definition { OfferId = "offer_resource_wood", RestockAmount = 10, LevelsPerRestock = 8 },
		new Definition { OfferId = "offer_gold", RestockAmount = 20, LevelsPerRestock = 10 }
	};

	private const string CounterPrefix = "merchant_stock_";

	// Stored in the player's EquippedSkins map under a "stock:" key that no class id uses, so it saves with the profile.
	private const string StateKeyPrefix = "stock:";

	private class State
	{
		// New-level count when the offer was first seen; restocks are counted from here.
		public int Baseline;

		public int Restocks;

		// Set when a restock refilled a sold-out offer; cleared once the player opens the shop.
		public bool Notice;
	}

	public static bool IsRestockable(BasicShopOfferBalancingData offer)
	{
		return GetDefinition(offer) != null;
	}

	public static bool IsCounterItem(string itemNameId)
	{
		return itemNameId != null && itemNameId.StartsWith(CounterPrefix);
	}

	// Raises the offer's NotHaveItem limit by the restocks earned so far. The list must hold copies.
	public static List<Requirement> ApplyRestocks(BasicShopOfferBalancingData offer, List<Requirement> requirements)
	{
		var definition = GetDefinition(offer);
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		if (definition == null || player == null)
		{
			return requirements;
		}
		var restocks = ReadState(player, definition).Restocks;
		foreach (var requirement in requirements.Where(r => IsLimitRequirement(r)))
		{
			requirement.Value += restocks * definition.RestockAmount;
		}
		return requirements;
	}

	public static bool TryGetRemaining(PlayerGameData player, BasicShopOfferBalancingData offer, out int remaining)
	{
		remaining = 0;
		var definition = GetDefinition(offer);
		if (definition == null || player == null)
		{
			return false;
		}
		remaining = GetRemaining(player, definition, offer);
		return true;
	}

	// Applies restocks earned since the last check for the given offers.
	public static void Sync(PlayerGameData player, IEnumerable<BasicShopOfferBalancingData> offers)
	{
		if (player == null || offers == null)
		{
			return;
		}
		var levels = CountClearedLevels(player);
		var changed = false;
		foreach (var offer in offers)
		{
			var definition = GetDefinition(offer);
			if (definition == null)
			{
				continue;
			}
			State state;
			if (!TryReadState(player, definition, out state))
			{
				WriteState(player, definition, new State { Baseline = levels });
				changed = true;
				continue;
			}
			var restocks = System.Math.Max(0, levels - state.Baseline) / definition.LevelsPerRestock;
			if (restocks <= state.Restocks)
			{
				continue;
			}
			if (GetRemaining(player, definition, offer) <= 0)
			{
				state.Notice = true;
			}
			state.Restocks = restocks;
			WriteState(player, definition, state);
			changed = true;
		}
		if (changed)
		{
			player.SavePlayerData();
		}
	}

	public static bool HasRestockNotice(PlayerGameData player, IEnumerable<BasicShopOfferBalancingData> offers)
	{
		if (player == null || offers == null)
		{
			return false;
		}
		return offers.Select(o => GetDefinition(o)).Any(d => d != null && ReadState(player, d).Notice);
	}

	public static void ClearRestockNotices(PlayerGameData player, IEnumerable<BasicShopOfferBalancingData> offers)
	{
		if (player == null || offers == null)
		{
			return;
		}
		var changed = false;
		foreach (var definition in offers.Select(o => GetDefinition(o)).Where(d => d != null))
		{
			State state;
			if (TryReadState(player, definition, out state) && state.Notice)
			{
				state.Notice = false;
				WriteState(player, definition, state);
				changed = true;
			}
		}
		if (changed)
		{
			player.SavePlayerData();
		}
	}

	private static int GetRemaining(PlayerGameData player, Definition definition, BasicShopOfferBalancingData offer)
	{
		var limit = ApplyRestocks(offer, DIContainerLogic.GetShopService().GetModifiedRequirements(offer.BuyRequirements)).FirstOrDefault(r => IsLimitRequirement(r));
		if (limit == null)
		{
			return 0;
		}
		return System.Math.Max(0, (int)limit.Value - DIContainerLogic.InventoryService.GetItemValue(player.InventoryGameData, limit.NameId));
	}

	private static bool IsLimitRequirement(Requirement requirement)
	{
		return requirement.RequirementType == RequirementType.NotHaveItem && IsCounterItem(requirement.NameId);
	}

	// Levels cleared for the first time: world map battle hotspots that have been beaten at least once.
	private static int CountClearedLevels(PlayerGameData player)
	{
		if (player.WorldGameData == null || player.WorldGameData.HotspotGameDatas == null)
		{
			return 0;
		}
		return player.WorldGameData.HotspotGameDatas.Values.Count(h => h != null && h.BalancingData != null && h.BalancingData.Type == HotspotType.Battle && h.IsCompleted());
	}

	private static Definition GetDefinition(BasicShopOfferBalancingData offer)
	{
		if (offer == null)
		{
			return null;
		}
		return s_Definitions.FirstOrDefault(d => d.OfferId == offer.NameId);
	}

	private static State ReadState(PlayerGameData player, Definition definition)
	{
		State state;
		return TryReadState(player, definition, out state) ? state : new State();
	}

	private static bool TryReadState(PlayerGameData player, Definition definition, out State state)
	{
		state = null;
		string saved;
		if (player.Data.EquippedSkins == null || !player.Data.EquippedSkins.TryGetValue(StateKeyPrefix + definition.OfferId, out saved))
		{
			return false;
		}
		var parts = saved.Split('|');
		int baseline;
		int restocks;
		if (parts.Length < 3 || !int.TryParse(parts[0], out baseline) || !int.TryParse(parts[1], out restocks))
		{
			return false;
		}
		state = new State { Baseline = baseline, Restocks = restocks, Notice = parts[2] == "1" };
		return true;
	}

	private static void WriteState(PlayerGameData player, Definition definition, State state)
	{
		if (player.Data.EquippedSkins == null)
		{
			player.Data.EquippedSkins = new Dictionary<string, string>();
		}
		player.Data.EquippedSkins[StateKeyPrefix + definition.OfferId] = state.Baseline + "|" + state.Restocks + "|" + (state.Notice ? "1" : "0");
	}
}
