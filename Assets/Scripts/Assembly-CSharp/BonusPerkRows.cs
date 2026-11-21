using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.Shared.Models.Generic;
using UnityEngine;

// Shows an item's bonus perks (upgrade blueprints, McCool's shield upgrades: EquipmentGameData.BonusPerks) as perks of
// their own, next to the item's perk, by cloning the perk's block at runtime:
// - the equipment row in the bird window (EquipmentItemInfo): the perk and its bonus perks are stacked like the set item
//   row's perk + set bonus (smaller icons), each with its own tap and hold;
// - the equipment hold overlay (EquipmentOverlay): one more perk box per bonus perk under the perk, the content below
//   moved down and the frame grown to fit.
// The state (layout before any change, clones) lives on a component on the block's parent, restored on every call.
public class BonusPerkRows : MonoBehaviour
{
	private Transform m_Block;

	private readonly Dictionary<Transform, Vector3> m_Positions = new Dictionary<Transform, Vector3>();

	private readonly Dictionary<Transform, Vector3> m_Scales = new Dictionary<Transform, Vector3>();

	private readonly Dictionary<UIWidget, int> m_Heights = new Dictionary<UIWidget, int>();

	private readonly List<GameObject> m_Clones = new List<GameObject>();

	private readonly List<EquipmentPerk> m_Perks = new List<EquipmentPerk>();

	// Row: vertical distance between the stacked perks, and the icon scale while stacked (the set bonus row's)
	private const float RowSpacing = 54f;

	private const float RowIconScale = 0.8f;

	// Overlay: height added per bonus perk box (box 90 + gap)
	private const float OverlayStep = 96f;

	private static BonusPerkRows For(Transform block)
	{
		var parent = block.parent;
		var rows = parent.GetComponent<BonusPerkRows>();
		if (rows == null)
		{
			rows = parent.gameObject.AddComponent<BonusPerkRows>();
		}
		if (rows.m_Block == null)
		{
			rows.m_Block = block;
			rows.Capture(parent);
		}
		return rows;
	}

	private void Capture(Transform root)
	{
		foreach (var t in root.GetComponentsInChildren<Transform>(true))
		{
			m_Positions[t] = t.localPosition;
			m_Scales[t] = t.localScale;
		}
		foreach (var widget in root.GetComponentsInChildren<UIWidget>(true))
		{
			m_Heights[widget] = widget.height;
		}
	}

	private void Restore()
	{
		foreach (var clone in m_Clones)
		{
			if (clone != null)
			{
				clone.SetActive(false);
				Object.Destroy(clone);
			}
		}
		m_Clones.Clear();
		m_Perks.Clear();
		foreach (var pair in m_Positions)
		{
			if (pair.Key != null)
			{
				pair.Key.localPosition = pair.Value;
			}
		}
		foreach (var pair in m_Scales)
		{
			if (pair.Key != null)
			{
				pair.Key.localScale = pair.Value;
			}
		}
		foreach (var pair in m_Heights)
		{
			if (pair.Key != null)
			{
				pair.Key.height = pair.Value;
			}
		}
	}

	public static EquipmentPerk GetPerk(Transform block, int index)
	{
		var rows = block == null || block.parent == null ? null : block.parent.GetComponent<BonusPerkRows>();
		return rows == null || index < 0 || index >= rows.m_Perks.Count ? null : rows.m_Perks[index];
	}

	public static Transform GetIcon(Transform block, int index)
	{
		var rows = block == null || block.parent == null ? null : block.parent.GetComponent<BonusPerkRows>();
		if (rows == null || index < 0 || index >= rows.m_Clones.Count)
		{
			return null;
		}
		var icon = rows.m_Clones[index].GetComponentsInChildren<UISprite>(true).FirstOrDefault(s => s.name == "Icon");
		return icon != null ? icon.cachedTransform : rows.m_Clones[index].transform;
	}

	// Bird window equipment row. block = the perk's root (3_PassiveEffect: Box, Icon, Name + tap and hold). The clones'
	// hold sends ShowBonusPerkTooltip<i> to the row's receiver.
	public static void ApplyToRow(Transform block, UILabel perkName, EquipmentGameData item)
	{
		var bonus = item == null || item.Perk == null ? new List<EquipmentPerk>() : item.BonusPerks;
		ApplyToRow(block, perkName, bonus, p => DIContainerInfrastructure.GetLocaService().GetPerkName(p.Type));
	}

	public static void ApplyToRow(Transform block, UILabel perkName, List<EquipmentPerk> bonus, System.Func<EquipmentPerk, string> nameOf)
	{
		if (block == null || block.parent == null)
		{
			return;
		}
		var rows = For(block);
		rows.Restore();
		if (bonus == null || bonus.Count == 0 || !block.gameObject.activeSelf)
		{
			return;
		}
		var count = bonus.Count + 1;
		var spacing = Mathf.Min(RowSpacing, 108f / (count - 1));
		var top = spacing * (count - 1) * 0.5f;
		var basePosition = rows.m_Positions[block];
		ShrinkRowIcons(block);
		block.localPosition = basePosition + new Vector3(0f, top, 0f);
		for (var i = 0; i < bonus.Count; i++)
		{
			var clone = Object.Instantiate(block.gameObject, block.parent);
			clone.name = block.name + "_Bonus" + i;
			clone.transform.localPosition = basePosition + new Vector3(0f, top - spacing * (i + 1), 0f);
			clone.transform.localScale = block.localScale;
			rows.m_Clones.Add(clone);
			rows.m_Perks.Add(bonus[i]);
			var icon = clone.GetComponentsInChildren<UISprite>(true).FirstOrDefault(s => s.name == "Icon");
			if (icon != null)
			{
				icon.spriteName = EquipmentGameData.GetPerkIconNameByPerk(bonus[i].Type);
			}
			var label = clone.GetComponentsInChildren<UILabel>(true).FirstOrDefault(l => perkName == null || l.name == perkName.name);
			if (label != null)
			{
				label.text = nameOf(bonus[i]);
			}
			var invoker = clone.GetComponent<ActionOverlayInvoker>();
			if (invoker != null)
			{
				invoker.m_OpenActionName = "ShowBonusPerkTooltip" + i;
				if (invoker.m_ReceiverObject == null)
				{
					invoker.m_ReceiverObject = block.parent.gameObject;
				}
			}
		}
	}

	private static void ShrinkRowIcons(Transform block)
	{
		foreach (Transform child in block)
		{
			if (child.GetComponent<UISprite>() != null)
			{
				child.localScale = new Vector3(child.localScale.x * RowIconScale, child.localScale.y * RowIconScale, child.localScale.z);
			}
		}
	}

	// Equipment hold overlay. block = the perk box (3_PassiveEffect: Box, Icon, Text, Slot) under the bubble that also
	// holds the frame ("Body"). descLabel = the block's text; mainDesc = the item's own perk text without the bonus perks.
	public static void ApplyToOverlay(Transform block, UILabel descLabel, EquipmentGameData item, string mainDesc)
	{
		var bonus = item == null || item.Perk == null ? new List<EquipmentPerk>() : item.BonusPerks;
		ApplyToOverlay(block, descLabel, bonus, mainDesc, p => "[c8a000]" + DIContainerInfrastructure.GetLocaService().GetPerkName(p.Type) + ":[-] " + EquipmentGameData.GetPerkDesc(p));
	}

	public static void ApplyToOverlay(Transform block, UILabel descLabel, List<EquipmentPerk> bonus, string mainDesc, System.Func<EquipmentPerk, string> textOf)
	{
		if (block == null || block.parent == null || block.parent.Find("Body") == null)
		{
			return;
		}
		var rows = For(block);
		rows.Restore();
		if (bonus == null || bonus.Count == 0 || !block.gameObject.activeSelf)
		{
			return;
		}
		if (descLabel != null)
		{
			descLabel.text = mainDesc;
		}
		var bubble = block.parent;
		var blockY = rows.m_Positions[block].y;
		var grow = OverlayStep * bonus.Count;
		// Everything below the perk box moves down
		foreach (Transform child in bubble)
		{
			if (child == block || child.name == "Body")
			{
				continue;
			}
			if (rows.m_Positions[child].y < blockY)
			{
				child.localPosition = rows.m_Positions[child] - new Vector3(0f, grow, 0f);
			}
		}
		// Frame: pieces below the perk box move down, pieces spanning it grow downwards
		foreach (Transform piece in bubble.Find("Body"))
		{
			var widget = piece.GetComponent<UIWidget>();
			var position = rows.m_Positions[piece];
			if (widget == null)
			{
				continue;
			}
			var height = rows.m_Heights[widget];
			var top = position.y + height * 0.5f;
			var bottom = position.y - height * 0.5f;
			if (bottom < blockY && top > blockY)
			{
				widget.height = height + Mathf.RoundToInt(grow);
				piece.localPosition = position - new Vector3(0f, grow * 0.5f, 0f);
			}
			else if (top <= blockY)
			{
				piece.localPosition = position - new Vector3(0f, grow, 0f);
			}
		}
		for (var i = 0; i < bonus.Count; i++)
		{
			var clone = Object.Instantiate(block.gameObject, bubble);
			clone.name = block.name + "_Bonus" + i;
			clone.transform.localPosition = rows.m_Positions[block] - new Vector3(0f, OverlayStep * (i + 1), 0f);
			clone.transform.localScale = block.localScale;
			rows.m_Clones.Add(clone);
			rows.m_Perks.Add(bonus[i]);
			var icon = clone.GetComponentsInChildren<UISprite>(true).FirstOrDefault(s => s.name == "Icon");
			if (icon != null)
			{
				icon.spriteName = EquipmentGameData.GetPerkIconNameByPerk(bonus[i].Type);
				icon.gameObject.SetActive(true);
			}
			var label = clone.GetComponentsInChildren<UILabel>(true).FirstOrDefault(l => descLabel == null || l.name == descLabel.name);
			if (label != null)
			{
				label.text = textOf(bonus[i]);
			}
		}
	}
}
