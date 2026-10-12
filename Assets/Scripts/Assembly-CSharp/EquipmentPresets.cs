using System;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;

public class EquipmentPresetBird
{
	public string BirdNameId;

	public string ClassNameId;

	public string MainHandNameId;

	public string OffHandNameId;

	public string Get(InventoryItemType type)
	{
		switch (type)
		{
		case InventoryItemType.Class:
			return ClassNameId;
		case InventoryItemType.MainHandEquipment:
			return MainHandNameId;
		case InventoryItemType.OffHandEquipment:
			return OffHandNameId;
		default:
			return null;
		}
	}
}

public class EquipmentPreset
{
	public string Name;

	public List<EquipmentPresetBird> Birds = new List<EquipmentPresetBird>();
}

// Player-made equipment loadouts (class, main hand, off hand per bird).
// Stored in PlayerData.EquippedSkins under "preset:<index>" keys, which no class id uses, so they save with the profile
// without touching the protobuf schema (same trick as ShowcaseCamps).
public static class EquipmentPresets
{
	public const int MaxPresets = 12;

	public const int MaxNameLength = 32;

	public static readonly InventoryItemType[] Slots = { InventoryItemType.Class, InventoryItemType.MainHandEquipment, InventoryItemType.OffHandEquipment };

	private const string KeyPrefix = "preset:";

	public static List<EquipmentPreset> Load()
	{
		var result = new List<EquipmentPreset>();
		var store = GetStore();
		if (store == null)
		{
			return result;
		}
		string value;
		for (var i = 0; store.TryGetValue(KeyPrefix + i, out value); i++)
		{
			var preset = Parse(value);
			if (preset != null)
			{
				result.Add(preset);
			}
		}
		return result;
	}

	public static void Save(List<EquipmentPreset> presets)
	{
		var store = GetStore();
		if (store == null)
		{
			return;
		}
		foreach (var key in store.Keys.Where(k => k.StartsWith(KeyPrefix)).ToList())
		{
			store.Remove(key);
		}
		for (var i = 0; i < presets.Count && i < MaxPresets; i++)
		{
			store[KeyPrefix + i] = Serialize(presets[i]);
		}
		DIContainerInfrastructure.GetCurrentPlayer().SavePlayerData();
	}

	// Snapshot of what the given birds are wearing right now.
	public static EquipmentPreset Capture(string name, IEnumerable<BirdGameData> birds)
	{
		var preset = new EquipmentPreset { Name = SanitizeName(name) };
		foreach (var bird in birds)
		{
			preset.Birds.Add(new EquipmentPresetBird
			{
				BirdNameId = bird.BalancingData.NameId,
				ClassNameId = ItemRef(bird.ClassItem),
				MainHandNameId = ItemRef(bird.MainHandItem),
				OffHandNameId = ItemRef(bird.OffHandItem)
			});
		}
		return preset;
	}

	// Equips every piece of the preset the player still owns. Returns how many pieces were missing (scrapped/sold).
	public static int Apply(EquipmentPreset preset)
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		var missing = 0;
		foreach (var entry in preset.Birds)
		{
			var bird = FindBird(entry.BirdNameId);
			if (bird == null)
			{
				continue;
			}
			foreach (var type in Slots)
			{
				var itemRef = entry.Get(type);
				if (string.IsNullOrEmpty(itemRef) || IsEquipped(bird, type, itemRef))
				{
					continue;
				}
				var item = FindOwnedItem(itemRef, bird, type);
				if (item == null)
				{
					missing++;
					continue;
				}
				DIContainerLogic.InventoryService.EquipBirdWithItem(new List<IInventoryItemGameData> { item }, type, bird.InventoryGameData);
			}
		}
		player.SavePlayerData();
		return missing;
	}

	public static BirdGameData FindBird(string birdNameId)
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		return player == null ? null : player.Birds.FirstOrDefault(b => b.BalancingData.NameId == birdNameId);
	}

	// Identifies one owned copy: "nameId:level:quality" for equipment (the player can own several copies of a NameId,
	// and BirdWindowUI tells them apart the same way), just the NameId for classes.
	public static string ItemRef(IInventoryItemGameData item)
	{
		if (item == null)
		{
			return null;
		}
		if (item is EquipmentGameData)
		{
			return item.ItemBalancing.NameId + ":" + item.ItemData.Level + ":" + item.ItemData.Quality;
		}
		return item.ItemBalancing.NameId;
	}

	// Presets saved before level/quality were recorded hold a bare NameId, which matches any copy.
	public static bool Matches(IInventoryItemGameData item, string itemRef)
	{
		if (item == null || string.IsNullOrEmpty(itemRef))
		{
			return false;
		}
		var parts = itemRef.Split(':');
		if (!string.Equals(item.ItemBalancing.NameId, parts[0], StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		int level;
		int quality;
		if (parts.Length >= 3 && int.TryParse(parts[1], out level) && int.TryParse(parts[2], out quality))
		{
			return item.ItemData.Level == level && item.ItemData.Quality == quality;
		}
		return true;
	}

	// The player's own copy of the item, or null if it is gone or no longer fits the bird.
	// Searched directly: InventoryService.TryGetItemGameData always fails for equipment.
	public static IInventoryItemGameData FindOwnedItem(string itemRef, BirdGameData bird, InventoryItemType type)
	{
		List<IInventoryItemGameData> owned;
		if (string.IsNullOrEmpty(itemRef) || !DIContainerInfrastructure.GetCurrentPlayer().InventoryGameData.Items.TryGetValue(type, out owned))
		{
			return null;
		}
		return owned.Where(i => Matches(i, itemRef) && i.IsValidForBird(bird)).OrderByDescending(i => i.ItemMainStat).FirstOrDefault();
	}

	// A throwaway copy of the bird wearing the preset's gear, for previews. Pieces the player no longer owns
	// leave the bird's current item in that slot. The real bird is never touched.
	public static BirdGameData CreatePreviewBird(BirdGameData bird, EquipmentPresetBird entry)
	{
		var preview = new BirdGameData(bird);
		if (entry == null)
		{
			return preview;
		}
		foreach (var type in Slots)
		{
			var item = FindOwnedItem(entry.Get(type), bird, type);
			if (item != null && !IsEquipped(preview, type, ItemRef(item)))
			{
				EquipPreview(preview, type, item);
			}
		}
		return preview;
	}

	public static void EquipPreview(BirdGameData preview, InventoryItemType type, IInventoryItemGameData item)
	{
		DIContainerLogic.InventoryService.EquipBirdWithItem(new List<IInventoryItemGameData> { item }, type, preview.InventoryGameData);
		if (type == InventoryItemType.Class)
		{
			// The copy is not wired to its inventory events, so swap in the class's skin by hand.
			preview.RaiseClassItemChanged();
		}
	}

	// Pieces of the preset the player has scrapped, sold or can no longer use.
	public static int CountMissing(EquipmentPreset preset)
	{
		var missing = 0;
		foreach (var entry in preset.Birds)
		{
			var bird = FindBird(entry.BirdNameId);
			foreach (var type in Slots)
			{
				var itemRef = entry.Get(type);
				if (!string.IsNullOrEmpty(itemRef) && (bird == null || (!IsEquipped(bird, type, itemRef) && FindOwnedItem(itemRef, bird, type) == null)))
				{
					missing++;
				}
			}
		}
		return missing;
	}

	public static EquipmentPresetBird GetEntry(EquipmentPreset preset, string birdNameId)
	{
		return preset == null ? null : preset.Birds.FirstOrDefault(b => b.BirdNameId == birdNameId);
	}

	public static bool IsEquipped(BirdGameData bird, InventoryItemType type, string itemRef)
	{
		IInventoryItemGameData item;
		return TryGetEquipped(bird, type, itemRef, out item);
	}

	public static string SanitizeName(string name)
	{
		var clean = new string((name ?? string.Empty).Where(c => c != '|' && c != ';' && c != '=' && c != ',' && !char.IsControl(c)).ToArray()).Trim();
		if (clean.Length > MaxNameLength)
		{
			clean = clean.Substring(0, MaxNameLength);
		}
		return clean.Length == 0 ? "Preset" : clean;
	}

	private static bool TryGetEquipped(BirdGameData bird, InventoryItemType type, string itemRef, out IInventoryItemGameData item)
	{
		List<IInventoryItemGameData> equipped;
		item = bird.InventoryGameData.Items.TryGetValue(type, out equipped) ? equipped.FirstOrDefault(i => Matches(i, itemRef)) : null;
		return item != null;
	}

	private static Dictionary<string, string> GetStore()
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		if (player == null)
		{
			return null;
		}
		if (player.Data.EquippedSkins == null)
		{
			player.Data.EquippedSkins = new Dictionary<string, string>();
		}
		return player.Data.EquippedSkins;
	}

	// Format: name|bird=class,main,off;bird=class,main,off
	private static string Serialize(EquipmentPreset preset)
	{
		var birds = preset.Birds.Select(b => b.BirdNameId + "=" + b.ClassNameId + "," + b.MainHandNameId + "," + b.OffHandNameId);
		return SanitizeName(preset.Name) + "|" + string.Join(";", birds.ToArray());
	}

	private static EquipmentPreset Parse(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return null;
		}
		var split = value.IndexOf('|');
		var preset = new EquipmentPreset { Name = split < 0 ? value : value.Substring(0, split) };
		if (split < 0)
		{
			return preset;
		}
		foreach (var birdPart in value.Substring(split + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
		{
			var eq = birdPart.IndexOf('=');
			if (eq <= 0)
			{
				continue;
			}
			var items = birdPart.Substring(eq + 1).Split(',');
			preset.Birds.Add(new EquipmentPresetBird
			{
				BirdNameId = birdPart.Substring(0, eq),
				ClassNameId = items.Length > 0 ? NullIfEmpty(items[0]) : null,
				MainHandNameId = items.Length > 1 ? NullIfEmpty(items[1]) : null,
				OffHandNameId = items.Length > 2 ? NullIfEmpty(items[2]) : null
			});
		}
		return preset;
	}

	private static string NullIfEmpty(string s)
	{
		return string.IsNullOrEmpty(s) ? null : s;
	}
}
