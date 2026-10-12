using System.Collections.Generic;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using ABH.Shared.Models.Generic;

namespace ABH.GameDatas.Battle
{
	// A swap crystal in the pig formation (see SwapCrystalConfig). PlaceCharacter places it like a pig and gives it an
	// initiative from its position, but it never participates: no health bar, no targeting, it doesn't hold up the
	// wave and it's left out of the initiative lists. SwapCrystalDirector takes its turn where its initiative falls
	// among the pigs. Negative base health makes it unkillable even by effects that hit every pig.
	public class SwapCrystalCombatant : PigCombatant
	{
		public const string AssetId = "SwapCrystal";

		public SwapCrystalConfig Config { get; private set; }

		public SwapCrystalCombatant(SwapCrystalConfig config, int level)
			: base(new PigGameData(CreateBalancing(config), level))
		{
			Config = config;
			IsParticipating = false;
			Entered = true;
		}

		private static PigBalancingData CreateBalancing(SwapCrystalConfig config)
		{
			return new PigBalancingData
			{
				NameId = config.NameId,
				AssetId = AssetId,
				LocaId = config.NameId,
				BaseHealth = -1,
				SkillNameIds = new List<string>(),
				SizeType = CharacterSizeType.Medium,
				SizeScale = config.Scale,
				Faction = Faction.Pigs,
				LootTableDefeatBonus = new Dictionary<string, int>(),
				PigStrength = 0,
				IgnoreDifficulty = true
			};
		}
	}
}
