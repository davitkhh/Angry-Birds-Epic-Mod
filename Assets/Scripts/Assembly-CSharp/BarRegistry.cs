using System;

[Serializable]
public class BarRegistry
{
	public uint Depth;

	public bool showSnoutlings;

	public bool showFriendshipEssence;

	public bool showLuckyCoins;

	public bool showEnergy;

	// Item NameIds (e.g. resource_cobalt_stone) shown as extra counters next to the currencies.
	public System.Collections.Generic.List<string> showResources;
}
