using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ABH.GameDatas;
using ABH.GameDatas.Interfaces;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using ABH.Shared.Interfaces;
using ABH.Shared.Models.Generic;
using ABH.Shared.Models.InventoryItems;
using UnityEngine;

// Upgrade blueprints: crafting recipes (CraftingRecipeBalancingData) that have an UpgradeBlueprintBalancingData with
// the same NameId. The owned recipe's Value counts the uses left; it leaves the inventory at 0. Each "craft" spends one
// use on an owned piece of equipment, pays that use's ResourceCostPerLevel row (Level = use number), and rolls 0-3
// stars like normal crafting. Applications are saved on the equipment (EquipmentData.Upgrades) together with the
// scrap refund they are worth; scrapping the item, or only its upgrades, hands back the blueprint uses and the refund.
public static class UpgradeBlueprints
{
	public const float StarBonusPercent = 10f;

	public const string RefundReason = "upgrade_blueprint_refund";

	private const string RecipeListKeyPrefix = "upgrade:";

	public class Application
	{
		public string BlueprintId;

		public int Stars;

		public Dictionary<string, int> Refund = new Dictionary<string, int>();

		public float Multiplier
		{
			get
			{
				return 1f + Stars * StarBonusPercent / 100f;
			}
		}

		public static Application Parse(string entry)
		{
			if (string.IsNullOrEmpty(entry))
			{
				return null;
			}
			var parts = entry.Split('|');
			var application = new Application { BlueprintId = parts[0] };
			if (parts.Length > 1)
			{
				int.TryParse(parts[1], out application.Stars);
			}
			if (parts.Length > 2 && parts[2].Length > 0)
			{
				foreach (var pair in parts[2].Split(','))
				{
					var colon = pair.LastIndexOf(':');
					int amount;
					if (colon > 0 && int.TryParse(pair.Substring(colon + 1), out amount))
					{
						application.Refund[pair.Substring(0, colon)] = amount;
					}
				}
			}
			return application;
		}

		public override string ToString()
		{
			return BlueprintId + "|" + Stars + "|" + string.Join(",", Refund.Select(r => r.Key + ":" + r.Value).ToArray());
		}
	}

	// ----- Definitions and targets -----

	public static bool TryGetDefinition(string recipeNameId, out UpgradeBlueprintBalancingData definition)
	{
		definition = null;
		return !string.IsNullOrEmpty(recipeNameId) && DIContainerBalancing.Service.TryGetBalancingData(recipeNameId, out definition);
	}

	public static bool TryGetDefinition(IInventoryItemGameData item, out UpgradeBlueprintBalancingData definition)
	{
		definition = null;
		var recipe = item as CraftingRecipeGameData;
		return recipe != null && TryGetDefinition(recipe.BalancingData.NameId, out definition);
	}

	public static bool IsUpgradeBlueprint(IInventoryItemGameData item)
	{
		UpgradeBlueprintBalancingData definition;
		return TryGetDefinition(item, out definition);
	}

	// Owned recipes are grouped by result item in InventoryGameData.CraftingRecipes. An upgrade blueprint's result
	// only picks its picture, so it gets a key of its own and never shares (or removes) a normal blueprint's entry.
	public static List<string> GetRecipeListKeys(CraftingRecipeGameData recipe)
	{
		if (IsUpgradeBlueprint(recipe))
		{
			return new List<string> { RecipeListKeyPrefix + recipe.BalancingData.NameId };
		}
		return recipe.GetResultLoot().Keys.ToList();
	}

	public static bool Matches(UpgradeBlueprintBalancingData definition, IInventoryItemBalancingData item)
	{
		if (definition == null || item == null)
		{
			return false;
		}
		string birdId;
		var equipment = item as EquipmentBalancingData;
		var classItem = item as ClassItemBalancingData;
		if (equipment != null && (equipment.ItemType == InventoryItemType.MainHandEquipment || equipment.ItemType == InventoryItemType.OffHandEquipment))
		{
			birdId = equipment.RestrictedBirdId;
		}
		else if (classItem != null)
		{
			birdId = classItem.RestrictedBirdId;
		}
		else
		{
			return false;
		}
		if (definition.TargetNameIds != null && definition.TargetNameIds.Count > 0 && !definition.TargetNameIds.Contains(item.NameId))
		{
			return false;
		}
		if (definition.TargetNameIdPrefixes != null && definition.TargetNameIdPrefixes.Count > 0 && !definition.TargetNameIdPrefixes.Any(p => item.NameId.StartsWith(p, StringComparison.Ordinal)))
		{
			return false;
		}
		if (!string.IsNullOrEmpty(definition.TargetBirdId) && birdId != definition.TargetBirdId)
		{
			return false;
		}
		if (!string.IsNullOrEmpty(definition.TargetItemType) && item.ItemType.ToString() != definition.TargetItemType)
		{
			return false;
		}
		return true;
	}

	private static readonly InventoryItemType[] UpgradeableTypes = { InventoryItemType.MainHandEquipment, InventoryItemType.OffHandEquipment, InventoryItemType.Class };

	// Every player equipment or class the blueprint can go on, owned or not (shown when the blueprint is held).
	public static List<IInventoryItemBalancingData> GetSupportedItems(UpgradeBlueprintBalancingData definition)
	{
		var equipment = DIContainerBalancing.Service.GetBalancingDataList<EquipmentBalancingData>()
			.Where(e => !string.IsNullOrEmpty(e.RestrictedBirdId) && e.RestrictedBirdId.StartsWith("bird_", StringComparison.Ordinal) && !e.NameId.EndsWith("_npc", StringComparison.Ordinal) && Matches(definition, e))
			.OrderBy(e => e.RestrictedBirdId)
			.ThenBy(e => e.ItemType)
			.ThenBy(e => e.SortPriority)
			.Cast<IInventoryItemBalancingData>();
		var classes = DIContainerBalancing.Service.GetBalancingDataList<ClassItemBalancingData>()
			.Where(c => !string.IsNullOrEmpty(c.RestrictedBirdId) && c.RestrictedBirdId.StartsWith("bird_", StringComparison.Ordinal) && Matches(definition, c))
			.Cast<IInventoryItemBalancingData>();
		return classes.Concat(equipment).ToList();
	}

	public static List<IInventoryItemGameData> GetEligibleOwned(InventoryGameData inventory, UpgradeBlueprintBalancingData definition)
	{
		var result = new List<IInventoryItemGameData>();
		foreach (var type in UpgradeableTypes)
		{
			List<IInventoryItemGameData> items;
			if (!inventory.Items.TryGetValue(type, out items))
			{
				continue;
			}
			result.AddRange(items.Where(i => (i is EquipmentGameData || i is ClassItemGameData) && Matches(definition, i.ItemBalancing)));
		}
		return result;
	}

	public static string GetName(UpgradeBlueprintBalancingData definition)
	{
		return DIContainerInfrastructure.GetLocaService().Tr(definition.LocaBaseId + "_name", definition.NameId);
	}

	public static string GetDescription(UpgradeBlueprintBalancingData definition)
	{
		return DIContainerInfrastructure.GetLocaService().Tr(definition.LocaBaseId + "_desc", string.Empty);
	}

	public static string GetItemName(IInventoryItemBalancingData item)
	{
		var equipment = item as EquipmentBalancingData;
		if (equipment != null)
		{
			return EquipmentGameData.GetFinalEquipmentName(equipment, 1, EquipmentSource.Crafting, false);
		}
		return new ClassItemGameData(item.NameId).ItemLocalizedName;
	}

	public static string GetSupportedEquipmentText(UpgradeBlueprintBalancingData definition, InventoryGameData inventory)
	{
		var owned = new HashSet<string>(GetEligibleOwned(inventory, definition).Select(e => e.ItemBalancing.NameId));
		var lines = new List<string>();
		foreach (var item in GetSupportedItems(definition))
		{
			var name = GetItemName(item);
			lines.Add(owned.Contains(item.NameId) ? "[00a800]" + name + "[-]" : name);
		}
		var header = DIContainerInfrastructure.GetLocaService().Tr("upgrade_blueprint_supported", "Can upgrade:");
		return header + "\n" + string.Join("\n", lines.ToArray());
	}

	// ----- Uses and costs -----

	public static int GetUsesLeft(InventoryGameData inventory, string blueprintId)
	{
		return DIContainerLogic.InventoryService.GetItemValue(inventory, blueprintId);
	}

	public static int CountApplied(InventoryGameData inventory, string blueprintId)
	{
		var count = 0;
		foreach (var type in UpgradeableTypes)
		{
			List<IInventoryItemGameData> items;
			if (!inventory.Items.TryGetValue(type, out items))
			{
				continue;
			}
			foreach (var item in items)
			{
				count += GetApplications(item).Count(a => a.BlueprintId == blueprintId);
			}
		}
		return count;
	}

	// The use about to be spent: one past the uses currently applied anywhere, wrapping per blueprint copy.
	public static int GetNextUseNumber(InventoryGameData inventory, UpgradeBlueprintBalancingData definition)
	{
		var uses = Mathf.Max(1, definition.Uses);
		return CountApplied(inventory, definition.NameId) % uses + 1;
	}

	public static List<KeyValuePair<string, int>> GetNextUseCost(InventoryGameData inventory, UpgradeBlueprintBalancingData definition)
	{
		var costs = new List<KeyValuePair<string, int>>();
		DIContainerLogic.CraftingService.AddCraftingCostsForItem(ref costs, definition.NameId, GetNextUseNumber(inventory, definition));
		return costs;
	}

	public static bool CanPay(InventoryGameData inventory, List<KeyValuePair<string, int>> costs, out List<IInventoryItemGameData> missing)
	{
		missing = new List<IInventoryItemGameData>();
		foreach (var cost in costs)
		{
			var have = DIContainerLogic.InventoryService.GetItemValue(inventory, cost.Key);
			if (have < cost.Value)
			{
				missing.Add(DIContainerLogic.InventoryService.GenerateNewInventoryItemGameData(1, 0, cost.Key, cost.Value - have));
			}
		}
		return missing.Count == 0;
	}

	// ----- Applying, rerolling and removing -----

	// Equipment and class items both keep their applied upgrades in an Upgrades list on their saved data.
	public static List<string> GetUpgradeList(IInventoryItemData data)
	{
		var equipment = data as EquipmentData;
		if (equipment != null)
		{
			return equipment.Upgrades;
		}
		var classItem = data as ClassItemData;
		return classItem == null ? null : classItem.Upgrades;
	}

	private static void SetUpgradeList(IInventoryItemData data, List<string> upgrades)
	{
		var equipment = data as EquipmentData;
		if (equipment != null)
		{
			equipment.Upgrades = upgrades;
			return;
		}
		var classItem = data as ClassItemData;
		if (classItem != null)
		{
			classItem.Upgrades = upgrades;
		}
	}

	public static List<Application> GetApplications(IInventoryItemGameData item)
	{
		var upgrades = item == null ? null : GetUpgradeList(item.ItemData);
		if (upgrades == null)
		{
			return new List<Application>();
		}
		return upgrades.Select(Application.Parse).Where(a => a != null).ToList();
	}

	public static bool HasUpgrades(IInventoryItemGameData item)
	{
		var upgrades = item == null ? null : GetUpgradeList(item.ItemData);
		return upgrades != null && upgrades.Count > 0;
	}

	// Spends one use of the blueprint on target. Returns the index of the new application, or -1.
	public static int Apply(InventoryGameData inventory, CraftingRecipeGameData recipe, IInventoryItemGameData target)
	{
		UpgradeBlueprintBalancingData definition;
		if (!TryGetDefinition(recipe, out definition) || target == null || !Matches(definition, target.ItemBalancing) || GetUsesLeft(inventory, definition.NameId) <= 0)
		{
			return -1;
		}
		var costs = GetNextUseCost(inventory, definition);
		List<IInventoryItemGameData> missing;
		if (!CanPay(inventory, costs, out missing))
		{
			return -1;
		}
		foreach (var cost in costs)
		{
			DIContainerLogic.InventoryService.RemoveItem(inventory, cost.Key, cost.Value, "upgrade_blueprint_applied");
		}
		var application = new Application
		{
			BlueprintId = definition.NameId,
			Stars = DIContainerLogic.CraftingService.GetQuality(false),
			Refund = GenerateRefund(costs)
		};
		var index = GetApplications(target).Count;
		ForEachCopy(target, data =>
		{
			var upgrades = GetUpgradeList(data) ?? new List<string>();
			upgrades.Add(application.ToString());
			SetUpgradeList(data, upgrades);
		});
		DIContainerLogic.InventoryService.RemoveItem(inventory, recipe, 1, "upgrade_blueprint_applied");
		target.RaiseItemDataChanged(0f);
		return index;
	}

	public static void SetStars(IInventoryItemGameData target, int index, int stars)
	{
		var applications = GetApplications(target);
		if (index < 0 || index >= applications.Count)
		{
			return;
		}
		var before = applications[index].ToString();
		applications[index].Stars = Mathf.Clamp(stars, 0, 3);
		var after = applications[index].ToString();
		ForEachCopy(target, data =>
		{
			var upgrades = GetUpgradeList(data);
			var i = upgrades == null ? -1 : upgrades.LastIndexOf(before);
			if (i >= 0)
			{
				upgrades[i] = after;
			}
		});
		target.RaiseItemDataChanged(0f);
	}

	// The item's main stat with application `index` at `stars` stars (stars < 0: without that application).
	public static float PreviewMainStat(EquipmentGameData target, int index, int stars)
	{
		var upgrades = target.Data.Upgrades == null ? new List<string>() : new List<string>(target.Data.Upgrades);
		if (index >= 0 && index < upgrades.Count)
		{
			if (stars < 0)
			{
				upgrades.RemoveAt(index);
			}
			else
			{
				var application = Application.Parse(upgrades[index]);
				application.Stars = stars;
				upgrades[index] = application.ToString();
			}
		}
		return new EquipmentGameData(CopyData(target.Data, upgrades)).ItemMainStat;
	}

	public static int GetStars(IInventoryItemGameData target, int index)
	{
		var applications = GetApplications(target);
		return index >= 0 && index < applications.Count ? applications[index].Stars : 0;
	}

	// Takes every upgrade off target: the blueprint uses go back to the inventory and the stored refund is paid out.
	// Returns what the player got, for the scrap explosion.
	public static List<IInventoryItemGameData> RemoveAll(InventoryGameData inventory, IInventoryItemGameData target)
	{
		var gained = new List<IInventoryItemGameData>();
		var applications = GetApplications(target);
		if (applications.Count == 0)
		{
			return gained;
		}
		var refund = new Dictionary<string, int>();
		foreach (var application in applications)
		{
			foreach (var pair in application.Refund)
			{
				int amount;
				refund.TryGetValue(pair.Key, out amount);
				refund[pair.Key] = amount + pair.Value;
			}
		}
		if (refund.Count > 0)
		{
			gained.AddRange(DIContainerLogic.GetLootOperationService().RewardLootGetInputCopy(inventory, 0, DIContainerLogic.GetLootOperationService().GenerateLoot(refund, 0), "scraped_upgrade"));
		}
		foreach (var group in applications.GroupBy(a => a.BlueprintId))
		{
			UpgradeBlueprintBalancingData definition;
			if (!TryGetDefinition(group.Key, out definition))
			{
				continue;
			}
			DIContainerLogic.InventoryService.AddItem(inventory, 1, 0, group.Key, group.Count(), RefundReason);
			gained.Add(DIContainerLogic.InventoryService.GenerateNewInventoryItemGameData(1, 0, group.Key, group.Count()));
		}
		ForEachCopy(target, data => SetUpgradeList(data, null));
		target.RaiseItemDataChanged(0f);
		return gained;
	}

	private static Dictionary<string, int> GenerateRefund(List<KeyValuePair<string, int>> costs)
	{
		var provider = DIContainerBalancing.GetInventoryItemBalancingDataPovider();
		var first = costs.Count > 0 ? provider.GetBalancingData(costs[0].Key) : null;
		var second = costs.Count > 1 ? provider.GetBalancingData(costs[1].Key) : null;
		var third = costs.Count > 2 ? provider.GetBalancingData(costs[2].Key) : null;
		var refund = DIContainerLogic.CraftingService.GenerateScrapLoot(first, costs.Count > 0 ? costs[0].Value : 0, second, costs.Count > 1 ? costs[1].Value : 0, third, costs.Count > 2 ? costs[2].Value : 0, DIContainerBalancing.GameConstantsBalancingDataProvider.CostToScrapLootRateCrafting, EquipmentSource.Crafting);
		return refund ?? new Dictionary<string, int>();
	}

	// After a reload the item a bird has equipped is a separate copy of the player's inventory item (the game matches
	// them by name, level and stars), so upgrade changes are also written to the equipped copy. A copy sharing the
	// target's data object is always it; otherwise a lookalike is only updated when the player owns no identical twin
	// of the target, since the equipped one could then be either. Classes are unique per name.
	private static void ForEachCopy(IInventoryItemGameData target, Action<IInventoryItemData> change)
	{
		var signature = UpgradeSignature(target.ItemData);
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		var copies = new List<IInventoryItemGameData>();
		var type = target.ItemBalancing.ItemType;
		if (player != null)
		{
			List<IInventoryItemGameData> owned;
			var twins = player.InventoryGameData.Items.TryGetValue(type, out owned) ? owned.Count(e => IsLookalike(e, target, signature)) : 1;
			foreach (var bird in player.Birds)
			{
				List<IInventoryItemGameData> items;
				if (bird.InventoryGameData == null || !bird.InventoryGameData.Items.TryGetValue(type, out items))
				{
					continue;
				}
				foreach (var copy in items)
				{
					if (copy.ItemData != target.ItemData && twins <= 1 && IsLookalike(copy, target, signature))
					{
						copies.Add(copy);
					}
				}
			}
		}
		change(target.ItemData);
		foreach (var copy in copies)
		{
			change(copy.ItemData);
			copy.RaiseItemDataChanged(0f);
		}
	}

	private static bool IsLookalike(IInventoryItemGameData copy, IInventoryItemGameData target, string signature)
	{
		if (copy.ItemBalancing.NameId != target.ItemBalancing.NameId || UpgradeSignature(copy.ItemData) != signature)
		{
			return false;
		}
		if (target is ClassItemGameData)
		{
			return true;
		}
		return copy.ItemData.Level == target.ItemData.Level && copy.ItemData.Quality == target.ItemData.Quality && copy.IsAncient == target.IsAncient;
	}

	public static string UpgradeSignature(IInventoryItemData data)
	{
		var upgrades = data == null ? null : GetUpgradeList(data);
		return upgrades == null ? string.Empty : string.Join(";", upgrades.ToArray());
	}

	// Attack / health percent a bird gets from the upgrades on its class and equipped items.
	public static float GetBirdStatPercent(BirdGameData bird, bool attack)
	{
		var percent = 0f;
		foreach (var item in new IInventoryItemGameData[] { bird.ClassItem, bird.MainHandItem, bird.OffHandItem })
		{
			if (item == null)
			{
				continue;
			}
			foreach (var effect in Effects(GetUpgradeList(item.ItemData)))
			{
				percent += (attack ? effect.Key.AttackPercent : effect.Key.HealthPercent) * effect.Value.Multiplier;
			}
		}
		return percent;
	}

	// ----- Effects -----

	private static IEnumerable<KeyValuePair<UpgradeBlueprintBalancingData, Application>> Effects(EquipmentData data)
	{
		return Effects(data == null ? null : data.Upgrades);
	}

	private static IEnumerable<KeyValuePair<UpgradeBlueprintBalancingData, Application>> Effects(List<string> upgrades)
	{
		if (upgrades == null)
		{
			yield break;
		}
		foreach (var entry in upgrades)
		{
			var application = Application.Parse(entry);
			UpgradeBlueprintBalancingData definition;
			if (application != null && TryGetDefinition(application.BlueprintId, out definition))
			{
				yield return new KeyValuePair<UpgradeBlueprintBalancingData, Application>(definition, application);
			}
		}
	}

	public static float ApplyToMainStat(EquipmentData data, float stat)
	{
		var flat = 0f;
		var percent = 0f;
		foreach (var effect in Effects(data))
		{
			flat += effect.Key.FlatStat * effect.Value.Multiplier;
			percent += effect.Key.StatPercent * effect.Value.Multiplier;
		}
		return (stat + flat) * (1f + percent / 100f);
	}

	// The item's perk after its upgrades: "set" values first (they never lower a value), then percentage boosts.
	public static EquipmentPerk GetEffectivePerk(EquipmentPerk basePerk, EquipmentData data)
	{
		if (basePerk == null || data == null || data.Upgrades == null || data.Upgrades.Count == 0)
		{
			return basePerk;
		}
		var perk = new EquipmentPerk
		{
			Type = basePerk.Type,
			ProbablityInPercent = basePerk.ProbablityInPercent,
			PerkValue = basePerk.PerkValue,
			PerkValue2 = basePerk.PerkValue2
		};
		var effects = Effects(data).ToList();
		foreach (var effect in effects)
		{
			if (effect.Key.SetPerkValue > 0f)
			{
				perk.PerkValue = Mathf.Max(perk.PerkValue, effect.Key.SetPerkValue * effect.Value.Multiplier);
			}
			if (effect.Key.SetPerkValue2 > 0f)
			{
				perk.PerkValue2 = Mathf.Max(perk.PerkValue2, effect.Key.SetPerkValue2 * effect.Value.Multiplier);
			}
		}
		var valuePercent = 0f;
		foreach (var effect in effects)
		{
			valuePercent += effect.Key.PerkValuePercent * effect.Value.Multiplier;
			perk.ProbablityInPercent = Mathf.Min(100f, perk.ProbablityInPercent + effect.Key.PerkChanceBonus * effect.Value.Multiplier);
		}
		perk.PerkValue *= 1f + valuePercent / 100f;
		perk.PerkValue2 *= 1f + valuePercent / 100f;
		return perk;
	}

	public static List<EquipmentPerk> GetBonusPerks(EquipmentData data)
	{
		var perks = new List<EquipmentPerk>();
		foreach (var effect in Effects(data))
		{
			PerkType type;
			if (string.IsNullOrEmpty(effect.Key.AddPerkType) || !TryParsePerk(effect.Key.AddPerkType, out type))
			{
				continue;
			}
			perks.Add(new EquipmentPerk
			{
				Type = type,
				ProbablityInPercent = effect.Key.AddPerkChance > 0f ? effect.Key.AddPerkChance : 100f,
				PerkValue = effect.Key.AddPerkValue * effect.Value.Multiplier,
				PerkValue2 = effect.Key.AddPerkValue2 * effect.Value.Multiplier
			});
		}
		return perks;
	}

	private static bool TryParsePerk(string name, out PerkType type)
	{
		try
		{
			type = (PerkType)Enum.Parse(typeof(PerkType), name, true);
			return true;
		}
		catch (ArgumentException)
		{
			type = PerkType.None;
			return false;
		}
	}

	// Stat perks work on the off hand's stats / hits taken; every other perk works on attacks. Perks added by an upgrade
	// are routed by this, whichever hand the item is.
	public static bool IsStatPerk(PerkType type)
	{
		switch (type)
		{
		case PerkType.Vitality:
		case PerkType.Might:
		case PerkType.Brilliance:
		case PerkType.Vigor:
		case PerkType.Guardian:
		case PerkType.PhatLoot:
		case PerkType.Amplify:
			return true;
		default:
			return false;
		}
	}

	// A perk of the given type on either equipped item, upgrades included.
	public static EquipmentPerk FindEquippedPerk(ICombatant combatant, PerkType type)
	{
		if (combatant == null)
		{
			return null;
		}
		var offHand = combatant.CombatantOffHandEquipment == null ? null : combatant.CombatantOffHandEquipment.FindPerk(type);
		return offHand ?? (combatant.CombatantMainHandEquipment == null ? null : combatant.CombatantMainHandEquipment.FindPerk(type));
	}

	// Perks for the stat / on-hit hooks: the off hand's own perk plus stat-type bonus perks from both hands.
	public static List<EquipmentPerk> GetStatPerks(ICombatant combatant)
	{
		var perks = new List<EquipmentPerk>();
		var offHand = combatant.CombatantOffHandEquipment;
		if (offHand != null && offHand.Perk != null)
		{
			perks.Add(offHand.Perk);
		}
		perks.AddRange(GetBonusPerks(combatant).Where(p => IsStatPerk(p.Type)));
		return perks;
	}

	// Attack-type bonus perks from both hands (the main hand's own perk is applied by the attack code as before).
	public static List<EquipmentPerk> GetAttackBonusPerks(ICombatant combatant)
	{
		return GetBonusPerks(combatant).Where(p => !IsStatPerk(p.Type)).ToList();
	}

	private static IEnumerable<EquipmentPerk> GetBonusPerks(ICombatant combatant)
	{
		if (combatant == null)
		{
			yield break;
		}
		foreach (var equipment in new[] { combatant.CombatantMainHandEquipment, combatant.CombatantOffHandEquipment })
		{
			if (equipment == null)
			{
				continue;
			}
			foreach (var perk in equipment.BonusPerks)
			{
				yield return perk;
			}
		}
	}

	private const string GoldParchmentName = "UpgradeBlueprintGold";

	// Swaps the blueprint parchment sprite ("Recipe") under root for the golden one in Resources/UI/Recipe_Upgrade,
	// or back. The parchment keeps its place, size and depth, so whatever is drawn on it is unchanged.
	public static void SetGoldParchment(GameObject root, bool gold)
	{
		var parchment = root.GetComponentsInChildren<UISprite>(true).FirstOrDefault(s => s.spriteName == "Recipe");
		if (parchment == null)
		{
			return;
		}
		// The gold scroll is a child of the original so it follows its animated transform (a copy of the
		// transform taken mid-animation stayed squashed)
		var existing = parchment.transform.Find(GoldParchmentName);
		var oldSibling = parchment.transform.parent != null ? parchment.transform.parent.Find(GoldParchmentName) : null;
		if (oldSibling != null)
		{
			UnityEngine.Object.Destroy(oldSibling.gameObject);
		}
		if (!gold)
		{
			if (existing != null)
			{
				UnityEngine.Object.Destroy(existing.gameObject);
			}
			parchment.enabled = true;
			return;
		}
		if (existing != null)
		{
			parchment.enabled = false;
			return;
		}
		var texture = Resources.Load<Texture2D>("UI/Recipe_Upgrade");
		if (texture == null)
		{
			return;
		}
		var goldObject = new GameObject(GoldParchmentName);
		goldObject.layer = parchment.gameObject.layer;
		goldObject.transform.parent = parchment.transform;
		goldObject.transform.localPosition = Vector3.zero;
		goldObject.transform.localRotation = Quaternion.identity;
		goldObject.transform.localScale = Vector3.one;
		var uiTexture = goldObject.AddComponent<UITexture>();
		uiTexture.shader = Shader.Find("Unlit/Transparent Colored");
		uiTexture.mainTexture = texture;
		uiTexture.rawPivot = parchment.pivot;
		uiTexture.depth = parchment.depth;
		// The golden texture is exactly the authored parchment sprite (162x166); size from it rather than from the live
		// sprite, which was found narrower at runtime on some tiles and made the gold scroll squashed
		uiTexture.width = texture.width;
		uiTexture.height = texture.height;
		if (parchment.width != texture.width || parchment.height != texture.height)
		{
			DebugLog.Log("[UpgradeBlueprints] parchment " + parchment.name + " is " + parchment.width + "x" + parchment.height + ", gold drawn at " + texture.width + "x" + texture.height);
		}
		parchment.enabled = false;
	}

	// "Brilliance Tome +2 stars" style lines for the item's info texts.
	public static string GetUpgradeSummary(EquipmentGameData equipment)
	{
		var lines = new List<string>();
		foreach (var group in GetApplications(equipment).GroupBy(a => a.BlueprintId))
		{
			UpgradeBlueprintBalancingData definition;
			if (!TryGetDefinition(group.Key, out definition))
			{
				continue;
			}
			var stars = string.Join(" ", group.Select(a => new string('*', a.Stars).PadRight(3, '-')).ToArray());
			lines.Add("[c8a000]" + GetName(definition) + (group.Count() > 1 ? " x" + group.Count() : string.Empty) + "[-] " + stars);
		}
		return string.Join("\n", lines.ToArray());
	}

	// ----- Old -> new stat preview -----

	public class StatChange
	{
		public string OldIcon;

		public string OldValue;

		public string NewIcon;

		public string NewValue;
	}

	// What one use (at 0 stars) changes on the item it would go on: the player's best matching item, or the first
	// supported item (equipment at level 1) when none is owned.
	public static List<StatChange> GetStatChanges(UpgradeBlueprintBalancingData definition, InventoryGameData inventory)
	{
		var changes = new List<StatChange>();
		var reference = inventory == null ? null : GetEligibleOwned(inventory, definition).OrderByDescending(e => e.ItemMainStat).FirstOrDefault();
		var supported = reference == null ? GetSupportedItems(definition).FirstOrDefault() : reference.ItemBalancing;
		if (supported == null)
		{
			return changes;
		}
		var oldUpgrades = reference == null ? null : GetUpgradeList(reference.ItemData);
		var newUpgrades = oldUpgrades == null ? new List<string>() : new List<string>(oldUpgrades);
		newUpgrades.Add(new Application { BlueprintId = definition.NameId }.ToString());

		// Bird-wide attack / health percent, shown as the bonus before and after this use
		AddPercentChange(changes, "Character_Damage_Large", oldUpgrades, newUpgrades, true);
		AddPercentChange(changes, "Character_Health_Large", oldUpgrades, newUpgrades, false);

		if (!(supported is EquipmentBalancingData))
		{
			return changes;
		}
		var referenceEquipment = reference as EquipmentGameData;
		var before = referenceEquipment != null ? CopyData(referenceEquipment.Data, oldUpgrades) : new EquipmentData { NameId = supported.NameId, Level = 1 };
		var oldItem = new EquipmentGameData(before);
		var newItem = new EquipmentGameData(CopyData(before, newUpgrades));
		var statIcon = oldItem.BalancingData.ItemType == InventoryItemType.MainHandEquipment ? "Character_Damage_Large" : "Character_Health_Large";
		if (Mathf.RoundToInt(oldItem.ItemMainStat) != Mathf.RoundToInt(newItem.ItemMainStat))
		{
			changes.Add(new StatChange { OldIcon = statIcon, OldValue = Mathf.RoundToInt(oldItem.ItemMainStat).ToString(), NewIcon = statIcon, NewValue = Mathf.RoundToInt(newItem.ItemMainStat).ToString() });
		}
		var oldPerk = oldItem.Perk;
		var newPerk = newItem.Perk;
		if (oldPerk != null && newPerk != null)
		{
			var icon = EquipmentGameData.GetPerkIconNameByPerk(oldPerk.Type);
			AddPerkValueChange(changes, icon, oldPerk.ProbablityInPercent, newPerk.ProbablityInPercent, "%");
			AddPerkValueChange(changes, icon, oldPerk.PerkValue, newPerk.PerkValue, PerkValueSuffix(oldPerk.Type, 2));
			AddPerkValueChange(changes, GetSecondValueIcon(oldPerk.Type), oldPerk.PerkValue2, newPerk.PerkValue2, PerkValueSuffix(oldPerk.Type, 3));
		}
		foreach (var bonusPerk in newItem.BonusPerks.Skip(oldItem.BonusPerks.Count))
		{
			var value = bonusPerk.PerkValue != 0f ? bonusPerk.PerkValue : bonusPerk.ProbablityInPercent;
			changes.Add(new StatChange { OldIcon = null, OldValue = "-", NewIcon = EquipmentGameData.GetPerkIconNameByPerk(bonusPerk.Type), NewValue = FormatValue(value) + PerkValueSuffix(bonusPerk.Type, 2) });
		}
		return changes;
	}

	private static void AddPercentChange(List<StatChange> changes, string icon, List<string> oldUpgrades, List<string> newUpgrades, bool attack)
	{
		var oldPercent = Effects(oldUpgrades).Sum(e => (attack ? e.Key.AttackPercent : e.Key.HealthPercent) * e.Value.Multiplier);
		var newPercent = Effects(newUpgrades).Sum(e => (attack ? e.Key.AttackPercent : e.Key.HealthPercent) * e.Value.Multiplier);
		if (!Mathf.Approximately(oldPercent, newPercent))
		{
			changes.Add(new StatChange { OldIcon = icon, OldValue = "+" + FormatValue(oldPercent) + "%", NewIcon = icon, NewValue = "+" + FormatValue(newPercent) + "%" });
		}
	}

	private static void AddPerkValueChange(List<StatChange> changes, string icon, float oldValue, float newValue, string suffix)
	{
		if (Mathf.Approximately(oldValue, newValue))
		{
			return;
		}
		changes.Add(new StatChange { OldIcon = icon, OldValue = FormatValue(oldValue) + suffix, NewIcon = icon, NewValue = FormatValue(newValue) + suffix });
	}

	// A perk value is a percentage when its description prints it followed by "%".
	private static string PerkValueSuffix(PerkType type, int valueIndex)
	{
		var desc = DIContainerInfrastructure.GetLocaService().Tr("perk_" + type.ToString().ToLower() + "_desc", string.Empty);
		return desc.Contains("{value_" + valueIndex + "}%") ? "%" : string.Empty;
	}

	// The second perk value usually belongs to a different stat than the perk's own icon shows.
	private static string GetSecondValueIcon(PerkType type)
	{
		switch (type)
		{
		case PerkType.Brilliance:
			return "PassiveEffect_CriticalStrike";
		default:
			return EquipmentGameData.GetPerkIconNameByPerk(type);
		}
	}

	private static EquipmentData CopyData(EquipmentData data, List<string> upgrades)
	{
		return new EquipmentData
		{
			NameId = data.NameId,
			Level = data.Level,
			Value = data.Value,
			Quality = data.Quality,
			ItemSource = data.ItemSource,
			EnchantmentLevel = data.EnchantmentLevel,
			EnchantmentProgress = data.EnchantmentProgress,
			IsAncient = data.IsAncient,
			Upgrades = upgrades == null ? null : new List<string>(upgrades)
		};
	}

	public static string FormatValue(float value)
	{
		return Mathf.Approximately(value, Mathf.Round(value)) ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.#", CultureInfo.InvariantCulture);
	}
}
