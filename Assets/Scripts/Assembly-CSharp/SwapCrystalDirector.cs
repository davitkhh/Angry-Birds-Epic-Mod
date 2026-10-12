using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Battle;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

// Runs a battle's swap crystals (SwapCrystalCombatant, see SwapCrystalConfig). They stand in the pig formation but
// don't fight. Each takes a turn in the pigs' turn where its initiative (from its formation slot) falls among the
// pigs: BattleMgr.DoNextStep asks TakeCrystalTurns before each pig acts, and once more when the birds' turn starts
// for crystals that come after every pig. On every "turns"-th of its turns a crystal switches its primary color and
// each assigned pig turns into the other assigned type (PigSwapMorph). Added to the battle manager on first use.
public class SwapCrystalDirector : MonoBehaviour
{
	private const float AppearDuration = 0.6f;

	private const float VanishDuration = 0.5f;

	// A turn without a swap: a short pulse so it's clear the crystal had its go.
	private const float IdleTurnDuration = 0.4f;

	// Swap timeline (seconds): crystal charges up, pigs take its color, forms and colors cross over, hold, color fades.
	private const float ChargeDuration = 0.35f;

	private const float TintInDuration = 0.45f;

	private const float MorphDuration = 1.1f;

	private const float HoldDuration = 0.15f;

	private const float TintOutDuration = 0.45f;

	private class Crystal
	{
		public SwapCrystalCombatant Combatant;

		public SwapCrystalView View;

		public bool Blue;

		public int TurnsTaken;

		// Its turn in the current pigs' turn hasn't come yet.
		public bool Pending;

		public SwapCrystalConfig Config
		{
			get
			{
				return Combatant.Config;
			}
		}
	}

	private BattleMgrBase m_BattleMgr;

	private readonly List<Crystal> m_Crystals = new List<Crystal>();

	// Color a crystal had when its wave ended, so the same crystal in the next wave picks up where it left off.
	private readonly Dictionary<string, bool> m_LastBlue = new Dictionary<string, bool>();

	private BattleGameData m_SubscribedBattle;

	public static SwapCrystalDirector For(BattleMgrBase battleMgr)
	{
		var director = battleMgr.GetComponent<SwapCrystalDirector>();
		if (director == null)
		{
			director = battleMgr.gameObject.AddComponent<SwapCrystalDirector>();
			director.m_BattleMgr = battleMgr;
		}
		return director;
	}

	private BattleGameData Battle
	{
		get
		{
			return m_BattleMgr.Model;
		}
	}

	// Called once a wave's pigs have their places. The crystals don't walk in with the pigs: they appear in their slot.
	public void OnWavePigsPlaced()
	{
		Subscribe();
		RemoveCrystals();
		if (!Battle.m_CombatantsPerFaction.ContainsKey(Faction.Pigs))
		{
			return;
		}
		foreach (var combatant in Battle.m_CombatantsPerFaction[Faction.Pigs].OfType<SwapCrystalCombatant>())
		{
			var view = combatant.CombatantView;
			var crystalView = view != null && view.m_AssetController != null ? view.m_AssetController.GetComponent<SwapCrystalView>() : null;
			if (crystalView == null)
			{
				// Boss formations (PlaceCharacterBoss) only place participating combatants.
				DebugLog.Error("[SwapCrystal] " + combatant.CombatantNameId + " got no place in this formation.");
				continue;
			}
			view.transform.position = m_BattleMgr.m_PigCenterPosition.position + view.CachedUnfocusedPos;
			var collider = view.GetComponent<Collider>();
			if (collider != null)
			{
				collider.enabled = false;
			}
			bool blue;
			var crystal = new Crystal
			{
				Combatant = combatant,
				View = crystalView,
				Blue = m_LastBlue.TryGetValue(combatant.CombatantNameId, out blue) ? blue : combatant.Config.StartBlue
			};
			crystalView.SetBlueAmount(crystal.Blue ? 1f : 0f);
			crystalView.SetAboutToSwap(crystal.Config.Turns == 1);
			crystalView.StartCoroutine(crystalView.Appear(AppearDuration));
			m_Crystals.Add(crystal);
		}
	}

	private void Subscribe()
	{
		if (m_SubscribedBattle == Battle)
		{
			return;
		}
		Unsubscribe();
		m_SubscribedBattle = Battle;
		m_SubscribedBattle.PigsTurnStarted += OnPigsTurnStarted;
		m_SubscribedBattle.WaveDone += OnWaveDone;
	}

	private void Unsubscribe()
	{
		if (m_SubscribedBattle != null)
		{
			m_SubscribedBattle.PigsTurnStarted -= OnPigsTurnStarted;
			m_SubscribedBattle.WaveDone -= OnWaveDone;
			m_SubscribedBattle = null;
		}
	}

	private void OnDestroy()
	{
		Unsubscribe();
	}

	// The cleared wave's crystals fade where they stand; on a floor change the battle area moves up without them.
	private void OnWaveDone(int clearedWaveIndex)
	{
		RemoveCrystals();
	}

	private void RemoveCrystals()
	{
		foreach (var crystal in m_Crystals)
		{
			m_LastBlue[crystal.Combatant.CombatantNameId] = crystal.Blue;
			var view = crystal.Combatant.CombatantView;
			if (crystal.View != null && view != null)
			{
				view.transform.SetParent(null, true);
				crystal.View.Vanish(VanishDuration, view.gameObject);
			}
		}
		m_Crystals.Clear();
	}

	private void OnPigsTurnStarted(int turn)
	{
		foreach (var crystal in m_Crystals)
		{
			crystal.Pending = true;
		}
	}

	// Takes the turns of crystals whose initiative comes before nextPig's (all remaining ones if null) in this pigs' turn.
	public IEnumerator TakeCrystalTurns(ICombatant nextPig)
	{
		var due = m_Crystals.Where(c => c.Pending && c.View != null && (nextPig == null || c.Combatant.CurrentInitiative < nextPig.CurrentInitiative)).OrderBy(c => c.Combatant.CurrentInitiative).ToList();
		foreach (var crystal in due)
		{
			crystal.Pending = false;
			if (!m_Crystals.Contains(crystal) || crystal.View == null || (Battle.m_BattleEndData != null && Battle.m_BattleEndData.m_WinnerFaction != Faction.None))
			{
				continue;
			}
			crystal.TurnsTaken++;
			if (crystal.TurnsTaken % crystal.Config.Turns == 0)
			{
				yield return StartCoroutine(Swap(crystal));
			}
			else
			{
				var view = crystal.View;
				yield return Animate(IdleTurnDuration, t => view.SetFlash(Mathf.Sin(t * Mathf.PI) * 0.35f));
			}
			if (crystal.View != null)
			{
				crystal.View.SetAboutToSwap((crystal.TurnsTaken + 1) % crystal.Config.Turns == 0);
			}
		}
	}

	private IEnumerator Swap(Crystal crystal)
	{
		var view = crystal.View;
		var fromColors = SwapCrystalView.GetPigTintColors(crystal.Blue);
		var toColors = SwapCrystalView.GetPigTintColors(!crystal.Blue);
		var swaps = new List<KeyValuePair<PigCombatant, string>>();
		var pigs = Battle.m_CombatantsPerFaction.ContainsKey(Faction.Pigs) ? Battle.m_CombatantsPerFaction[Faction.Pigs].OfType<PigCombatant>().ToList() : new List<PigCombatant>();
		foreach (var pig in pigs)
		{
			var target = crystal.Config.GetSwapTarget(pig.CombatantNameId);
			if (target != null && pig.IsParticipating && pig.CombatantView != null && pig.CombatantView.m_AssetController != null)
			{
				swaps.Add(new KeyValuePair<PigCombatant, string>(pig, target));
			}
		}

		// Each pig's new rig is spawned in its own frame while the crystal charges (all at once dropped frames); the
		// pigs look unchanged until the tint starts.
		var morphs = new List<PigSwapMorph>();
		var beginning = StartCoroutine(BeginMorphs(swaps, morphs, view.m_PigMorphMaterial, fromColors, toColors));
		// The crystal rises while it charges and colors the pigs, stays up through the swap, and settles as the color fades.
		yield return Animate(ChargeDuration, t =>
		{
			view.SetFlash(Mathf.Sin(t * Mathf.PI * 0.5f) * 0.6f);
			view.SetLift(Mathf.SmoothStep(0f, 0.6f, t));
		});
		yield return beginning;
		// Their captures start the frame after Begin.
		yield return null;

		yield return Animate(TintInDuration, t =>
		{
			var strength = Mathf.SmoothStep(0f, 1f, t);
			foreach (var morph in morphs)
			{
				morph.Strength = strength;
			}
			view.SetFlash(0.6f * (1f - t));
			view.SetLift(Mathf.Lerp(0.6f, 1f, Mathf.SmoothStep(0f, 1f, t)));
		});

		var wasBlue = crystal.Blue;
		yield return Animate(MorphDuration, t =>
		{
			var k = Mathf.SmoothStep(0f, 1f, t);
			foreach (var morph in morphs)
			{
				morph.Morph = k;
				morph.ColorT = k;
			}
			view.SetBlueAmount(wasBlue ? 1f - k : k);
		});
		crystal.Blue = !wasBlue;
		view.SetBlueAmount(crystal.Blue ? 1f : 0f);

		yield return new WaitForSeconds(HoldDuration);

		yield return Animate(TintOutDuration, t =>
		{
			var strength = 1f - Mathf.SmoothStep(0f, 1f, t);
			foreach (var morph in morphs)
			{
				morph.Strength = strength;
			}
			view.SetLift(strength);
		});
		foreach (var morph in morphs)
		{
			morph.Finish();
		}
	}

	private IEnumerator BeginMorphs(List<KeyValuePair<PigCombatant, string>> swaps, List<PigSwapMorph> morphs, Material material, Color[] fromColors, Color[] toColors)
	{
		foreach (var swap in swaps)
		{
			if (swap.Key.IsParticipating && swap.Key.CombatantView != null)
			{
				morphs.Add(PigSwapMorph.Begin(swap.Key, swap.Value, m_BattleMgr, material, fromColors, toColors));
			}
			yield return null;
		}
	}

	// Calls step with 0..1 over duration, ending on exactly 1.
	private static IEnumerator Animate(float duration, Action<float> step)
	{
		for (var elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
		{
			step(elapsed / duration);
			yield return null;
		}
		step(1f);
	}
}
