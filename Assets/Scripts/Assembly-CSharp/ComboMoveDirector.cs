using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Battle.Skills;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

// Combo moves (ComboMove, TargetType Combo). A combatant that plays one on its turn signs up here. When its side's turn
// ends (BattleMgr.DoNextStep: pigs' combos as the birds' turn starts, birds' combos as the pigs' turn starts), every
// combo move that side played this turn adds up to one ComboRecipe in which every comboer takes part.
//
// Matching: the recipe for all of the moves together ("combo_" + sorted combo ids). If there is none, the largest
// group that has a recipe goes first (earliest joiners preferred), then the rest the same way, so three Spins with only
// combo_spin_spin and combo_spin defined become Spin + Spin, then Spin. Moves no recipe covers fizzle (logged).
// Comboers that were knocked out, removed or stunned before their combo goes off drop out of it.
// Added to the battle manager on first use.
public class ComboMoveDirector : MonoBehaviour
{
	// Bigger groups are never tried (combinations grow fast; no recipe needs more).
	private const int MaxComboSize = 6;

	// The "Combo: <name>" label shows this long before the ability starts.
	private const float LabelLeadTime = 0.6f;

	private const float TimeBetweenCombos = 0.3f;

	private class Entry
	{
		public ICombatant Combatant;

		public ComboMove Move;

		public int Wave;
	}

	private BattleMgrBase m_BattleMgr;

	private readonly List<Entry> m_Pending = new List<Entry>();

	// True when the last Resolve played at least one combo (the caller then checks for the battle's end).
	public bool LastResolveActed { get; private set; }

	public static ComboMoveDirector For(BattleMgrBase battleMgr)
	{
		var director = battleMgr.GetComponent<ComboMoveDirector>();
		if (director == null)
		{
			director = battleMgr.gameObject.AddComponent<ComboMoveDirector>();
			director.m_BattleMgr = battleMgr;
		}
		return director;
	}

	public void Join(ICombatant combatant, ComboMove move)
	{
		// An extra turn's second combo move replaces the first: one contribution per combatant.
		m_Pending.RemoveAll(e => e.Combatant == combatant);
		m_Pending.Add(new Entry
		{
			Combatant = combatant,
			Move = move,
			Wave = m_BattleMgr.Model.CurrentWaveIndex
		});
	}

	public bool HasPending(Faction faction)
	{
		return m_Pending.Any(e => e.Combatant.CombatantFaction == faction);
	}

	public IEnumerator Resolve(Faction faction)
	{
		LastResolveActed = false;
		var battle = m_BattleMgr.Model;
		var entries = m_Pending.Where(e => e.Combatant.CombatantFaction == faction).ToList();
		m_Pending.RemoveAll(e => e.Combatant.CombatantFaction == faction);
		// Sign-ups from a wave that already ended don't carry over.
		entries.RemoveAll(e => e.Wave != battle.CurrentWaveIndex);
		while (true)
		{
			entries.RemoveAll(e => !CanTakePart(e.Combatant));
			if (entries.Count == 0)
			{
				yield break;
			}
			ComboRecipe recipe;
			List<Entry> group;
			if (!TryPickGroup(entries, out recipe, out group))
			{
				DebugLog.Log("[ComboMoveDirector] No combo recipe for " + ComboRecipe.KeyFor(entries.Select(e => e.Move.ComboId)) + " or any part of it; those moves fizzle");
				yield break;
			}
			entries.RemoveAll(group.Contains);
			if (LastResolveActed)
			{
				yield return new WaitForSeconds(TimeBetweenCombos);
			}
			LastResolveActed = true;
			var comboers = group.Select(e => e.Combatant).ToList();
			DebugLog.Log("[ComboMoveDirector] " + recipe.Model.SkillNameId + " by " + string.Join(", ", comboers.Select(c => c.CombatantNameId).ToArray()));
			var label = DIContainerInfrastructure.GetLocaService().Tr("combo_label_prefix", "Combo: ") + recipe.GetLocalizedName();
			comboers[0].CombatantView.ShowCombatLabel(label, SkillBlind.ComboColor);
			yield return new WaitForSeconds(LabelLeadTime);
			yield return StartCoroutine(recipe.Perform(battle, comboers, this));
		}
	}

	private static bool CanTakePart(ICombatant combatant)
	{
		return combatant != null && combatant.IsParticipating && combatant.IsAlive && !combatant.IsStunned && combatant.CombatantView != null;
	}

	// The largest group (earliest joiners first) whose combination has a recipe.
	private static bool TryPickGroup(List<Entry> entries, out ComboRecipe recipe, out List<Entry> group)
	{
		for (var size = Mathf.Min(entries.Count, MaxComboSize); size >= 1; size--)
		{
			foreach (var indices in Combinations(entries.Count, size))
			{
				var candidate = indices.Select(i => entries[i]).ToList();
				var key = ComboRecipe.KeyFor(candidate.Select(e => e.Move.ComboId));
				if (!ComboRecipe.Exists(key))
				{
					continue;
				}
				recipe = ComboRecipe.Create(key);
				if (recipe != null)
				{
					group = candidate;
					return true;
				}
			}
		}
		recipe = null;
		group = null;
		return false;
	}

	// Index sets of the given size in lexicographic order (0,1 before 0,2 before 1,2 ...).
	private static IEnumerable<int[]> Combinations(int count, int size)
	{
		var indices = Enumerable.Range(0, size).ToArray();
		while (true)
		{
			yield return (int[])indices.Clone();
			var i = size - 1;
			while (i >= 0 && indices[i] == count - size + i)
			{
				i--;
			}
			if (i < 0)
			{
				yield break;
			}
			indices[i]++;
			for (var j = i + 1; j < size; j++)
			{
				indices[j] = indices[j - 1] + 1;
			}
		}
	}
}
