using System;

namespace ABH.Shared.Generic
{
	public enum InventoryItemType
	{
		None,
		Resources,
		Ingredients,
		MainHandEquipment,
		OffHandEquipment,
		Consumable,
		Premium,
		Story,
		PlayerToken,
		Points,
		Class,
		PlayerStats,
		CraftingRecipes,
		EventBattleItem,
		EventCollectible,
		Mastery,
		BannerTip,
		Banner,
		BannerEmblem,
		EventCampaignItem,
		EventBossItem,
		CollectionComponent,
		Trophy,
		Skin,
		// Carried by a bird without a fixed equipping slot (unlike Class/headgear, MainHand, OffHand).
		Trinket
	}
}
