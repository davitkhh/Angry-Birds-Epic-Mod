// Story choices switched in code. ActionTree Branch nodes read them by name (their secondaryText).
public static class StoryToggles
{
	public const string PorkyStealsMcCoolBagName = "PorkyStealsMcCoolBag";

	// Prince Porky orders a rogue to steal McCool's bag in the cutscene after hotspot 008 (WS_03_McCoolShopOpen_Sequence).
	// Off: the robbery is skipped, McCool keeps his bag, holds it at his shop and can still wear it as gear.
	public static bool PorkyStealsMcCoolBag = true;

	// The bag: McCool's Phat Loot off hand.
	public const string McCoolBagNameId = "bird_offhand_merchant_phatloot";

	public static bool IsOn(string name)
	{
		switch (name)
		{
			case PorkyStealsMcCoolBagName:
				return PorkyStealsMcCoolBag;
		}
		DebugLog.Warn("StoryToggles: unknown toggle \"" + name + "\", treated as on");
		return true;
	}

	// McCool only joins the player after this cutscene (when his shop unlocks), so with the robbery on he never has the bag.
	public static bool IsMcCoolBagStolen
	{
		get
		{
			return PorkyStealsMcCoolBag;
		}
	}
}
