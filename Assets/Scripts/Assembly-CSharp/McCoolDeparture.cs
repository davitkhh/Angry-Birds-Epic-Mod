using ABH.GameDatas;

// Piggy McCool leaves his stall once the player beats the level right before the one that unlocks Matilda
// (hotspot_022_battleground: battle_019 grants addbird_whitebird). His shop stays open with whatever is left in it, but
// nothing restocks any more (MerchantStock), his stall pig is gone and he no longer peeks over the shop board.
public static class McCoolDeparture
{
	public const string DepartureHotspotNameId = "hotspot_019_battleground";

	public static bool HasLeft(PlayerGameData player)
	{
		HotspotGameData hotspot;
		return player != null && player.WorldGameData != null && player.WorldGameData.HotspotGameDatas != null && player.WorldGameData.HotspotGameDatas.TryGetValue(DepartureHotspotNameId, out hotspot) && hotspot != null && hotspot.IsCompleted();
	}

	public static bool HasLeft()
	{
		return HasLeft(DIContainerInfrastructure.GetCurrentPlayer());
	}
}
