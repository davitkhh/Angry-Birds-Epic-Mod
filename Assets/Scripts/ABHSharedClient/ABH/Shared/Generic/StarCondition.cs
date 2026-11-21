using System;

namespace ABH.Shared.Generic
{
	// What a battle star asks for. Regular = the usual score threshold for that star's position.
	// Every other type replaces that one star's score requirement with a condition checked during/after the battle.
	public enum StarConditionType
	{
		Regular,
		// Enter the battle with at most Value birds (friend birds count)
		OnlyXBirds
	}

	// One star of a battle (BattleBalancingData.Stars, index 0 = first star).
	// Value / NameId are the condition's parameters; what they mean depends on Type.
	public class StarCondition
	{
		public StarConditionType Type { get; set; }

		public int Value { get; set; }

		public string NameId { get; set; }
	}
}
