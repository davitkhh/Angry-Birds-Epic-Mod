using System.Collections.Generic;
using ABH.GameDatas.Battle;
using ABH.GameDatas.Interfaces;
using ABH.Shared.BalancingData;
using ABH.Shared.Models.Generic;

// A swap crystal: the red/blue crystal on its podium that stands in the pig formation without fighting. On every
// "turns"-th of its turns it switches its primary color, and every pig of one of its two assigned types becomes the
// other type, keeping its max and current health (see SwapCrystalDirector).
//
// Placing one: add a battle participant entry named "crystal_<anything>" to a wave's participant table, among the
// pigs; it takes a formation slot where it's listed (Amount = how many, Probability and LevelDifference are ignored).
// It acts when its slot's turn comes. It's configured by the SkillBalancingData with the same NameId:
//   TargetCulling   - the two pig NameIds it swaps between
//   SkillParameters - "turns": swaps on every this-many of its own turns (default 2)
//                     "start_color": 0 = red primary (default), 1 = blue primary
//                     "scale": size multiplier (default 1)
public class SwapCrystalConfig
{
	public const string EntryPrefix = "crystal_";

	private const string AssetPath = "Prefabs/0_Generic/3_SwapCrystal/SwapCrystal";

	public string NameId;

	public string[] PigNameIds;

	public int Turns = 2;

	public bool StartBlue;

	public float Scale = 1f;

	public static bool IsCrystalEntry(string nameId)
	{
		return nameId != null && nameId.StartsWith(EntryPrefix);
	}

	public static SwapCrystalConfig Get(string nameId)
	{
		var skill = DIContainerBalancing.Service.GetBalancingData<SkillBalancingData>(nameId);
		if (skill == null || skill.TargetCulling == null || skill.TargetCulling.Count < 2)
		{
			DebugLog.Error("[SwapCrystal] " + nameId + " needs a SkillBalancingData entry with the two pig NameIds in TargetCulling.");
			return null;
		}
		var config = new SwapCrystalConfig
		{
			NameId = nameId,
			PigNameIds = new[] { skill.TargetCulling[0], skill.TargetCulling[1] }
		};
		var parameters = skill.SkillParameters ?? new Dictionary<string, float>();
		float value;
		if (parameters.TryGetValue("turns", out value))
		{
			config.Turns = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(value));
		}
		if (parameters.TryGetValue("start_color", out value))
		{
			config.StartBlue = value > 0f;
		}
		if (parameters.TryGetValue("scale", out value) && value > 0f)
		{
			config.Scale = value;
		}
		foreach (var pigNameId in config.PigNameIds)
		{
			if (DIContainerBalancing.Service.GetBalancingData<PigBalancingData>(pigNameId) == null)
			{
				DebugLog.Error("[SwapCrystal] " + nameId + " lists unknown pig " + pigNameId + "; it won't swap.");
				return null;
			}
		}
		return config;
	}

	// The pig type a pig of this crystal becomes, or null if the crystal doesn't swap it.
	public string GetSwapTarget(string pigNameId)
	{
		if (pigNameId == PigNameIds[0])
		{
			return PigNameIds[1];
		}
		if (pigNameId == PigNameIds[1])
		{
			return PigNameIds[0];
		}
		return null;
	}

	// The pig generators would treat crystal entries as pigs, so they get the table without them.
	public static BattleParticipantTableBalancingData WithoutCrystals(BattleParticipantTableBalancingData table)
	{
		if (table == null || table.BattleParticipants == null || !table.BattleParticipants.Exists(e => IsCrystalEntry(e.NameId)))
		{
			return table;
		}
		return new BattleParticipantTableBalancingData
		{
			NameId = table.NameId,
			Type = table.Type,
			VictoryCondition = table.VictoryCondition,
			BattleParticipants = table.BattleParticipants.FindAll(e => !IsCrystalEntry(e.NameId))
		};
	}

	// Adds the table's crystals to the generated wave at their listed position among the pigs (PlaceCharacter fills
	// the formation in list order). firstPigIndex: where this table's pigs start in combatants.
	public static void InsertCrystals(List<ICombatant> combatants, int firstPigIndex, BattleParticipantTableBalancingData table, int level)
	{
		if (table == null || table.BattleParticipants == null)
		{
			return;
		}
		var index = firstPigIndex;
		foreach (var entry in table.BattleParticipants)
		{
			var amount = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(entry.Amount));
			if (!IsCrystalEntry(entry.NameId))
			{
				index += amount;
				continue;
			}
			var config = Get(entry.NameId);
			if (config == null)
			{
				continue;
			}
			EnsureAssetRegistered();
			for (var i = 0; i < amount; i++)
			{
				index = UnityEngine.Mathf.Min(index, combatants.Count);
				combatants.Insert(index++, new SwapCrystalCombatant(config, level));
			}
		}
	}

	// The character asset providers only know the assets listed in Root.unity; the crystal is added at runtime.
	private static void EnsureAssetRegistered()
	{
		foreach (var provider in new[] { DIContainerInfrastructure.GetCharacterAssetProvider(false), DIContainerInfrastructure.GetCharacterAssetProvider(true) })
		{
			var key = SwapCrystalCombatant.AssetId.ToLower();
			if (provider != null && !provider.UsedAssets.ContainsKey(key))
			{
				provider.UsedAssets.Add(key, new AssetLoadingInfo
				{
					AssetLoadingType = LoadingType.FromResources,
					LastRequestTime = System.DateTime.MinValue,
					LoadPath = AssetPath
				});
			}
		}
	}
}
