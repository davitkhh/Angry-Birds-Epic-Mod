using System.Linq;
using ABH.GameDatas;
using ABH.GameDatas.Battle.Skills;

// Main hand + off hand pairs that grant a set skill when both are equipped, for birds outside the gacha set system.
// The vanilla sets (EquipmentBalancingData.CorrespondingSetItemId / SetItemSkill) are read straight from the balancing
// by the gacha, set fusion and the set preview popup, which all assume the five main birds, so these pairs are listed
// here instead. EquipmentGameData.IsSetItem / CorrespondingSetItem / SetItemSkill fall back to this list, so the items
// look and work like any other set (set frame and info panel, set skill run by BattleService.AddSetItemPassiveEffects).
public static class CustomEquipmentSets
{
	private class Definition
	{
		public string MainHand;

		public string OffHand;

		public string SkillNameId;
	}

	private static readonly Definition[] Sets =
	{
		// Piggy McCool: Dynamite + Pyrobolist powder keg = Glass Cannon
		new Definition { MainHand = "bird_weapon_merchant_dynamite", OffHand = "bird_offhand_merchant_pyrobolist", SkillNameId = "bird_merchant_set_bonus_glasscannon" }
	};

	// The other half of the pair the item belongs to, or null.
	public static string GetPartnerNameId(string itemNameId)
	{
		var set = Sets.FirstOrDefault(s => s.MainHand == itemNameId || s.OffHand == itemNameId);
		if (set == null)
		{
			return null;
		}
		return set.MainHand == itemNameId ? set.OffHand : set.MainHand;
	}

	// The set skill of the pair the item belongs to, whether or not the pair is complete.
	public static string GetSetSkillNameId(string itemNameId)
	{
		var set = Sets.FirstOrDefault(s => s.MainHand == itemNameId || s.OffHand == itemNameId);
		return set == null ? null : set.SkillNameId;
	}

	public static string GetSetSkillNameId(BirdGameData bird)
	{
		if (bird == null || bird.MainHandItem == null || bird.OffHandItem == null)
		{
			return null;
		}
		var mainHand = bird.MainHandItem.BalancingData.NameId;
		var offHand = bird.OffHandItem.BalancingData.NameId;
		var set = Sets.FirstOrDefault(s => s.MainHand == mainHand && s.OffHand == offHand);
		return set == null ? null : set.SkillNameId;
	}

	public static SkillGameData GetSetSkill(BirdGameData bird)
	{
		var nameId = GetSetSkillNameId(bird);
		return string.IsNullOrEmpty(nameId) ? null : new SkillGameData(nameId);
	}

	public static GlassCannon GetGlassCannon(BirdGameData bird)
	{
		var skill = GetSetSkill(bird);
		return skill == null ? null : skill.GenerateSkillBattleData() as GlassCannon;
	}
}
