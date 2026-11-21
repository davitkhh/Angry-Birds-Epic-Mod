using System.Collections.Generic;
using ABH.GameDatas;
using ABH.Shared.Generic;
using UnityEngine;

public class EquipmentItemInfo : MonoBehaviour
{
	private float m_OldStat;

	[SerializeField]
	private List<LootDisplayContoller> m_LootDisplays = new List<LootDisplayContoller>();

	[SerializeField]
	private StatisticsElement m_RegularStats;

	[SerializeField]
	private StatisticsElement m_SetInfoStats;

	[SerializeField]
	private GameObject m_RegularInfo;

	[SerializeField]
	private UILabel m_SetEffectName;

	[SerializeField]
	private GameObject m_SetInfo;

	[SerializeField]
	private UISprite m_PerkSprite;

	[SerializeField]
	private UILabel m_PerkName;

	[SerializeField]
	private UILabel m_EquipmentName;

	private EquipmentGameData m_Item;

	private bool m_isPvp;

	public void ShowPerkTooltip()
	{
		if (m_Item != null && m_PerkSprite)
		{
			// With bonus perks shown as their own rows, the item's perk shows on its own too
			if (m_Item.Perk != null && BonusPerkRows.GetPerk(m_PerkSprite.cachedTransform.parent, 0) != null)
			{
				DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays.ShowPerkOverlay(m_PerkSprite.cachedTransform, m_Item.Perk, true);
				return;
			}
			DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays.ShowPerkOverlay(m_PerkSprite.cachedTransform, m_Item, true);
		}
	}

	// Sent by the bonus perk rows' tap and hold (BonusPerkRows.ApplyToRow)
	public void ShowBonusPerkTooltip0()
	{
		ShowBonusPerkTooltip(0);
	}

	public void ShowBonusPerkTooltip1()
	{
		ShowBonusPerkTooltip(1);
	}

	public void ShowBonusPerkTooltip2()
	{
		ShowBonusPerkTooltip(2);
	}

	private void ShowBonusPerkTooltip(int index)
	{
		var block = m_PerkSprite ? m_PerkSprite.cachedTransform.parent : null;
		var perk = BonusPerkRows.GetPerk(block, index);
		if (perk != null)
		{
			DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays.ShowPerkOverlay(BonusPerkRows.GetIcon(block, index), perk, true);
		}
	}

	public void ShowSetPerkTooltip()
	{
		if (m_Item != null && m_PerkSprite)
		{
			DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays.ShowSetPerkOverlay(m_PerkSprite.cachedTransform, m_Item, true, m_isPvp);
		}
	}

	public void ShowEquipmentTooltip()
	{
		if (m_Item != null && m_PerkSprite)
		{
			DIContainerInfrastructure.GetCoreStateMgr().m_InfoOverlays.ShowItemOverlay(base.transform, m_Item, true, m_isPvp);
		}
	}

	public void SetModel(EquipmentGameData equipmentGameData, BirdGameData selectedBird, bool isPvp)
	{
		m_Item = equipmentGameData;
		m_isPvp = isPvp;
		StatisticsElement statisticsElement;
		if (!m_Item.IsSetItem)
		{
			m_RegularInfo.SetActive(true);
			if (m_SetInfo)
			{
				m_SetInfo.SetActive(false);
			}
			statisticsElement = m_RegularStats;
			DebugLog.Log("Regular Stats activated");
		}
		else
		{
			if (m_SetInfo)
			{
				m_SetInfo.SetActive(true);
				if (m_RegularInfo)
				{
					m_RegularInfo.SetActive(false);
				}
				statisticsElement = m_SetInfoStats;
			}
			else
			{
				if (m_RegularInfo)
				{
					m_RegularInfo.SetActive(true);
				}
				statisticsElement = m_RegularStats;
			}
			if (m_SetEffectName)
			{
				m_SetEffectName.text = m_Item.SetItemSkill.SkillLocalizedName;
			}
		}
		if (statisticsElement)
		{
			switch (equipmentGameData.BalancingData.ItemType)
			{
			case InventoryItemType.MainHandEquipment:
				statisticsElement.SetIconSprite("Character_Damage_Large");
				statisticsElement.SetValueLabel(m_Item.ItemMainStat);
				break;
			case InventoryItemType.OffHandEquipment:
				statisticsElement.SetIconSprite("Character_Health_Large");
				statisticsElement.SetValueLabel(m_Item.ItemMainStat);
				break;
			}
		}
		var list = new List<IInventoryItemGameData>();
		if (equipmentGameData.GetScrapLoot() != null)
		{
			list = DIContainerLogic.GetLootOperationService().GetItemsFromLoot(DIContainerLogic.GetLootOperationService().GenerateLoot(equipmentGameData.GetScrapLoot(), 0));
		}
		if (m_LootDisplays != null)
		{
			for (var i = 0; i < m_LootDisplays.Count; i++)
			{
				if (list.Count > i)
				{
					m_LootDisplays[i].gameObject.SetActive(true);
					m_LootDisplays[i].SetModel(list[i], new List<IInventoryItemGameData>(), LootDisplayType.None, "_Small");
				}
				else
				{
					m_LootDisplays[i].gameObject.SetActive(false);
				}
			}
		}
		if (m_PerkSprite)
		{
			EquipmentGameData.ShowPerkIcon(m_PerkSprite, equipmentGameData);
		}
		if (m_PerkName)
		{
			m_PerkName.text = EquipmentGameData.GetPerkName(equipmentGameData);
		}
		// Bonus perks (upgrades) as perks of their own, stacked under the item's perk
		if (m_PerkSprite && m_PerkSprite.cachedTransform.parent != null && m_PerkSprite.cachedTransform.parent.parent == base.transform)
		{
			BonusPerkRows.ApplyToRow(m_PerkSprite.cachedTransform.parent, m_PerkName, equipmentGameData);
		}
		if (statisticsElement)
		{
			statisticsElement.RefreshStat(false, false, equipmentGameData.ItemMainStat, m_OldStat);
		}
		if (m_EquipmentName)
		{
			m_EquipmentName.text = equipmentGameData.ItemLocalizedName;
		}
		m_OldStat = equipmentGameData.ItemMainStat;
	}
}
