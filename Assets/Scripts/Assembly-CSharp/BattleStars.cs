using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Battle;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using UnityEngine;

// Battle stars whose score requirement can be swapped for a condition, per star position
// (BattleBalancingData.Stars, 0 = first star). A Regular star keeps the usual score threshold of its position:
// star N needs N * score-per-star, whatever the other stars are.
public static class BattleStars
{
	public const int Count = 3;

	private static readonly Dictionary<string, Texture2D> s_Icons = new Dictionary<string, Texture2D>();

	public static StarCondition Get(BattleBalancingData balancing, int index)
	{
		if (balancing == null || balancing.Stars == null || index < 0 || index >= balancing.Stars.Count)
		{
			return null;
		}
		var star = balancing.Stars[index];
		return star != null && star.Type != StarConditionType.Regular ? star : null;
	}

	public static bool IsRegular(BattleBalancingData balancing, int index)
	{
		return Get(balancing, index) == null;
	}

	public static bool HasConditions(BattleBalancingData balancing)
	{
		for (var i = 0; i < Count; i++)
		{
			if (Get(balancing, i) != null)
			{
				return true;
			}
		}
		return false;
	}

	public static int BirdsEntered(BattleGameData battle)
	{
		if (battle.m_BirdsEnteredCount > 0)
		{
			return battle.m_BirdsEnteredCount;
		}
		List<ABH.GameDatas.Interfaces.ICombatant> birds;
		if (battle.m_CombatantsPerFaction != null && battle.m_CombatantsPerFaction.TryGetValue(Faction.Birds, out birds))
		{
			return birds.Count(b => b is BirdCombatant && b.summoningType != SummoningType.Summoned);
		}
		return 0;
	}

	public static bool IsMet(StarCondition star, BattleGameData battle)
	{
		switch (star.Type)
		{
		case StarConditionType.OnlyXBirds:
			return BirdsEntered(battle) <= star.Value;
		default:
			return false;
		}
	}

	// Earned flag per star position for a won battle. Nothing is guaranteed: a win can earn 0 stars.
	public static bool[] Evaluate(BattleGameData battle, int scorePerStar)
	{
		var earned = new bool[Count];
		for (var i = 0; i < Count; i++)
		{
			var star = Get(battle.Balancing, i);
			earned[i] = star != null ? IsMet(star, battle) : battle.m_BattleEndData.m_Score >= scorePerStar * (i + 1);
		}
		return earned;
	}

	// Earned flags of a finished battle; older results without flags count the first N stars
	public static bool[] Earned(BattleEndGameData end)
	{
		if (end.m_StarsEarned != null && end.m_StarsEarned.Length == Count)
		{
			return end.m_StarsEarned;
		}
		var earned = new bool[Count];
		for (var i = 0; i < Count; i++)
		{
			earned[i] = i < end.m_BattlePerformanceStars;
		}
		return earned;
	}

	// Icon texture in Resources/UI. onStar = the variant with a dark rim for drawing on top of a star.
	public static string IconName(StarCondition star, bool onStar)
	{
		if (star == null)
		{
			return null;
		}
		string name;
		switch (star.Type)
		{
		case StarConditionType.OnlyXBirds:
			name = "StarReq_OnlyXBirds";
			break;
		default:
			return null;
		}
		return onStar ? name + "_Star" : name;
	}

	public static Texture2D Icon(StarCondition star, bool onStar)
	{
		var name = IconName(star, onStar);
		if (name == null)
		{
			return null;
		}
		Texture2D texture;
		if (!s_Icons.TryGetValue(name, out texture) || texture == null)
		{
			texture = Resources.Load<Texture2D>("UI/" + name);
			s_Icons[name] = texture;
		}
		return texture;
	}

	// Icon baked onto a star sprite's face in its perspective: Resources/UI/<Icon>_On_<Sprite>.png, the sprite's own
	// size (ArtSource/StarConditions/star_faces.py). Desaturated sprites use the coloured sprite's bake.
	public static Texture2D IconOnSprite(StarCondition star, string spriteName)
	{
		var icon = IconName(star, false);
		if (icon == null || string.IsNullOrEmpty(spriteName))
		{
			return null;
		}
		var name = icon + "_On_" + spriteName.Replace("_Desaturated", string.Empty);
		Texture2D texture;
		if (!s_Icons.TryGetValue(name, out texture))
		{
			texture = Resources.Load<Texture2D>("UI/" + name);
			s_Icons[name] = texture;
		}
		return texture;
	}

	public const string NumberColor = "[ff0000]";

	// Score needed for the regular star at a position, given the score per star
	public static int ScoreForStar(int index, int scorePerStar)
	{
		return scorePerStar * (index + 1);
	}

	// Score per star before the battle. Exact for FixedMaximum / PvP. MaximumByStrength depends on the pigs that
	// actually spawn, so it is estimated (estimated = true): weighted waves fill up to the battle's StrengthPoints,
	// IgnoreStrength waves count their entries. Mirrors BattleService.getScorePerStarFromMaximum.
	public static int EstimateScorePerStar(BattleBalancingData balancing, int birds, out bool estimated)
	{
		estimated = false;
		var score = DIContainerBalancing.Service.GetBalancingData<ScoreBalancingData>("default_score");
		if (balancing == null || score == null)
		{
			return 0;
		}
		switch (balancing.ScoringStrategy)
		{
		case ScoringStrategy.FixedMaximum:
			return score.ScorePerStarNeeded;
		case ScoringStrategy.PvP:
			return score.ScorePerStarNeededPvP;
		}
		estimated = true;
		var strength = 0;
		if (balancing.BattleParticipantsIds != null)
		{
			foreach (var tableId in balancing.BattleParticipantsIds)
			{
				BattleParticipantTableBalancingData table;
				if (!DIContainerBalancing.Service.TryGetBalancingData(tableId, out table) || table == null)
				{
					strength += balancing.StrengthPoints;
					continue;
				}
				if (table.Type != BattleParticipantTableType.IgnoreStrength || table.BattleParticipants == null)
				{
					strength += balancing.StrengthPoints;
					continue;
				}
				foreach (var entry in table.BattleParticipants)
				{
					PigBalancingData pig;
					BossBalancingData boss;
					var amount = Mathf.Max(1, Mathf.RoundToInt(entry.Amount));
					if (DIContainerBalancing.Service.TryGetBalancingData(entry.NameId, out pig) && pig != null)
					{
						strength += pig.PigStrength * amount;
					}
					else if (DIContainerBalancing.Service.TryGetBalancingData(entry.NameId, out boss) && boss != null)
					{
						strength += boss.PigStrength * amount;
					}
				}
			}
		}
		return (strength * score.ScorePerStrengthPoint + score.ScorePerBird * Mathf.Max(1, birds)) / 4;
	}

	// Tooltip text with NGUI color codes (the condition's number in red). scoreText: the regular star's score
	// (null = no number known)
	public static string Description(StarCondition star, string scoreText)
	{
		if (star == null && !string.IsNullOrEmpty(scoreText))
		{
			const string scoreFallback = "Reach the score {value_1}.";
			var text = Application.isPlaying ? DIContainerInfrastructure.GetLocaService().Tr("star_condition_regular_score_tt", scoreFallback) : scoreFallback;
			return text.Replace("{value_1}", NumberColor + scoreText + "[-]");
		}
		return Description(star);
	}

	public static string Description(StarCondition star)
	{
		ABHLocaService loca = null;
		if (Application.isPlaying)
		{
			loca = DIContainerInfrastructure.GetLocaService();
		}
		if (star == null)
		{
			return loca != null ? loca.Tr("star_condition_regular_tt", "Reach the score for this star.") : "Reach the score for this star.";
		}
		switch (star.Type)
		{
		case StarConditionType.OnlyXBirds:
		{
			var one = star.Value == 1;
			var key = one ? "star_condition_onlyxbirds_one_tt" : "star_condition_onlyxbirds_tt";
			var fallback = one ? "Enter the battle\nwith only {value_1} bird." : "Enter the battle\nwith only {value_1} birds.";
			var text = loca != null ? loca.Tr(key, fallback) : fallback;
			return text.Replace("{value_1}", NumberColor + star.Value + "[-]");
		}
		default:
			return star.Type.ToString();
		}
	}
}
