using ABH.GameDatas;

// Character asset for a swap crystal (SwapCrystalCombatant), so the regular battle character view can place it.
// It has no bone animation (the base class falls back to a proxy) and no equipment; SwapCrystalView does the looks.
public class SwapCrystalAssetController : CharacterAssetController
{
	protected override void SetEquipment(bool isWorldMap, bool showItems)
	{
		m_EquipmentSetOnce = true;
	}

	protected override void SetEquipmentItem(IInventoryItemGameData itemGameData, string boneName, string suffix = "")
	{
	}

	protected override void PlayBlinkAnimation()
	{
	}

	public override void PlayAttackAnim(bool useOffhand)
	{
	}

	public override float GetAttackAnimationLength()
	{
		return 0f;
	}
}
