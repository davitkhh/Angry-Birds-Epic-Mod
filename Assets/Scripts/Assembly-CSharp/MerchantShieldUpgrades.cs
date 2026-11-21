using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.Shared.BalancingData;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;
using ABH.Shared.Models.InventoryItems;

// Piggy McCool's two shield upgrades, sold side by side in one merchant slot (ShopOfferBlindWorldmap pair layout).
// While McCool runs his stall each half is a "blueprintless" upgrade: buying it raises a merchant_stock_* counter, and every
// count adds the half's upgrade blueprint effect (a Bulwark bonus perk, +10 to Red's support "percent") to every copy of
// that shield (UpgradeBlueprints.GetBonusPerks). Each half can be bought MaxPurchases times, the second at double the price
// (one offer per purchase number). Once McCool has left, the purchases still in stock are sold as one upgrade blueprint
// per half (20 snoutlings) with that many uses and the cobalt costs they would have had: the full blueprint (uses 1 and
// 2) or the "_last" one (only use 2's cost).
public static class MerchantShieldUpgrades
{
	public const int MaxPurchases = 2;

	public class Half
	{
		// Shield drawn on the half
		public string ShieldNameId;

		// Effect + blueprint with MaxPurchases uses; its definition's targets decide which shield the direct purchases boost
		public string BlueprintId;

		// Counts the direct purchases (merchant_stock_ prefix: story items above 1 are allowed)
		public string CounterItem;

		// Index = purchases made so far
		public string[] DirectOffers;

		// Index = uses left - 1
		public string[] BlueprintOffers;
	}

	public static readonly Half[] Halves = new Half[]
	{
		new Half
		{
			ShieldNameId = "bird_offhand_red_shield_start",
			BlueprintId = "upgrade_offhand_red_shield_start_protect",
			CounterItem = "merchant_stock_upgrade_shield_start",
			DirectOffers = new[] { "offer_merchant_upgrade_shield_start_1", "offer_merchant_upgrade_shield_start_2" },
			BlueprintOffers = new[] { "offer_merchant_blueprint_shield_start_1", "offer_merchant_blueprint_shield_start_2" }
		},
		new Half
		{
			ShieldNameId = "bird_offhand_red_shield_introwheel",
			BlueprintId = "upgrade_offhand_red_shield_introwheel_protect",
			CounterItem = "merchant_stock_upgrade_shield_introwheel",
			DirectOffers = new[] { "offer_merchant_upgrade_shield_introwheel_1", "offer_merchant_upgrade_shield_introwheel_2" },
			BlueprintOffers = new[] { "offer_merchant_blueprint_shield_introwheel_1", "offer_merchant_blueprint_shield_introwheel_2" }
		}
	};

	public static bool IsPairOffer(BasicShopOfferBalancingData offer)
	{
		return offer != null && IsPairOffer(offer.NameId);
	}

	public static bool IsPairOffer(string offerNameId)
	{
		return Halves.Any(h => h.DirectOffers.Contains(offerNameId) || h.BlueprintOffers.Contains(offerNameId));
	}

	public static int GetDirectCount(InventoryGameData inventory, Half half)
	{
		return inventory == null ? 0 : DIContainerLogic.InventoryService.GetItemValue(inventory, half.CounterItem);
	}

	public static bool IsBlueprintOffer(Half half, BasicShopOfferBalancingData offer)
	{
		return offer != null && half.BlueprintOffers.Contains(offer.NameId);
	}

	// The offer the half shows now. soldOut: nothing left to buy (the offer is then only shown as sold out).
	public static BasicShopOfferBalancingData GetCurrentOffer(PlayerGameData player, Half half, out bool soldOut)
	{
		soldOut = true;
		if (player == null)
		{
			return null;
		}
		var bought = System.Math.Min(MaxPurchases, GetDirectCount(player.InventoryGameData, half));
		string offerId;
		if (!McCoolDeparture.HasLeft(player))
		{
			offerId = half.DirectOffers[System.Math.Min(bought, MaxPurchases - 1)];
		}
		else
		{
			var left = MaxPurchases - bought;
			offerId = left > 0 ? half.BlueprintOffers[left - 1] : half.DirectOffers[MaxPurchases - 1];
		}
		BuyableShopOfferBalancingData offer;
		if (!DIContainerBalancing.Service.TryGetBalancingData(offerId, out offer))
		{
			return null;
		}
		soldOut = !DIContainerLogic.GetShopService().IsOfferBuyableIgnoreCost(player, offer);
		return offer;
	}

	// The shop list holds one offer per slot; the pair slot stands for both halves' current offers.
	public static List<BasicShopOfferBalancingData> ExpandPairs(PlayerGameData player, List<BasicShopOfferBalancingData> offers)
	{
		if (offers == null || !offers.Any(IsPairOffer))
		{
			return offers;
		}
		var result = new List<BasicShopOfferBalancingData>();
		foreach (var offer in offers)
		{
			if (!IsPairOffer(offer))
			{
				result.Add(offer);
				continue;
			}
			foreach (var half in Halves)
			{
				bool soldOut;
				var current = GetCurrentOffer(player, half, out soldOut);
				if (current != null)
				{
					result.Add(current);
				}
			}
		}
		return result;
	}

	// Upgrade definitions bought directly, with how often, for the support/rage/stat bonus sums.
	public static IEnumerable<KeyValuePair<UpgradeBlueprintBalancingData, int>> GetDirectUpgrades(InventoryGameData inventory)
	{
		foreach (var half in Halves)
		{
			var count = System.Math.Min(MaxPurchases, GetDirectCount(inventory, half));
			UpgradeBlueprintBalancingData definition;
			if (count > 0 && UpgradeBlueprints.TryGetDefinition(half.BlueprintId, out definition))
			{
				yield return new KeyValuePair<UpgradeBlueprintBalancingData, int>(definition, count);
			}
		}
	}

	// Upgrade definitions bought directly for this equipment (by NameId, so every copy of the shield has them), with counts.
	// Read from the current player's inventory.
	public static IEnumerable<KeyValuePair<UpgradeBlueprintBalancingData, int>> GetDirectUpgradesFor(string equipmentNameId)
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		EquipmentBalancingData balancing;
		if (player == null || string.IsNullOrEmpty(equipmentNameId) || !Halves.Any(h => h.ShieldNameId == equipmentNameId) || !DIContainerBalancing.Service.TryGetBalancingData(equipmentNameId, out balancing))
		{
			yield break;
		}
		foreach (var pair in GetDirectUpgrades(player.InventoryGameData))
		{
			if (UpgradeBlueprints.Matches(pair.Key, balancing))
			{
				yield return pair;
			}
		}
	}

	// Part of EquipmentGameData's perk cache key, so a purchase refreshes the shield's bonus perks.
	public static string GetPerkSignature(EquipmentData data)
	{
		if (data == null || !Halves.Any(h => h.ShieldNameId == data.NameId))
		{
			return string.Empty;
		}
		return "#" + string.Join(",", GetDirectUpgradesFor(data.NameId).Select(p => p.Key.NameId + ":" + p.Value).ToArray());
	}

	// The bonus perk one purchase of the half adds (for the shop tile's perk row).
	public static EquipmentPerk GetPerkPerPurchase(Half half)
	{
		UpgradeBlueprintBalancingData definition;
		PerkType type;
		if (!UpgradeBlueprints.TryGetDefinition(half.BlueprintId, out definition) || string.IsNullOrEmpty(definition.AddPerkType) || !System.Enum.IsDefined(typeof(PerkType), definition.AddPerkType))
		{
			return null;
		}
		type = (PerkType)System.Enum.Parse(typeof(PerkType), definition.AddPerkType);
		return new EquipmentPerk { Type = type, ProbablityInPercent = definition.AddPerkChance > 0f ? definition.AddPerkChance : 100f, PerkValue = definition.AddPerkValue, PerkValue2 = definition.AddPerkValue2 };
	}
}
