using ABH.GameDatas;
using ABH.GameDatas.Battle;

// Combatant Forge: a skill can name the clip its user plays for it, in place of the usual Action_Attack_<n> or
// Action_Support (CharacterControllerBattleGroundBase.PlaySkillClip). Skill parameters are numbers, so the parameter
// "anim_clip" holds a 24-bit FNV-1a hash of the clip's name; Tools/CombatantForge (publish.clip_hash) computes the same.
// "anim_projectile" names a projectile the same way (AttackSkillTemplate fires it instead of the weapon's), and
// "anim_ranged" makes an attack ranged (1) or melee (0) whatever its weapon is.
public static class ForgeSkillAnimation
{
	public const string Param = "anim_clip";

	public const string ProjectileParam = "anim_projectile";

	public const string RangedParam = "anim_ranged";

	public static int Hash(string name)
	{
		unchecked
		{
			var hash = 2166136261u;
			foreach (var c in name)
			{
				hash ^= c;
				hash *= 16777619u;
			}
			return (int)(hash & 0xFFFFFF);
		}
	}

	// The clip on this character's rig the skill asks for, or null when it asks for none or the rig doesn't have it.
	public static string ClipFor(SkillBattleDataBase skill, CharacterAssetController asset)
	{
		float value;
		if (skill == null || asset == null || skill.Model == null || skill.Model.SkillParameters == null || !skill.Model.SkillParameters.TryGetValue(Param, out value))
		{
			return null;
		}
		return asset.FindClipByHash((int)value);
	}

	// The projectile the skill names, or null when it names none or the game has no projectile by that name.
	public static string ProjectileFor(SkillGameData skill)
	{
		float value;
		if (skill == null || skill.SkillParameters == null || !skill.SkillParameters.TryGetValue(ProjectileParam, out value))
		{
			return null;
		}
		var provider = DIContainerInfrastructure.ProjectileAssetProvider;
		if (provider == null || provider.AssetInfos == null)
		{
			return null;
		}
		foreach (var info in provider.AssetInfos)
		{
			if (info != null && !string.IsNullOrEmpty(info.NameId) && Hash(info.NameId) == (int)value)
			{
				return info.NameId;
			}
		}
		return null;
	}
}
