using System.Collections.Generic;
using ABH.GameDatas;
using ABH.Shared.Generic;

// Story choices switched in code. ActionTree Branch nodes read them by name (their secondaryText).
public static class StoryToggles
{
	public const string PorkyStealsMcCoolBagName = "PorkyStealsMcCoolBag";

	// Master switch for the robbery in McCool's cutscene after hotspot 008 (WS_03_McCoolShopOpen_Sequence). Off: it never
	// happens. On: it happens only if Prince Porky's Cobalt Plateaus miniboss (hotspot 010, battle_009) isn't beaten yet when
	// the cutscene plays; 008 is an optional side level, so the player can beat Porky first and McCool keeps his bag.
	public static bool PorkyStealsMcCoolBag = true;

	// The bag: McCool's Phat Loot off hand.
	public const string McCoolBagNameId = "bird_offhand_merchant_phatloot";

	public const string PorkyMinibossHotspotId = "hotspot_010_battleground";

	public const string McCoolMerchantHotspotId = "hotspot_008_02_merchant";

	// The outcome, fixed when the cutscene's Branch node asks and saved with the profile ("stolen" / "kept"), so beating
	// Porky afterwards doesn't bring the bag back. Delete the key to have the cutscene decide again.
	public const string McCoolBagSaveKey = "story:mccool_bag";

	public static bool IsOn(string name)
	{
		switch (name)
		{
			case PorkyStealsMcCoolBagName:
				return IsMcCoolBagStolen;
		}
		DebugLog.Warn("StoryToggles: unknown toggle \"" + name + "\", treated as on");
		return true;
	}

	// Called by ActionTree Branch nodes: story toggles that depend on progress are decided (and saved) here.
	public static bool Decide(string name)
	{
		switch (name)
		{
			case PorkyStealsMcCoolBagName:
			{
				var stolen = PorkyStealsMcCoolBag && !IsHotspotBeaten(PorkyMinibossHotspotId);
				Save(McCoolBagSaveKey, stolen ? "stolen" : "kept");
				return stolen;
			}
		}
		return IsOn(name);
	}

	// McCool only joins the player after the cutscene (when his shop unlocks).
	public static bool IsMcCoolBagStolen
	{
		get
		{
			var saved = Load(McCoolBagSaveKey);
			if (saved != null)
			{
				return saved == "stolen";
			}
			// Saves from before the outcome was stored saw the robbery whenever the switch was on.
			if (IsHotspotUnlocked(McCoolMerchantHotspotId))
			{
				Save(McCoolBagSaveKey, PorkyStealsMcCoolBag ? "stolen" : "kept");
				return PorkyStealsMcCoolBag;
			}
			return PorkyStealsMcCoolBag && !IsHotspotBeaten(PorkyMinibossHotspotId);
		}
	}

	private static HotspotGameData GetHotspot(string hotspotId)
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		if (player == null || player.WorldGameData == null || player.WorldGameData.HotspotGameDatas == null)
		{
			return null;
		}
		HotspotGameData hotspot;
		return player.WorldGameData.HotspotGameDatas.TryGetValue(hotspotId, out hotspot) ? hotspot : null;
	}

	private static bool IsHotspotBeaten(string hotspotId)
	{
		var hotspot = GetHotspot(hotspotId);
		return hotspot != null && hotspot.Data != null && hotspot.Data.UnlockState >= HotspotUnlockState.ResolvedNew;
	}

	private static bool IsHotspotUnlocked(string hotspotId)
	{
		var hotspot = GetHotspot(hotspotId);
		return hotspot != null && hotspot.Data != null && hotspot.Data.UnlockState >= HotspotUnlockState.Active;
	}

	private static string Load(string key)
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		string value;
		if (player == null || player.Data == null || player.Data.EquippedSkins == null || !player.Data.EquippedSkins.TryGetValue(key, out value))
		{
			return null;
		}
		return value;
	}

	private static void Save(string key, string value)
	{
		var player = DIContainerInfrastructure.GetCurrentPlayer();
		if (player == null || player.Data == null)
		{
			return;
		}
		if (player.Data.EquippedSkins == null)
		{
			player.Data.EquippedSkins = new Dictionary<string, string>();
		}
		player.Data.EquippedSkins[key] = value;
	}
}
