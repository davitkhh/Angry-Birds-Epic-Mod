using System;

namespace ABH.Shared.Generic
{
	public enum SkillTargetTypes
	{
		Attack = 1,
		Support,
		Passive,
		Environmental,
		SetPassive,
		SetHit,
		// Combo moves (ComboMove template): played like attacks, resolved together at the end of the side's turn.
		Combo
	}
}
