using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.Shared.Generic;

// The pirate story chain. Hotspots 028, 006_01, 020_01, 017_01 and 017_02 are ordinary side levels (their *_early battle)
// as soon as the hotspot before them on the map is beaten. The pirates take them over one at a time in story order: a
// hotspot's pirate battle opens once the previous pirate battle is won (028's once 027 is beaten), and the hotspot then
// shows as unbeaten again until that pirate battle is won. For the story (cutscenes, story states, hotspot requirements,
// the ship chapter music, world progress) these hotspots only count as beaten once their pirate battle is won.
public static class PirateChain
{
	private class Link
	{
		public string HotspotId;

		// Every battle that is the pirate fight itself (first try, retry after a loss).
		public string[] PirateBattles;

		// Replay of the pirate fight once it is won.
		public string ResolvedBattle;
	}

	// The hotspot whose completion brings the pirates to the first link.
	public const string ArrivalHotspotId = "hotspot_027_battleground";

	private static readonly Link[] s_Chain = new Link[]
	{
		new Link { HotspotId = "hotspot_028_battleground", PirateBattles = new[] { "battle_024" }, ResolvedBattle = "battle_024_resolved" },
		new Link { HotspotId = "hotspot_006_01_battleground", PirateBattles = new[] { "battle_005_1", "battle_005_1_retry" }, ResolvedBattle = "battle_005_1_resolved" },
		new Link { HotspotId = "hotspot_020_01_battleground", PirateBattles = new[] { "battle_017_1" }, ResolvedBattle = "battle_017_1_resolved" },
		new Link { HotspotId = "hotspot_017_01_battleground", PirateBattles = new[] { "battle_014_1" }, ResolvedBattle = "battle_014_1_resolved" },
		new Link { HotspotId = "hotspot_017_02_battleground", PirateBattles = new[] { "battle_014_2" }, ResolvedBattle = "battle_014_2_resolved" }
	};

	// Saved in the player's EquippedSkins map like other mod state ("pirate:won:<hotspot>").
	private const string WonKeyPrefix = "pirate:won:";

	private const string MigratedKey = "pirate:migrated";

	public static bool IsChainHotspot(string hotspotId)
	{
		return GetLink(hotspotId) != null;
	}

	public static bool IsPirateWon(PlayerGameData player, string hotspotId)
	{
		string value;
		return player != null && player.Data.EquippedSkins != null && player.Data.EquippedSkins.TryGetValue(WonKeyPrefix + hotspotId, out value) && value == "1";
	}

	// The pirates have reached this hotspot: the previous pirate battle is won (or, for the first, 027 is beaten).
	public static bool HavePiratesArrived(PlayerGameData player, string hotspotId)
	{
		var index = IndexOf(hotspotId);
		if (index < 0 || player == null)
		{
			return false;
		}
		if (index == 0)
		{
			HotspotGameData arrival;
			return player.WorldGameData != null && player.WorldGameData.HotspotGameDatas.TryGetValue(ArrivalHotspotId, out arrival) && arrival.Data.UnlockState >= HotspotUnlockState.ResolvedNew;
		}
		return IsPirateWon(player, s_Chain[index - 1].HotspotId);
	}

	public static bool IsPirateActive(PlayerGameData player, string hotspotId)
	{
		return HavePiratesArrived(player, hotspotId) && !IsPirateWon(player, hotspotId);
	}

	// The hotspot's state as far as the story is concerned: a chain hotspot whose pirate battle is not won yet is at
	// most Active, whatever its early level did.
	public static HotspotUnlockState StoryState(PlayerGameData player, HotspotGameData hotspot)
	{
		var state = hotspot.Data.UnlockState;
		if (state >= HotspotUnlockState.ResolvedNew && IsChainHotspot(hotspot.BalancingData.NameId) && !IsPirateWon(player, hotspot.BalancingData.NameId))
		{
			return HotspotUnlockState.Active;
		}
		return state;
	}

	public static HotspotUnlockState StoryState(HotspotGameData hotspot)
	{
		return StoryState(DIContainerInfrastructure.GetCurrentPlayer(), hotspot);
	}

	// Battle selection (BattleService.GetFirstPossibleBattle): the pirate fight only while the pirates are at the hotspot,
	// its replay only once won; any other battle (the early level) is not restricted here.
	public static bool IsBattleAllowed(PlayerGameData player, string battleId)
	{
		foreach (var link in s_Chain)
		{
			if (link.PirateBattles.Contains(battleId))
			{
				return IsPirateActive(player, link.HotspotId);
			}
			if (link.ResolvedBattle == battleId)
			{
				return IsPirateWon(player, link.HotspotId);
			}
		}
		return true;
	}

	// A battle on a world map hotspot was won. Returns whether it counts for the story (always, except an early level
	// on a chain hotspot whose pirate battle is not won).
	public static bool OnBattleWon(PlayerGameData player, HotspotGameData hotspot, string battleId)
	{
		var link = GetLink(hotspot.BalancingData.NameId);
		if (link == null)
		{
			return true;
		}
		if (link.PirateBattles.Contains(battleId) && !IsPirateWon(player, link.HotspotId))
		{
			SetWon(player, link.HotspotId);
		}
		return IsPirateWon(player, link.HotspotId);
	}

	// Brings saves up to date: profiles from before the chain (a beaten chain hotspot was its pirate battle) count those
	// as won once, and a beaten hotspot the pirates have reached goes back to an unbeaten level.
	public static void Sync(PlayerGameData player)
	{
		if (player == null || player.WorldGameData == null || player.WorldGameData.HotspotGameDatas == null)
		{
			return;
		}
		if (player.Data.EquippedSkins == null)
		{
			player.Data.EquippedSkins = new Dictionary<string, string>();
		}
		if (!player.Data.EquippedSkins.ContainsKey(MigratedKey))
		{
			foreach (var link in s_Chain)
			{
				HotspotGameData hotspot;
				if (player.WorldGameData.HotspotGameDatas.TryGetValue(link.HotspotId, out hotspot) && hotspot.Data.UnlockState >= HotspotUnlockState.ResolvedNew)
				{
					SetWon(player, link.HotspotId);
				}
			}
			player.Data.EquippedSkins[MigratedKey] = "1";
		}
		foreach (var link in s_Chain)
		{
			HotspotGameData hotspot;
			if (IsPirateActive(player, link.HotspotId) && player.WorldGameData.HotspotGameDatas.TryGetValue(link.HotspotId, out hotspot) && hotspot.Data.UnlockState >= HotspotUnlockState.ResolvedNew)
			{
				hotspot.Data.UnlockState = HotspotUnlockState.Active;
				hotspot.RaiseHotspotChanged();
			}
		}
	}

	private static void SetWon(PlayerGameData player, string hotspotId)
	{
		if (player.Data.EquippedSkins == null)
		{
			player.Data.EquippedSkins = new Dictionary<string, string>();
		}
		player.Data.EquippedSkins[WonKeyPrefix + hotspotId] = "1";
	}

	private static int IndexOf(string hotspotId)
	{
		for (var i = 0; i < s_Chain.Length; i++)
		{
			if (s_Chain[i].HotspotId == hotspotId)
			{
				return i;
			}
		}
		return -1;
	}

	private static Link GetLink(string hotspotId)
	{
		var index = IndexOf(hotspotId);
		return index < 0 ? null : s_Chain[index];
	}
}
