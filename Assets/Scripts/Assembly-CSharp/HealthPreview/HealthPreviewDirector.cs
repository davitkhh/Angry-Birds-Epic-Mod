using System;
using System.Collections.Generic;
using ABH.GameDatas.Battle;
using ABH.GameDatas.Interfaces;
using UnityEngine;

// Shows what a hovered move would do on the health bars (HealthBarPreview), fed by the bird drag
// (CharacterControllerBattleGround.SetDragHUD) and the rage drag (RageMeterController.OnDrag).
public static class HealthPreviewDirector
{
	private static readonly List<HealthBarPreview> s_Shown = new List<HealthBarPreview>();

	private static ICombatant s_Source;

	private static ICombatant s_Target;

	private static SkillBattleDataBase s_Skill;

	private static bool s_IsRage;

	private static RageMeterPreview s_Rage;

	public static void Show(BattleGameData battle, ICombatant source, SkillBattleDataBase skill, ICombatant target, bool isRage)
	{
		if (s_Shown.Count > 0 && source == s_Source && target == s_Target && skill == s_Skill && isRage == s_IsRage)
		{
			return;
		}
		Clear();
		s_Source = source;
		s_Target = target;
		s_Skill = skill;
		s_IsRage = isRage;
		HealthPreviewContext ctx;
		try
		{
			ctx = HealthPreviewCalculator.PredictAll(battle, source, skill, target, isRage);
		}
		catch (Exception e)
		{
			Debug.LogWarning("[HealthPreview] " + (skill != null ? skill.GetType().Name : "?") + ": " + e);
			return;
		}
		// the rage chili (RageMeterPreview)
		s_Rage = RageMeterPreview.Show(battle, source, ctx.Rage, isRage);
		foreach (var pair in ctx.Forecasts)
		{
			var view = pair.Key.CombatantView;
			if (view == null)
			{
				continue;
			}
			var bar = view.GetComponentInChildren<CharacterHealthBar>();
			if (bar == null || bar is CharacterChunkHealthBar || !bar.gameObject.activeInHierarchy)
			{
				continue;
			}
			var preview = bar.GetComponent<HealthBarPreview>();
			if (preview == null)
			{
				preview = bar.gameObject.AddComponent<HealthBarPreview>();
			}
			if (preview.Show(pair.Value))
			{
				s_Shown.Add(preview);
			}
		}
		if (s_Shown.Count == 0)
		{
			// remembered anyway, so an empty hover isn't predicted again every frame
			s_Shown.Add(null);
		}
	}

	public static void Clear()
	{
		foreach (var preview in s_Shown)
		{
			if (preview != null)
			{
				preview.Hide();
			}
		}
		if (s_Rage != null)
		{
			s_Rage.Hide();
		}
		Reset();
	}

	// The move was released on the hovered target
	public static void Commit()
	{
		foreach (var preview in s_Shown)
		{
			if (preview != null)
			{
				preview.Commit();
			}
		}
		// the chili's own fill animation takes over
		if (s_Rage != null)
		{
			s_Rage.Hide();
		}
		Reset();
	}

	private static void Reset()
	{
		s_Shown.Clear();
		s_Source = null;
		s_Target = null;
		s_Skill = null;
		s_IsRage = false;
		s_Rage = null;
	}
}
