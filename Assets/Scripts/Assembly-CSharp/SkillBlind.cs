using System.Collections.Generic;
using ABH.GameDatas;
using ABH.GameDatas.Battle;
using ABH.GameDatas.Battle.Skills;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

public class SkillBlind : MonoBehaviour
{
	[SerializeField]
	private UISprite m_SkillIcon;

	[SerializeField]
	private UISprite m_SkillTargetIcon;

	[SerializeField]
	private GameObject m_SkillTargetRoot;

	[SerializeField]
	private GameObject m_PerkBackgroundRoot;

	[SerializeField]
	private UILabel m_SkillName;

	[SerializeField]
	private UILabel m_SkillDescription;

	[SerializeField]
	private UISprite m_Background;

	[SerializeField]
	private Color m_OffensiveColor = Color.red;

	[SerializeField]
	private Color m_SupportColor = Color.blue;

	[SerializeField]
	private Color m_PassiveColor = Color.green;

	[SerializeField]
	private Color m_NoneColor = Color.grey;

	// Combo moves' name strip (#CD7936). In code rather than serialized so the existing overlay prefabs pick it up.
	public static readonly Color ComboColor = new Color32(0xCD, 0x79, 0x36, 0xFF);

	[SerializeField]
	private GameObject m_ChargeProgressRoot;

	[SerializeField]
	private UISprite m_ChargeProgress;

	[SerializeField]
	private List<GameObject> m_ChargeProgressDivider = new List<GameObject>();

	// The skill charge bar, cloned for the ammo regen bar in the character overlay
	public GameObject ChargeProgressTemplate
	{
		get
		{
			return m_ChargeProgressRoot;
		}
	}

	public void ShowSkillOverlay(SkillBattleDataBase skill, ICombatant invoker, bool isPassive)
	{
		if (m_Background)
		{
			AmmoUI.ClearSkillPlate(m_Background.cachedTransform.parent);
		}
		if (skill == null)
		{
			if (isPassive)
			{
				if (m_SkillName != null)
				{
					m_SkillName.text = DIContainerInfrastructure.GetLocaService().Tr("sko_header_no_passive", "No Passive Skill");
				}
				m_SkillDescription.text = DIContainerInfrastructure.GetLocaService().Tr("sko_text_no_passive", "This Pig doesn't have a Passive Skill");
			}
			else
			{
				if (m_SkillName != null)
				{
					m_SkillName.text = string.Empty;
				}
				m_SkillDescription.text = string.Empty;
			}
			m_SkillIcon.gameObject.SetActive(false);
			m_ChargeProgressRoot.SetActive(false);
			m_Background.color = m_NoneColor;
			return;
		}
		m_SkillIcon.gameObject.SetActive(true);
		if (m_SkillName != null)
		{
			m_SkillName.text = skill.GetLocalizedName(invoker);
		}
		var duration = 0;
		if (skill.TryGetChargeDurationLeft(out duration, invoker))
		{
			if (m_ChargeProgressRoot)
			{
				m_ChargeProgressRoot.SetActive(true);
				for (var i = 0; i < m_ChargeProgressDivider.Count; i++)
				{
					m_ChargeProgressDivider[i].SetActive(i == skill.GetChargeDuration() - 2);
				}
				m_ChargeProgress.fillAmount = 1f - (float)duration / (float)skill.GetChargeDuration();
			}
		}
		else if (m_ChargeProgressRoot)
		{
			m_ChargeProgressRoot.SetActive(false);
		}
		m_SkillDescription.text = skill.GetLocalizedDescription(invoker);
		if (DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().ContainsAsset(skill.Model.Balancing.IconAtlasId))
		{
			var gameObject = DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().GetObject(skill.Model.Balancing.IconAtlasId) as GameObject;
			m_SkillIcon.atlas = gameObject.GetComponent<UIAtlas>();
		}
		if (m_PerkBackgroundRoot && skill.Model.IsPseudoPerk())
		{
			m_PerkBackgroundRoot.SetActive(true);
			m_SkillIcon.spriteName = BannerItemGameData.GetPerkIconNameByPerk(skill.Model.GetPerkType());
			m_SkillIcon.MakePixelPerfect();
		}
		else
		{
			if (m_PerkBackgroundRoot)
			{
				m_PerkBackgroundRoot.SetActive(false);
			}
			m_SkillIcon.spriteName = skill.Model.m_SkillIconName;
			m_SkillIcon.MakePixelPerfect();
		}
		var flag = skill.Model.SkillParameters != null && skill.Model.SkillParameters.ContainsKey("all");
		var displayType = GetDisplayTargetType(skill);
		var flag2 = displayType == SkillTargetTypes.Passive || displayType == SkillTargetTypes.Support;
		var flag3 = (flag2 && invoker is PigCombatant) || (!flag2 && invoker is BirdCombatant);
		if (m_SkillTargetRoot)
		{
			m_SkillTargetRoot.gameObject.SetActive(false);
		}
		var text = !flag3 ? "Target_Bird" : "Target_Pig";
		if (flag)
		{
			text += "s";
		}
		if (m_Background)
		{
			switch (displayType)
			{
			case SkillTargetTypes.Attack:
				m_Background.color = m_OffensiveColor;
				break;
			case SkillTargetTypes.Passive:
				m_Background.color = m_PassiveColor;
				break;
			case SkillTargetTypes.Support:
				m_Background.color = m_SupportColor;
				break;
			case SkillTargetTypes.Combo:
				m_Background.color = ComboColor;
				break;
			}
			// Combatant Forge: a custom strip colour, stored as a 0xRRGGBB number.
			float customColor;
			if (skill.Model.SkillParameters != null && skill.Model.SkillParameters.TryGetValue("ui_color_rgb", out customColor))
			{
				var rgb = (int)customColor;
				m_Background.color = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
			}
		}
		if (m_SkillTargetIcon != null)
		{
			m_SkillTargetIcon.spriteName = text;
		}
		if (m_Background)
		{
			AmmoUI.ShowOnSkillPlate(m_Background, m_SkillName, skill, invoker);
		}
	}

	// Support-typed skills that read as attacks to the player (they target an ally or bomb, but hurt birds).
	private static SkillTargetTypes GetDisplayTargetType(SkillBattleDataBase skill)
	{
		if (skill is ShortenChargesAndThrowBombs || skill is GoadAllyIntoAttack)
		{
			return SkillTargetTypes.Attack;
		}
		return skill.Model.Balancing.TargetType;
	}

	public float GetBottomSlack(float spacing, float padding)
	{
		if (m_SkillDescription == null || m_Background == null)
		{
			return 0f;
		}
		var nextBlindTop = transform.InverseTransformPoint(m_Background.worldCorners[1]).y - spacing;
		var descriptionTop = transform.InverseTransformPoint(m_SkillDescription.worldCorners[1]).y;
		var contentBottom = descriptionTop - m_SkillDescription.printedSize.y;
		return Mathf.Max(0f, contentBottom - padding - nextBlindTop);
	}
}
