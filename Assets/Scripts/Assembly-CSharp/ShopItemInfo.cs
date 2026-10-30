using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.GameDatas.Interfaces;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using ABH.Shared.Interfaces;
using ABH.Shared.Models.Generic;
using ABH.Shared.Models.InventoryItems;
using UnityEngine;

// What the shop's tap-and-hold overlay (ShopItemInfoOverlay) shows for gear, equipment blueprints and upgrade
// blueprints: name, stats with one row per star count, perks with each of their values, and the crafting recipe.
// Values of 0 are left out. Built from game data here; the overlay only lays it out (so editor previews can fill it).
public class ShopItemInfo
{
	// An icon (atlas sprite or a texture in Resources/UI) with a value next to it
	public class Cell
	{
		public string Atlas;

		public string Sprite;

		public string Texture;

		public string Text;

		public Color Color = TextColor;

		public static Cell FromSprite(string sprite, string text, Color color, string atlas = null)
		{
			return new Cell { Atlas = atlas, Sprite = sprite, Text = text, Color = color };
		}

		public static Cell FromTexture(string texture, string text, Color color)
		{
			return new Cell { Texture = texture, Text = text, Color = color };
		}
	}

	public class PerkInfo
	{
		public Cell Icon;

		public string Name;

		public List<Cell> Values = new List<Cell>();
	}

	public static readonly Color TextColor = new Color(0.3529412f, 0.20790161f, 0.10380623f, 1f);

	public static readonly Color Green = new Color(0f, 0.6f, 0.05f, 1f);

	public static readonly Color Red = new Color(0.85f, 0.08f, 0.04f, 1f);

	public static readonly Color Blue = new Color(0.1f, 0.38f, 0.85f, 1f);

	public static readonly Color Purple = new Color(0.6f, 0.16f, 0.78f, 1f);

	public const string ChanceIcon = "PerkStat_Chance";

	public const string RageDrainIcon = "PerkStat_RageDrain";

	public const string WeakenIcon = "PerkStat_Weaken";

	private const string DamageIcon = "Character_Damage_Large";

	private const string HealthIcon = "Character_Health_Large";

	private const string HealIcon = "PassiveEffect_HocusPocus";

	public string Name;

	// StatRows[stars]: the stats at 0..3 stars. Rows equal to the 0 star row are dropped.
	public List<List<Cell>> StatRows = new List<List<Cell>>();

	public List<PerkInfo> Perks = new List<PerkInfo>();

	public List<Cell> Recipe = new List<Cell>();

	public string RecipeCaption;

	// Gear, equipment blueprints and upgrade blueprints get this overlay; null for anything else
	public static ShopItemInfo Create(IInventoryItemGameData item)
	{
		var recipe = item as CraftingRecipeGameData;
		if (recipe != null)
		{
			UpgradeBlueprintBalancingData upgradeBlueprint;
			if (UpgradeBlueprints.TryGetDefinition(recipe, out upgradeBlueprint))
			{
				return FromUpgradeBlueprint(upgradeBlueprint);
			}
			var category = recipe.BalancingData.RecipeCategoryType;
			if (category != InventoryItemType.MainHandEquipment && category != InventoryItemType.OffHandEquipment)
			{
				return null;
			}
			var loot = DIContainerLogic.GetLootOperationService().GenerateLoot(recipe.GetResultLoot(), recipe.Data.Level);
			var result = DIContainerLogic.GetLootOperationService().GetItemsFromLoot(DIContainerInfrastructure.GetCurrentPlayer(), loot).FirstOrDefault() as EquipmentGameData;
			return result == null || result.IsSetItem ? null : FromEquipment(result, true);
		}
		var equipment = item as EquipmentGameData;
		if (equipment == null || equipment.IsSetItem)
		{
			return null;
		}
		var type = equipment.BalancingData.ItemType;
		return type == InventoryItemType.MainHandEquipment || type == InventoryItemType.OffHandEquipment ? FromEquipment(equipment, false) : null;
	}

	private static ShopItemInfo FromEquipment(EquipmentGameData equipment, bool isBlueprint)
	{
		var loca = DIContainerInfrastructure.GetLocaService();
		var info = new ShopItemInfo();
		info.Name = isBlueprint ? loca.Tr("prefix_blueprint", "Blueprint: ") + equipment.ItemLocalizedName : equipment.ItemLocalizedName;
		var statIcon = equipment.BalancingData.ItemType == InventoryItemType.MainHandEquipment ? DamageIcon : HealthIcon;
		// Same stat the shop tile shows: blueprints at enchantment 3, sold gear at its own enchantment
		var enchantment = isBlueprint ? 3 : -1;
		for (var stars = 0; stars <= 3; stars++)
		{
			var copy = new EquipmentGameData(new EquipmentData
			{
				NameId = equipment.Data.NameId,
				Level = equipment.Data.Level,
				Value = 1,
				Quality = stars,
				ItemSource = equipment.Data.ItemSource,
				EnchantmentLevel = equipment.Data.EnchantmentLevel,
				IsAncient = equipment.Data.IsAncient,
				Upgrades = equipment.Data.Upgrades == null ? null : new List<string>(equipment.Data.Upgrades)
			});
			var row = new List<Cell>();
			AddValue(row, statIcon, Mathf.Round(EquipmentGameData.GetItemMainStat(copy, enchantment)), string.Empty, string.Empty, TextColor);
			info.StatRows.Add(row);
		}
		foreach (var perk in equipment.AllPerks)
		{
			info.Perks.Add(GetPerkInfo(perk));
		}
		var costs = new List<KeyValuePair<string, int>>();
		DIContainerLogic.CraftingService.AddCraftingCostsForItem(ref costs, equipment.BalancingData.NameId, equipment.Data.Level);
		info.SetRecipe(costs);
		info.DropUnchangedStarRows();
		return info;
	}

	// One use of the blueprint on the first item it supports: its stats (scaled by stars like a forge roll), the perk
	// it changes or adds, and the cost of the next use.
	private static ShopItemInfo FromUpgradeBlueprint(UpgradeBlueprintBalancingData definition)
	{
		var info = new ShopItemInfo();
		info.Name = UpgradeBlueprints.GetName(definition);
		var supported = UpgradeBlueprints.GetSupportedItems(definition);
		var equipmentBalancing = supported.OfType<EquipmentBalancingData>().FirstOrDefault();
		var statIcon = equipmentBalancing != null && equipmentBalancing.ItemType == InventoryItemType.OffHandEquipment ? HealthIcon : DamageIcon;
		for (var stars = 0; stars <= 3; stars++)
		{
			var multiplier = 1f + stars * UpgradeBlueprints.StarBonusPercent / 100f;
			var row = new List<Cell>();
			AddValue(row, statIcon, definition.FlatStat * multiplier, "+", string.Empty, TextColor);
			AddValue(row, statIcon, definition.StatPercent * multiplier, "+", "%", TextColor);
			AddValue(row, DamageIcon, definition.AttackPercent * multiplier, "+", "%", TextColor);
			AddValue(row, HealthIcon, definition.HealthPercent * multiplier, "+", "%", TextColor);
			AddRageSkillValue(row, definition, supported.FirstOrDefault());
			info.StatRows.Add(row);
		}
		if (equipmentBalancing != null)
		{
			var application = new UpgradeBlueprints.Application { BlueprintId = definition.NameId };
			var upgraded = new EquipmentGameData(new EquipmentData { NameId = equipmentBalancing.NameId, Level = 1, Value = 1, Upgrades = new List<string> { application.ToString() } });
			var changesPerk = definition.SetPerkValue > 0f || definition.SetPerkValue2 > 0f || definition.PerkValuePercent != 0f || definition.PerkChanceBonus != 0f;
			if (changesPerk && upgraded.Perk != null)
			{
				info.Perks.Add(GetPerkInfo(upgraded.Perk));
			}
			foreach (var bonusPerk in upgraded.BonusPerks)
			{
				info.Perks.Add(GetPerkInfo(bonusPerk));
			}
		}
		info.SetRecipe(UpgradeBlueprints.GetNextUseCost(DIContainerInfrastructure.GetCurrentPlayer().InventoryGameData, definition));
		info.DropUnchangedStarRows();
		return info;
	}

	private static void AddRageSkillValue(List<Cell> row, UpgradeBlueprintBalancingData definition, IInventoryItemBalancingData supported)
	{
		if (string.IsNullOrEmpty(definition.RageSkillParameter) || definition.RageSkillParameterBonus == 0f || supported == null)
		{
			return;
		}
		var equipment = supported as EquipmentBalancingData;
		var classItem = supported as ClassItemBalancingData;
		var birdId = equipment != null ? equipment.RestrictedBirdId : (classItem != null ? classItem.RestrictedBirdId : null);
		BirdBalancingData bird;
		SkillBalancingData rage;
		if (string.IsNullOrEmpty(birdId) || !DIContainerBalancing.Service.TryGetBalancingData(birdId, out bird) || string.IsNullOrEmpty(bird.RageSkillIdent) || !DIContainerBalancing.Service.TryGetBalancingData(bird.RageSkillIdent, out rage))
		{
			return;
		}
		row.Add(Cell.FromSprite(rage.IconAssetId, "+" + UpgradeBlueprints.FormatValue(definition.RageSkillParameterBonus), TextColor, rage.IconAtlasId));
	}

	private void SetRecipe(List<KeyValuePair<string, int>> costs)
	{
		var inventory = DIContainerInfrastructure.GetCurrentPlayer().InventoryGameData;
		var amounts = new Dictionary<string, int>();
		foreach (var cost in costs.Where(c => c.Value > 0))
		{
			amounts[cost.Key] = (amounts.ContainsKey(cost.Key) ? amounts[cost.Key] : 0) + cost.Value;
		}
		if (amounts.Count == 0)
		{
			return;
		}
		var items = DIContainerLogic.GetLootOperationService().GetItemsFromLoot(DIContainerLogic.GetLootOperationService().GenerateLootPreview(amounts, 1));
		foreach (var item in items)
		{
			int amount;
			if (!amounts.TryGetValue(item.ItemBalancing.NameId, out amount))
			{
				continue;
			}
			var craftingItem = item as CraftingItemGameData;
			var atlas = craftingItem != null ? craftingItem.BalancingData.AtlasNameId : null;
			// Red when the player can't pay it yet
			var owned = DIContainerLogic.InventoryService.GetItemValue(inventory, item.ItemBalancing.NameId);
			Recipe.Add(Cell.FromSprite(item.ItemAssetName, amount.ToString(), owned >= amount ? TextColor : Red, atlas));
		}
		RecipeCaption = Recipe.Count > 0 ? DIContainerInfrastructure.GetLocaService().Tr("shop_tt_recipe", "Recipe") : null;
	}

	public void DropUnchangedStarRows()
	{
		if (StatRows.Count == 0)
		{
			return;
		}
		var baseRow = StatRows[0];
		for (var i = StatRows.Count - 1; i > 0; i--)
		{
			var row = StatRows[i];
			if (row.Count == baseRow.Count && row.Select(c => c.Text).SequenceEqual(baseRow.Select(c => c.Text)))
			{
				StatRows.RemoveAt(i);
			}
		}
		if (baseRow.Count == 0 && StatRows.Count == 1)
		{
			StatRows.Clear();
		}
	}

	private static void AddValue(List<Cell> row, string sprite, float value, string prefix, string suffix, Color color)
	{
		if (Mathf.Approximately(value, 0f))
		{
			return;
		}
		row.Add(Cell.FromSprite(sprite, prefix + UpgradeBlueprints.FormatValue(value) + suffix, color));
	}

	// value_1 = ProbablityInPercent, value_2 = PerkValue, value_3 = PerkValue2 (see the perk descriptions)
	public static PerkInfo GetPerkInfo(EquipmentPerk perk)
	{
		var info = new PerkInfo { Name = DIContainerInfrastructure.GetLocaService().GetPerkName(perk.Type) };
		var icon = EquipmentGameData.GetPerkIconNameByPerk(perk.Type);
		info.Icon = perk.Type == PerkType.Weaken ? Cell.FromTexture(WeakenIcon, null, TextColor) : Cell.FromSprite(icon, null, TextColor);
		var values = info.Values;
		var chance = perk.ProbablityInPercent;
		var value = perk.PerkValue;
		var value2 = perk.PerkValue2;
		// A 100% chance is just how the perk always works
		if (chance > 0f && chance < 100f)
		{
			values.Add(Cell.FromTexture(ChanceIcon, UpgradeBlueprints.FormatValue(chance) + "%", Blue));
		}
		switch (perk.Type)
		{
		case PerkType.CriticalStrike:
		case PerkType.ChainAttack:
			AddValue(values, DamageIcon, value, string.Empty, "%", Red);
			break;
		case PerkType.Bedtime:
			AddTurns(values, "PassiveEffect_Stun", value, Red);
			break;
		case PerkType.HocusPokus:
			AddValue(values, HealIcon, value, string.Empty, "%", Green);
			break;
		case PerkType.HocusPokusChiliDrain:
			AddValue(values, HealIcon, value, string.Empty, "%", Green);
			if (value2 != 0f)
			{
				values.Add(Cell.FromTexture(RageDrainIcon, UpgradeBlueprints.FormatValue(value2) + "%", Red));
			}
			break;
		case PerkType.Vigor:
			AddValue(values, "PassiveEffect_Vigor", value, string.Empty, "%", Green);
			break;
		case PerkType.Might:
			AddValue(values, DamageIcon, value, "+", "%", Red);
			break;
		case PerkType.Vitality:
			AddValue(values, HealthIcon, value, "+", "%", Green);
			break;
		case PerkType.Weaken:
			if (value != 0f)
			{
				values.Add(Cell.FromTexture(WeakenIcon, UpgradeBlueprints.FormatValue(value) + "%", Purple));
			}
			break;
		case PerkType.Brilliance:
			AddValue(values, DamageIcon, value, "+", "%", Red);
			AddValue(values, "PassiveEffect_CriticalStrike", value2, "+", "%", Red);
			break;
		case PerkType.Amplify:
			AddValue(values, "PassiveEffect_Balance", value, "+", "%", Green);
			break;
		case PerkType.PhatLoot:
			AddValue(values, "Resource_Coin", value2, "x", string.Empty, Green);
			AddValue(values, "Resource_Coin", value, "-", string.Empty, Red);
			break;
		case PerkType.CashStrike:
			AddValue(values, "Resource_Coin", value, string.Empty, "%", Green);
			AddValue(values, DamageIcon, value2, "x", string.Empty, Red);
			break;
		case PerkType.Dynamite:
			AddTurns(values, null, value2, Blue);
			break;
		case PerkType.GroundZero:
			AddValue(values, DamageIcon, value, string.Empty, "%", Red);
			break;
		case PerkType.Rally:
			AddValue(values, "PassiveEffect_Vigor", value, "+", "%", Green);
			break;
		case PerkType.Dispel:
		case PerkType.Guardian:
			break;
		default:
			AddValue(values, icon, value, string.Empty, string.Empty, TextColor);
			AddValue(values, icon, value2, string.Empty, string.Empty, TextColor);
			break;
		}
		return info;
	}

	private static void AddTurns(List<Cell> values, string sprite, float turns, Color color)
	{
		if (Mathf.Approximately(turns, 0f))
		{
			return;
		}
		var text = Mathf.Approximately(turns, 1f) ? DIContainerInfrastructure.GetLocaService().Tr("shop_tt_turn", "{value_1} turn") : DIContainerInfrastructure.GetLocaService().Tr("shop_tt_turns", "{value_1} turns");
		values.Add(Cell.FromSprite(sprite, text.Replace("{value_1}", UpgradeBlueprints.FormatValue(turns)), color));
	}
}
