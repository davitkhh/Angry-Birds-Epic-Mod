using UnityEngine;

// The "CRITICAL!" text of the Crit hit effect (SkillFX_Hit_Critical). Ground Zero reuses that effect but reads
// "BLAST-OFF!" instead; when a real crit lands on the same hit both texts show, stacked with BLAST-OFF! on top.
public static class PerkHitLabels
{
	public const string CritLocaIdent = "btl_perk_crit";

	public const string BlastOffLocaIdent = "btl_perk_groundzero";

	public const string BlastOffDefault = "BLAST-OFF!";

	// Texts spawned on one combatant within this long count as the same hit
	private const float SameHitWindow = 0.5f;

	// One line of the label (font 68 under the Text node's 1.5 y scale), along the text's own tilted up axis
	private const float LineHeight = 95f;

	public class State
	{
		public int BlastOffRequests;

		public float BlastOffRequestTime = -1f;

		public LocaScript LastLabel;

		public float LastLabelTime = -1f;

		public int LastStackLevel;
	}

	// Ground Zero fired: the next Crit text spawned on this combatant reads BLAST-OFF!
	public static void RequestBlastOff(CharacterControllerBattleGroundBase character)
	{
		if (character == null)
		{
			return;
		}
		var state = character.m_PerkHitLabels;
		if (Time.time - state.BlastOffRequestTime > SameHitWindow)
		{
			state.BlastOffRequests = 0;
		}
		state.BlastOffRequests++;
		state.BlastOffRequestTime = Time.time;
	}

	// Called for every effect spawned on a combatant
	public static void OnEffectSpawned(CharacterControllerBattleGroundBase character, GameObject effect)
	{
		if (character == null || effect == null)
		{
			return;
		}
		var label = FindCritLabel(effect);
		if (label == null)
		{
			return;
		}
		var state = character.m_PerkHitLabels;
		var now = Time.time;
		var blastOff = false;
		if (state.BlastOffRequests > 0)
		{
			if (now - state.BlastOffRequestTime <= SameHitWindow)
			{
				state.BlastOffRequests--;
				blastOff = true;
			}
			else
			{
				state.BlastOffRequests = 0;
			}
		}
		SetText(label, blastOff);
		var stackLevel = 0;
		var previous = state.LastLabel;
		if (previous != null && now - state.LastLabelTime <= SameHitWindow)
		{
			stackLevel = state.LastStackLevel + 1;
			var textRoot = TextRoot(label, effect.transform);
			textRoot.localPosition += textRoot.localRotation * Vector3.up * (LineHeight * stackLevel);
			// BLAST-OFF! reads above CRITICAL!
			if (!blastOff && IsBlastOff(previous))
			{
				SetText(label, true);
				SetText(previous, false);
			}
		}
		state.LastLabel = label;
		state.LastLabelTime = now;
		state.LastStackLevel = stackLevel;
	}

	private static LocaScript FindCritLabel(GameObject effect)
	{
		foreach (var loca in effect.GetComponentsInChildren<LocaScript>(true))
		{
			if (loca.m_locaIdent == CritLocaIdent || loca.m_locaIdent == BlastOffLocaIdent)
			{
				return loca;
			}
		}
		return null;
	}

	private static bool IsBlastOff(LocaScript label)
	{
		return label.m_locaIdent == BlastOffLocaIdent;
	}

	// LocaScript re-applies its ident on Start, so change the ident too, not just the label
	private static void SetText(LocaScript label, bool blastOff)
	{
		if (blastOff)
		{
			label.m_locaIdent = BlastOffLocaIdent;
			label.m_defaultText = BlastOffDefault;
		}
		else
		{
			label.m_locaIdent = CritLocaIdent;
			label.m_defaultText = "Critical!";
		}
		label.SetLoca();
	}

	// The child of the effect root holding the label ("Text"): the clip animates only the nodes below it
	private static Transform TextRoot(LocaScript label, Transform effectRoot)
	{
		var t = label.transform;
		while (t.parent != null && t.parent != effectRoot)
		{
			t = t.parent;
		}
		return t;
	}
}
