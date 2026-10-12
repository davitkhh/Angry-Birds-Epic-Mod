using System;
using System.Collections.Generic;
using Chimera.Library.Components.Interfaces;
using ProtoBuf;

namespace ABH.Shared.BalancingData
{
	// One upgrade blueprint, for equipment or class items. NameId is the CraftingRecipeBalancingData NameId of the blueprint item the
	// player owns; its ResultLoot only picks the equipment drawn on the blueprint. Each use costs the
	// ResourceCostPerLevelBalancingData row with AppliedItemNameId = NameId and Level = use number.
	// Targets: an item matches when every non-empty filter matches (lists match any entry).
	// Effects are per use and scaled by (1 + 10% per star) of that use.
	[ProtoContract]
	public class UpgradeBlueprintBalancingData : IBalancingData
	{
		[ProtoMember(1)]
		public string NameId { get; set; }

		[ProtoMember(2)]
		public string LocaBaseId { get; set; }

		[ProtoMember(3)]
		public int Uses { get; set; }

		[ProtoMember(4)]
		public List<string> TargetNameIds { get; set; }

		[ProtoMember(5)]
		public List<string> TargetNameIdPrefixes { get; set; }

		[ProtoMember(6)]
		public string TargetBirdId { get; set; }

		[ProtoMember(7)]
		public string TargetItemType { get; set; }

		[ProtoMember(8)]
		public float FlatStat { get; set; }

		[ProtoMember(9)]
		public float StatPercent { get; set; }

		[ProtoMember(10)]
		public float PerkValuePercent { get; set; }

		[ProtoMember(11)]
		public float PerkChanceBonus { get; set; }

		[ProtoMember(12)]
		public float SetPerkValue { get; set; }

		[ProtoMember(13)]
		public float SetPerkValue2 { get; set; }

		[ProtoMember(14)]
		public string AddPerkType { get; set; }

		[ProtoMember(15)]
		public float AddPerkChance { get; set; }

		[ProtoMember(16)]
		public float AddPerkValue { get; set; }

		[ProtoMember(17)]
		public float AddPerkValue2 { get; set; }

		// Percent added to the attack / health of the bird using the upgraded class or item
		[ProtoMember(18)]
		public float AttackPercent { get; set; }

		[ProtoMember(19)]
		public float HealthPercent { get; set; }
	}
}
