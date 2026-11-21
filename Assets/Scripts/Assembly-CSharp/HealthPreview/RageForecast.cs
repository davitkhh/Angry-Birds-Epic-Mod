using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// One predicted change of the birds' rage chili, in rage percent (+ fills, - empties).
public class RageChange
{
	public float Amount;

	public float Chance = 1f;

	public bool Guaranteed
	{
		get { return Chance >= 0.999f; }
	}
}

// Everything one hovered move would do to the birds' rage chili (drawn by RageMeterPreview).
// Using the rage itself (emptying the chili) is never part of it.
public class RageForecast
{
	public readonly List<RageChange> Changes = new List<RageChange>();

	// Attack hits counted so far: the move's first hit fills more than the ones after it (AddRageForAttack).
	public int AttackHits;

	public bool IsEmpty
	{
		get { return !Changes.Any(c => Mathf.Abs(c.Amount) > 0.01f && c.Chance > 0.001f); }
	}

	public void Add(float amount, float chance)
	{
		if (Mathf.Abs(amount) <= 0.01f || chance <= 0.001f)
		{
			return;
		}
		Changes.Add(new RageChange { Amount = amount, Chance = Mathf.Clamp01(chance) });
	}
}
