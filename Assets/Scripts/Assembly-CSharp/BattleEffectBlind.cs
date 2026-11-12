using ABH.GameDatas.Battle;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

public class BattleEffectBlind : MonoBehaviour
{
	[SerializeField]
	private UISprite m_EffectIcon;

	[SerializeField]
	private UILabel m_EffectName;

	[SerializeField]
	private UILabel m_Duration;

	[SerializeField]
	private GameObject m_DurationRoot;

	[SerializeField]
	private UISprite m_Background;

	[SerializeField]
	private Color m_CurseColor = Color.red;

	[SerializeField]
	private Color m_BlessingColor = Color.green;

	[SerializeField]
	private Color m_PassiveColor = Color.blue;

	[SerializeField]
	private Color m_NoneColor = Color.grey;

	[SerializeField]
	private Color m_SetColor = Color.yellow;

	public void ShowEffectBlind(BattleEffectGameData effect, ICombatant owner)
	{
		if (string.IsNullOrEmpty(effect.m_IconAssetId))
		{
			return;
		}
		var turnsLeft = effect.GetTurnsLeft();
		m_Duration.text = effect.GetTurnsLeft().ToString("0");
		m_DurationRoot.SetActive(turnsLeft > 0);
		if (effect.m_IconAssetId == "Stun")
		{
			if (DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().ContainsAsset("Skills_Pigs_01"))
			{
				var gameObject = DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().GetObject("Skills_Pigs_01") as GameObject;
				m_EffectIcon.atlas = gameObject.GetComponent<UIAtlas>();
			}
			m_EffectIcon.spriteName = "AttackCurse";
		}
		else
		{
			if (DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().ContainsAsset(effect.m_IconAtlasId))
			{
				var gameObject2 = DIContainerInfrastructure.GetGenericIconAtlasAssetProvider().GetObject(effect.m_IconAtlasId) as GameObject;
				m_EffectIcon.atlas = gameObject2.GetComponent<UIAtlas>();
			}
			m_EffectIcon.spriteName = effect.m_IconAssetId;
		}
		m_EffectName.text = effect.m_LocalizedName;
		ShowStacks(effect.m_Stacks);
		switch (effect.m_EffectType)
		{
		case SkillEffectTypes.Blessing:
			m_Background.color = m_BlessingColor;
			break;
		case SkillEffectTypes.Curse:
			m_Background.color = m_CurseColor;
			break;
		case SkillEffectTypes.None:
			m_Background.color = m_NoneColor;
			break;
		case SkillEffectTypes.Passive:
			m_Background.color = m_PassiveColor;
			break;
		case SkillEffectTypes.Environmental:
			m_Background.color = m_PassiveColor;
			break;
		case SkillEffectTypes.SetPassive:
			m_Background.color = m_SetColor;
			break;
		}
	}

	// "2x" left of the icon for stacked effects (Hype perk); single stacks show nothing
	private void ShowStacks(int stacks)
	{
		if (stacks <= 1 || m_EffectName == null)
		{
			return;
		}
		var label = Instantiate(m_EffectName.gameObject).GetComponent<UILabel>();
		label.name = "Stacks";
		label.transform.parent = transform;
		label.transform.localScale = Vector3.one;
		label.rawPivot = UIWidget.Pivot.Right;
		// The icon's square background spans x = -1..53 of the row
		label.transform.localPosition = new Vector3(-6f, 0f, m_EffectName.transform.localPosition.z);
		label.overflowMethod = UILabel.Overflow.ResizeFreely;
		label.text = stacks + "x";
		label.MarkAsChanged();
	}
}
