using System.Collections.Generic;
using System.Globalization;
using ABH.Shared.Models.Generic;

// Ability Decision Order trees from the Combatant Forge (Tools/CombatantForge/web/decisions.js): a combatant can have
// several whole trees of combos and picks one of them at the start of every battle. Each combo of a tree starts with
// the marker "@tree<n>:<pct>" (pct = that tree's chance of being picked); combos without a marker are used as they are.
public static class ForgeDecisionTrees
{
	public const string Prefix = "@tree";

	// The combos this combatant follows for this battle: one tree picked by its chance, its markers taken off.
	public static List<AiCombo> Pick(List<AiCombo> all)
	{
		if (all == null)
		{
			return null;
		}
		var ids = new List<string>();
		var weights = new List<float>();
		foreach (var c in all)
		{
			string id;
			float pct;
			if (Parse(c, out id, out pct) && !ids.Contains(id))
			{
				ids.Add(id);
				weights.Add(UnityEngine.Mathf.Max(0f, pct));
			}
		}
		if (ids.Count == 0)
		{
			return all;
		}
		var total = 0f;
		foreach (var w in weights)
		{
			total += w;
		}
		var chosen = ids[ids.Count - 1];
		if (total > 0f)
		{
			var r = UnityEngine.Random.Range(0f, total);
			for (var i = 0; i < ids.Count; i++)
			{
				if (r < weights[i])
				{
					chosen = ids[i];
					break;
				}
				r -= weights[i];
			}
		}
		else
		{
			chosen = ids[UnityEngine.Random.Range(0, ids.Count)];
		}
		var result = new List<AiCombo>();
		foreach (var c in all)
		{
			string id;
			float pct;
			if (!Parse(c, out id, out pct))
			{
				result.Add(c);
			}
			else if (id == chosen)
			{
				result.Add(new AiCombo { Percentage = c.Percentage, ComboChain = c.ComboChain.GetRange(1, c.ComboChain.Count - 1) });
			}
		}
		return result;
	}

	private static bool Parse(AiCombo c, out string id, out float pct)
	{
		id = null;
		pct = 0f;
		if (c == null || c.ComboChain == null || c.ComboChain.Count == 0 || c.ComboChain[0] == null || !c.ComboChain[0].StartsWith(Prefix))
		{
			return false;
		}
		var mark = c.ComboChain[0].Substring(Prefix.Length);
		var colon = mark.IndexOf(':');
		id = colon < 0 ? mark : mark.Substring(0, colon);
		if (colon >= 0)
		{
			float.TryParse(mark.Substring(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out pct);
		}
		return true;
	}
}
