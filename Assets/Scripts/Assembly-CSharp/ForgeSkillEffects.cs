using System.Collections.Generic;
using UnityEngine;

// Combatant Forge: an ability's own attack effects. Each edited ability is written as Resources/ForgeSkillEffects/<skill
// NameId>.json in VisualEffectSetting's own shape (Tools/CombatantForge, publish.write_skill_effects), and
// SkillBattleDataBase uses it in place of the effects listed for its AssetId in Root.unity, which other skills share.
public static class ForgeSkillEffects
{
	private static readonly Dictionary<string, VisualEffectSetting> s_Cache = new Dictionary<string, VisualEffectSetting>();

	public static bool TryGet(string skillNameId, out VisualEffectSetting setting)
	{
		setting = null;
		if (string.IsNullOrEmpty(skillNameId))
		{
			return false;
		}
		if (!s_Cache.TryGetValue(skillNameId, out setting))
		{
			var text = Resources.Load<TextAsset>("ForgeSkillEffects/" + skillNameId);
			if (text != null)
			{
				try
				{
					setting = JsonUtility.FromJson<VisualEffectSetting>(text.text);
				}
				catch (System.Exception ex)
				{
					DebugLog.Error("Forge skill effects " + skillNameId + " failed to load: " + ex);
					setting = null;
				}
			}
			s_Cache[skillNameId] = setting;
		}
		return setting != null && setting.VisualEffects != null;
	}
}
