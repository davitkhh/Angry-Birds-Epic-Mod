using System;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.GameDatas.Interfaces;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using ABH.Shared.Models;
using ABH.Shared.Models.Character;

public class ShowcaseCampBird
{
	public string NameId;

	public int Level;

	public ShowcaseCampBird(string nameId, int level)
	{
		NameId = nameId;
		Level = level;
	}
}

public class ShowcaseCampAlly
{
	// The bird the NPC lends as an ally; it is the only bird shown in the showcase camp.
	public string BirdNameId;

	public string DefaultMainHand;

	// For McCool this is bird_offhand_merchant_01, which has no artwork and is shown as "No Off Hand".
	public string DefaultOffHand;

	// Mirrors the friend level the friend-bird window lends this ally at.
	public Func<int, int> LevelFromPlayerLevel = playerLevel => playerLevel;
}

public class ShowcaseCampDefinition
{
	public string Id;

	public string NameLocaKey;

	public string AvatarSprite;

	public string SceneName;

	public int Level = 1;

	// A BannerBalancingData NameId (e.g. bird_banner_porky, which carries McCool's bird_banner_mccool inventory), not an inventory id.
	public string BannerNameId = "bird_banner_porky";

	public string InventoryLootTableNameId = "loot_npc_inventory";

	public List<ShowcaseCampBird> Birds = new List<ShowcaseCampBird>();

	// When set, the camp shows only this ally, and the player can pick its Main Hand and Off Hand.
	public ShowcaseCampAlly Ally;

	public Action<PublicPlayerData> Customize;
}

public static class ShowcaseCamps
{
	// A showcase Id is the NPC friend's Id, so visiting that friend always opens the showcase instead of Camp_Friend.
	public const string PiggyMcCool = "NPC_Low";

	private static readonly Dictionary<string, ShowcaseCampDefinition> m_Definitions = new Dictionary<string, ShowcaseCampDefinition>();

	static ShowcaseCamps()
	{
		Register(new ShowcaseCampDefinition
		{
			Id = PiggyMcCool,
			NameLocaKey = "npc_friend_merchant",
			AvatarSprite = "Avatar_MerchantPig",
			SceneName = "Camp_McCool",
			Level = 50,
			Ally = new ShowcaseCampAlly
			{
				BirdNameId = "bird_merchant",
				DefaultMainHand = "bird_weapon_merchant_01",
				DefaultOffHand = "bird_offhand_merchant_01",
				LevelFromPlayerLevel = playerLevel => DIContainerLogic.SocialService.GetLowNPCFriend(playerLevel).Level
			}
		});
	}

	public static void Register(ShowcaseCampDefinition definition)
	{
		m_Definitions[definition.Id] = definition;
	}

	public static bool TryGet(string id, out ShowcaseCampDefinition definition)
	{
		if (string.IsNullOrEmpty(id))
		{
			definition = null;
			return false;
		}
		return m_Definitions.TryGetValue(id, out definition);
	}

	public static bool IsShowcase(string id)
	{
		ShowcaseCampDefinition definition;
		return TryGet(id, out definition);
	}

	public static FriendGameData CreateFriend(ShowcaseCampDefinition definition)
	{
		var friendData = new FriendData();
		friendData.IsNPC = true;
		friendData.Id = definition.Id;
		friendData.FirstName = definition.NameLocaKey;
		friendData.IsSilhouettePicture = false;
		friendData.Level = definition.Level;
		friendData.PictureUrl = string.Empty;
		return new FriendGameData(definition.Id).SetFriendData(friendData).SetPublicPlayerData(BuildPlayer(definition));
	}

	private static PublicPlayerData BuildPlayer(ShowcaseCampDefinition definition)
	{
		var publicPlayerData = new PublicPlayerData();
		publicPlayerData.LastSaveTime = DIContainerLogic.GetDeviceTimingService().GetCurrentTimestamp();
		publicPlayerData.LocationProgress = new Dictionary<LocationType, int>
		{
			{
				LocationType.World,
				0
			},
			{
				LocationType.ChronicleCave,
				0
			}
		};
		publicPlayerData.Inventory = DIContainerLogic.SocialService.GetNPCInventory(definition.InventoryLootTableNameId);
		publicPlayerData.Birds = new List<BirdData>();
		if (definition.Ally != null)
		{
			publicPlayerData.Birds.Add(BuildAllyBird(definition));
		}
		else
		{
			foreach (var bird in definition.Birds)
			{
				publicPlayerData.Birds.Add(new BirdGameData(bird.NameId, bird.Level).Data);
			}
		}
		publicPlayerData.SocialId = definition.Id;
		publicPlayerData.SocialPlayerName = DIContainerInfrastructure.GetLocaService().Tr(definition.NameLocaKey);
		publicPlayerData.Banner = new BannerGameData(definition.BannerNameId, definition.Level).Data;
		publicPlayerData.Level = definition.Level;
		publicPlayerData.PvPIndices = new List<int> { 0, 1, 2 };
		if (definition.Customize != null)
		{
			definition.Customize(publicPlayerData);
		}
		return publicPlayerData;
	}

	public static bool TryGetAlly(string birdNameId, out ShowcaseCampDefinition definition)
	{
		definition = m_Definitions.Values.FirstOrDefault(d => d.Ally != null && d.Ally.BirdNameId == birdNameId);
		return definition != null;
	}

	// The showcase camp currently being visited, if it has an editable ally.
	public static ShowcaseCampDefinition GetVisitedAllyCamp()
	{
		ShowcaseCampDefinition definition;
		if (ClientInfo.IsFriend && ClientInfo.InspectedFriend != null && TryGet(ClientInfo.InspectedFriend.FriendId, out definition) && definition.Ally != null)
		{
			return definition;
		}
		return null;
	}

	// The ally bird being edited in the visited showcase camp; the player never owns it, so stat comparisons need it from here.
	public static BirdGameData GetVisitedAllyBird(string birdNameId)
	{
		var definition = GetVisitedAllyCamp();
		if (definition == null || definition.Ally.BirdNameId != birdNameId || ClientInfo.CurrentCampBirds == null)
		{
			return null;
		}
		return ClientInfo.CurrentCampBirds.FirstOrDefault(b => b.BalancingData.NameId == birdNameId);
	}

	// Same stats the friend-bird window lends this ally with.
	private static BirdData BuildAllyBird(ShowcaseCampDefinition definition)
	{
		var level = definition.Ally.LevelFromPlayerLevel(DIContainerInfrastructure.GetCurrentPlayer().Data.Level);
		var bird = new BirdGameData(definition.Ally.BirdNameId, level);
		bird.IsNPC = true;
		bird.ClassItem.Data.Level = GetAllyMastery();
		return ApplyAllyLoadout(bird.Data);
	}

	private static int GetAllyMastery()
	{
		var total = 0f;
		var count = 0;
		foreach (var bird in DIContainerInfrastructure.GetCurrentPlayer().AllBirds)
		{
			total += bird.ClassItem.Data.Level;
			count++;
		}
		var average = count == 0 ? 1 : (int)Math.Round(total / count, MidpointRounding.AwayFromZero);
		return Math.Max(1, average - 1);
	}

	// Equips the player's chosen Main Hand / Off Hand on any NPC ally bird that belongs to a showcase.
	public static BirdData ApplyAllyLoadout(BirdData birdData)
	{
		ShowcaseCampDefinition definition;
		if (birdData == null || !TryGetAlly(birdData.NameId, out definition))
		{
			return birdData;
		}
		var bird = new BirdGameData(birdData);
		EquipAlly(bird, InventoryItemType.MainHandEquipment, GetAllyEquipment(definition, InventoryItemType.MainHandEquipment));
		EquipAlly(bird, InventoryItemType.OffHandEquipment, GetAllyEquipment(definition, InventoryItemType.OffHandEquipment));
		return bird.Data;
	}

	private static void EquipAlly(BirdGameData bird, InventoryItemType type, string nameId)
	{
		if (string.IsNullOrEmpty(nameId))
		{
			return;
		}
		var item = CreateEquipment(nameId, bird.Level);
		if (item != null)
		{
			DIContainerLogic.InventoryService.EquipBirdWithItem(new List<IInventoryItemGameData> { item }, type, bird.InventoryGameData);
		}
	}

	private static IInventoryItemGameData CreateEquipment(string nameId, int level)
	{
		var item = DIContainerLogic.InventoryService.GenerateNewInventoryItemGameData(null, level, 1, nameId, 1, EquipmentSource.LootBird);
		if (item != null)
		{
			item.ItemData.IsNew = false;
		}
		return item;
	}

	// Every equipment item restricted to the ally bird, so new entries in EquipmentBalancingData show up automatically.
	private static List<EquipmentBalancingData> GetAllyEquipmentOptions(ShowcaseCampDefinition definition, InventoryItemType type)
	{
		return DIContainerBalancing.Service.GetBalancingDataList<EquipmentBalancingData>()
			.Where(e => e.ItemType == type && e.RestrictedBirdId == definition.Ally.BirdNameId)
			.ToList();
	}

	// The item list the equipment screen offers for this ally.
	public static InventoryGameData CreateAllyArmory(ShowcaseCampDefinition definition, int level)
	{
		var armory = new InventoryGameData(DIContainerBalancing.Service.GetBalancingData<BirdBalancingData>(definition.Ally.BirdNameId).DefaultInventoryNameId);
		foreach (var type in new[] { InventoryItemType.MainHandEquipment, InventoryItemType.OffHandEquipment })
		{
			foreach (var option in GetAllyEquipmentOptions(definition, type))
			{
				armory.AddNewItemToInventory(CreateEquipment(option.NameId, level));
			}
		}
		return armory;
	}

	private static string GetLoadoutKey(ShowcaseCampDefinition definition, InventoryItemType type)
	{
		return "showcase:" + definition.Id + ":" + type;
	}

	private static string GetAllyEquipment(ShowcaseCampDefinition definition, InventoryItemType type)
	{
		var fallback = type == InventoryItemType.MainHandEquipment ? definition.Ally.DefaultMainHand : definition.Ally.DefaultOffHand;
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		string saved;
		if (player == null || player.Data.EquippedSkins == null || !player.Data.EquippedSkins.TryGetValue(GetLoadoutKey(definition, type), out saved))
		{
			return fallback;
		}
		// Ignore a saved item that was removed from the balancing data or no longer fits this ally.
		return GetAllyEquipmentOptions(definition, type).Any(e => e.NameId == saved) ? saved : fallback;
	}

	// The choice is stored in the player's EquippedSkins map under a "showcase:" key that no class id uses, so it saves with the profile.
	public static void SaveAllyLoadout(ShowcaseCampDefinition definition, BirdGameData bird)
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		if (player.Data.EquippedSkins == null)
		{
			player.Data.EquippedSkins = new Dictionary<string, string>();
		}
		if (bird.MainHandItem != null)
		{
			player.Data.EquippedSkins[GetLoadoutKey(definition, InventoryItemType.MainHandEquipment)] = bird.MainHandItem.BalancingData.NameId;
		}
		if (bird.OffHandItem != null)
		{
			player.Data.EquippedSkins[GetLoadoutKey(definition, InventoryItemType.OffHandEquipment)] = bird.OffHandItem.BalancingData.NameId;
		}
	}
}
