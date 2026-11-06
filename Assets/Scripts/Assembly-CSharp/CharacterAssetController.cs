using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.GameDatas.Interfaces;
using ABH.Shared.BalancingData;
using ABH.Shared.Generic;
using SmoothMoves;
using UnityEngine;

public class CharacterAssetController : AssetControllerWithBoneAnimationBase
{
	protected ICharacter Model;

	public Transform MainHandBone;

	public Transform OffHandBone;

	public Transform HeadGearBone;

	public Transform BodyCenter;

	public Transform BodyRoot;

	public Vector3 HealthBarPosition;

	public Vector3 CombatTextPosition;

	public Vector3 BubblePosition;

	public Vector3 ColliderSize;

	public Vector3 ColliderOffset;

	public Vector2 MinMaxTimeForBlink = new Vector2(2f, 4f);

	public float OffsetFromEnemyX = 100f;

	public bool m_IsWorldMap = true;

	public bool m_IsIllusion;

	protected string TextureName = string.Empty;

	protected bool m_EquipmentSetOnce;

	public List<string> m_LoadingAtlasNames = new List<string>();

	private bool m_ShowEquipment = true;

	public bool m_IsLite;

	public AssetFaction m_AssetFaction;

	public virtual void SetModel(ICharacter model, bool isWorldMap, bool showEquipment = true, bool useScaleController = true, bool lightWeighted = false, bool showItems = true)
	{
		// Rigs are pooled: drop items carried for a previous owner (see ShowHandItem).
		RemoveCarriedHandItems();
		var components = GetComponents<Behaviour>();
		foreach (var behaviour in components)
		{
			behaviour.enabled = true;
		}
		m_IsLite = lightWeighted;
		m_ShowEquipment = showEquipment;
		Model = model;
		RegisterEventHandler();
		m_IsWorldMap = isWorldMap;
		if (base.transform.parent)
		{
			base.gameObject.layer = base.transform.parent.gameObject.layer;
		}
		if (!useScaleController)
		{
			base.gameObject.GetComponent<ScaleController>().enabled = false;
			base.transform.localScale = Vector3.one;
		}
		else
		{
			var component = base.gameObject.GetComponent<ScaleController>();
			if (component)
			{
				component.enabled = true;
			}
		}
		SetEquipment(!m_ShowEquipment, showItems);
		PlayAnimation("Idle");
		CancelInvoke();
		if (HasAnimation("Blink"))
		{
			Invoke("PlayBlinkAnimation", Random.Range(MinMaxTimeForBlink.x, MinMaxTimeForBlink.y));
		}
	}

	protected virtual void PlayBlinkAnimation()
	{
		if (IsPlayingAnimation("Idle") && !IsPlayingAnimation("Stunned"))
		{
			PlayAnimation("Blink");
		}
		Invoke("PlayBlinkAnimation", Random.Range(MinMaxTimeForBlink.x, MinMaxTimeForBlink.y));
	}

	protected virtual void InventoryGameData_InventoryOfTypeChanged(InventoryItemType type, IInventoryItemGameData item)
	{
		if (base.gameObject == null)
		{
			DeregisterEventHandler();
		}
		else if (base.gameObject.activeInHierarchy)
		{
			switch (type)
			{
			case InventoryItemType.Class:
			case InventoryItemType.Skin:
				SetEquipment(!m_ShowEquipment, true);
				break;
			case InventoryItemType.MainHandEquipment:
				SetEquipment(!m_ShowEquipment, true);
				break;
			case InventoryItemType.OffHandEquipment:
				SetEquipment(!m_ShowEquipment, true);
				break;
			case InventoryItemType.Resources:
			case InventoryItemType.Ingredients:
			case InventoryItemType.Consumable:
			case InventoryItemType.Premium:
			case InventoryItemType.Story:
			case InventoryItemType.PlayerToken:
			case InventoryItemType.Points:
			case InventoryItemType.PlayerStats:
			case InventoryItemType.CraftingRecipes:
			case InventoryItemType.EventBattleItem:
			case InventoryItemType.EventCollectible:
			case InventoryItemType.Mastery:
			case InventoryItemType.BannerTip:
			case InventoryItemType.Banner:
			case InventoryItemType.BannerEmblem:
			case InventoryItemType.EventCampaignItem:
			case InventoryItemType.EventBossItem:
			case InventoryItemType.CollectionComponent:
			case InventoryItemType.Trophy:
				break;
			}
		}
	}

	protected void OnDestroy()
	{
		DeregisterEventHandler();
	}

	private void OnDisable()
	{
		// Disabling stops coroutines, so a pending mourn-sound watch can't clean up itself.
		ListenForMournTrigger(false);
		m_MournSoundRoutine = null;
		DeregisterEventHandler();
	}

	protected virtual void SetEquipment(bool isWorldMap, bool showItems)
	{
		if (base.HasNoBoneAnimation)
		{
			return;
		}
		if (isWorldMap && Model is BirdGameData)
		{
			SetBirdHeadgear();
			m_BoneAnimation.HideBone("MainHand", true);
			m_BoneAnimation.HideBone("OffHand", true);
		}
		else if (Model is PigGameData)
		{
			// Pig clips hide the shadow while the pig hops; keep it on the ground instead.
			AirborneShadow.Attach(this);
			if (isWorldMap)
			{
				if (HeadGearBone && HeadGearBone.name.EndsWith("_A"))
				{
					SetEquipmentItem(Model.ClassItem, "Headgear_A", string.Empty);
					SetEquipmentItem(Model.ClassItem, "Headgear_Back_A", "_Back");
					if (m_BoneAnimation.GetBoneTransform("MainHand_A") != null)
					{
						m_BoneAnimation.HideBone("MainHand_A", true);
					}
					if (m_BoneAnimation.GetBoneTransform("OffHand_A") != null)
					{
						m_BoneAnimation.HideBone("OffHand_A", true);
					}
					SetEquipmentItem(Model.ClassItem, "Headgear_B", string.Empty);
					SetEquipmentItem(Model.ClassItem, "Headgear_Back_B", "_Back");
					if (m_BoneAnimation.GetBoneTransform("MainHand_B") != null)
					{
						m_BoneAnimation.HideBone("MainHand_B", true);
					}
					if (m_BoneAnimation.GetBoneTransform("OffHand_B") != null)
					{
						m_BoneAnimation.HideBone("OffHand_B", true);
					}
					SetEquipmentItem(Model.ClassItem, "Headgear_C", string.Empty);
					SetEquipmentItem(Model.ClassItem, "Headgear_Back_C", "_Back");
					if (m_BoneAnimation.GetBoneTransform("MainHand_C") != null)
					{
						m_BoneAnimation.HideBone("MainHand_C", true);
					}
					if (m_BoneAnimation.GetBoneTransform("OffHand_C") != null)
					{
						m_BoneAnimation.HideBone("OffHand_C", true);
					}
				}
				else
				{
					SetEquipmentItem(Model.ClassItem, "Headgear", string.Empty);
					SetEquipmentItem(Model.ClassItem, "Headgear_Back", "_Back");
					SetPirateVariants(Model.ClassItem);
					if (m_BoneAnimation.GetBoneTransform("MainHand") != null)
					{
						m_BoneAnimation.HideBone("MainHand", true);
					}
					if (m_BoneAnimation.GetBoneTransform("OffHand") != null)
					{
						m_BoneAnimation.HideBone("OffHand", true);
					}
				}
			}
			else if (HeadGearBone && HeadGearBone.name.EndsWith("_A"))
			{
				SetEquipmentItem(Model.ClassItem, "Headgear_A", string.Empty);
				SetEquipmentItem(Model.ClassItem, "Headgear_Back_A", "_Back");
				if (showItems)
				{
					SetEquipmentItem(Model.MainHandItem, "MainHand_A", string.Empty);
					SetEquipmentItem(Model.OffHandItem, "OffHand_A", string.Empty);
					if (m_BoneAnimation.GetBoneTransform("OffHand_Back_A") != null)
					{
						SetEquipmentItem(Model.OffHandItem, "OffHand_Back_A", "_Back");
					}
					SetEquipmentItem(Model.MainHandItem, "MainHand_B", string.Empty);
					SetEquipmentItem(Model.OffHandItem, "OffHand_B", string.Empty);
					if (m_BoneAnimation.GetBoneTransform("OffHand_Back_B") != null)
					{
						SetEquipmentItem(Model.OffHandItem, "OffHand_Back_B", "_Back");
					}
					SetEquipmentItem(Model.MainHandItem, "MainHand_C", string.Empty);
					SetEquipmentItem(Model.OffHandItem, "OffHand_C", string.Empty);
					if (m_BoneAnimation.GetBoneTransform("OffHand_Back_C") != null)
					{
						SetEquipmentItem(Model.OffHandItem, "OffHand_Back_C", "_Back");
					}
				}
				else
				{
					if (m_BoneAnimation.GetBoneTransform("MainHand") != null)
					{
						m_BoneAnimation.HideBone("MainHand", true);
					}
					if (m_BoneAnimation.GetBoneTransform("OffHand") != null)
					{
						m_BoneAnimation.HideBone("OffHand", true);
					}
				}
				SetEquipmentItem(Model.ClassItem, "Headgear_B", string.Empty);
				SetEquipmentItem(Model.ClassItem, "Headgear_Back_B", "_Back");
				SetEquipmentItem(Model.ClassItem, "Headgear_C", string.Empty);
				SetEquipmentItem(Model.ClassItem, "Headgear_Back_C", "_Back");
			}
			else
			{
				if ((Model as PigGameData).ClassSkin != null && Model.ClassItem.BalancingData.NameId == (Model as PigGameData).ClassSkin.BalancingData.OriginalClass)
				{
					SetEquipmentItem((Model as PigGameData).ClassSkin, "Headgear", string.Empty);
					SetEquipmentItem((Model as PigGameData).ClassSkin, "Headgear_Back", "_Back");
					SetPirateVariants((Model as PigGameData).ClassSkin);
				}
				else
				{
					SetEquipmentItem(Model.ClassItem, "Headgear", string.Empty);
					SetEquipmentItem(Model.ClassItem, "Headgear_Back", "_Back");
					SetPirateVariants(Model.ClassItem);
				}
				if (showItems)
				{
					SetEquipmentItem(Model.MainHandItem, "MainHand", string.Empty);
					SetEquipmentItem(Model.OffHandItem, "OffHand", string.Empty);
					if (m_BoneAnimation.GetBoneTransform("OffHand_Back") != null)
					{
						SetEquipmentItem(Model.OffHandItem, "OffHand_Back", "_Back");
					}
					if (m_BoneAnimation.GetBoneTransform("MainHand_Arrow") != null)
					{
						SetEquipmentItem(Model.MainHandItem, "MainHand_Arrow", "_Arrow");
					}
				}
				else
				{
					if (m_BoneAnimation.GetBoneTransform("MainHand") != null)
					{
						m_BoneAnimation.HideBone("MainHand", true);
					}
					if (m_BoneAnimation.GetBoneTransform("OffHand") != null)
					{
						m_BoneAnimation.HideBone("OffHand", true);
					}
				}
			}
		}
		else
		{
			SetBirdHeadgear();
			if (showItems)
			{
				SetEquipmentItem(Model.MainHandItem, "MainHand", string.Empty);
				SetEquipmentItem(Model.OffHandItem, "OffHand", string.Empty);
				if (m_BoneAnimation.GetBoneTransform("MainHand_Arrow") != null)
				{
					SetEquipmentItem(Model.MainHandItem, "MainHand_Arrow", "_Arrow");
				}
			}
			else
			{
				m_BoneAnimation.HideBone("MainHand", true);
				m_BoneAnimation.HideBone("OffHand", true);
			}
		}
		if (Model is PigGameData)
		{
			SetForgeFeatures(Model.ClassItem);
		}
		m_EquipmentSetOnce = true;
	}

	private void SetBirdHeadgear()
	{
		if (Model is BirdGameData && (Model as BirdGameData).ClassSkin != null && Model.ClassItem != null && Model.ClassItem.BalancingData.NameId == (Model as BirdGameData).ClassSkin.BalancingData.OriginalClass)
		{
			SetEquipmentItem((Model as BirdGameData).ClassSkin, "Headgear", string.Empty);
			SetEquipmentItem((Model as BirdGameData).ClassSkin, "Headgear_Back", "_Back");
		}
		else
		{
			SetEquipmentItem(Model.ClassItem, "Headgear", string.Empty);
			SetEquipmentItem(Model.ClassItem, "Headgear_Back", "_Back");
		}
	}

	protected virtual void SetEquipmentItem(IInventoryItemGameData itemGameData, string boneName, string suffix = "")
	{
		if (base.HasNoBoneAnimation)
		{
			return;
		}
		var animationBone = m_BoneAnimation.mBoneSource.FirstOrDefault(b => b.boneName == boneName);
		if (animationBone == null)
		{
			return;
		}
		var boneIndex = animationBone.boneNodeIndex;
		var triggerFrame = m_BoneAnimation.triggerFrames.FirstOrDefault(f => f.GetTriggerFrameBone(boneIndex) != null && !string.IsNullOrEmpty(f.GetTriggerFrameBone(boneIndex).originalTextureGUID));
		if (triggerFrame == null)
		{
			DebugLog.Error("Original Texture is never set!");
			return;
		}
		var triggerFrameBone = triggerFrame.GetTriggerFrameBone(boneIndex);
		if (itemGameData == null || string.IsNullOrEmpty(itemGameData.ItemAssetName))
		{
			m_BoneAnimation.HideBone(boneName, true);
			return;
		}
		var itemAssetName = GetHeadgearPlacement(itemGameData.ItemAssetName.Replace("_Ancient", string.Empty));
		if (!SwapBoneToTexture(boneName, triggerFrameBone.originalTextureGUID, itemAssetName + suffix))
		{
			m_BoneAnimation.HideBone(boneName, true);
		}
	}

	// Headgear drawn in several placements on the head; each character picks one once (weights in percent) and wears
	// texture <asset>_<n> (plus <asset>_<n>_Back when that placement has one).
	private static readonly Dictionary<string, int[]> RandomPlacementHeadgear = new Dictionary<string, int[]>
	{
		{ "Headgear_PirateBucket", new[] { 30, 30, 30, 10 } }
	};

	private int m_HeadgearPlacement = -1;

	private string GetHeadgearPlacement(string itemAssetName)
	{
		int[] weights;
		if (!RandomPlacementHeadgear.TryGetValue(itemAssetName, out weights))
		{
			return itemAssetName;
		}
		if (m_HeadgearPlacement < 0 || m_HeadgearPlacement >= weights.Length)
		{
			var roll = UnityEngine.Random.Range(0, weights.Sum());
			m_HeadgearPlacement = 0;
			while (roll >= weights[m_HeadgearPlacement])
			{
				roll -= weights[m_HeadgearPlacement];
				m_HeadgearPlacement++;
			}
		}
		return itemAssetName + "_" + (m_HeadgearPlacement + 1);
	}

	// Swaps boneName's original texture for textureName from whichever of the rig's atlases has it.
	// Returns false (and changes nothing) when no atlas has it.
	private bool SwapBoneToTexture(string boneName, string originalTextureGUID, string textureName)
	{
		TextureAtlas textureAtlas = null;
		var textureAtlases = m_BoneAnimation.textureAtlases;
		foreach (var textureAtlas2 in textureAtlases)
		{
			if (textureAtlas2 == null)
			{
				Debug.LogError(GetType() + ": There is a null value referenced as a texture atlas on a BoneAnimation script!", base.gameObject);
			}
			else if (textureAtlas2.textureNames.Contains(textureName))
			{
				textureAtlas = textureAtlas2;
				TextureName = textureAtlas.name;
			}
		}
		if (textureAtlas == null)
		{
			return false;
		}
		var num = -1;
		TextureAtlas textureAtlas3 = null;
		var textureAtlases2 = m_BoneAnimation.textureAtlases;
		foreach (var textureAtlas4 in textureAtlases2)
		{
			num = textureAtlas4.GetTextureIndex(originalTextureGUID);
			if (num != -1)
			{
				textureAtlas3 = textureAtlas4;
				break;
			}
		}
		m_BoneAnimation.HideBone(boneName, false);
		if (m_EquipmentSetOnce)
		{
			m_BoneAnimation.RestoreBoneTexture(boneName);
		}
		m_BoneAnimation.SwapBoneTexture(boneName, textureAtlas3.name, textureAtlas3.textureNames[num], TextureName, textureName);
		return true;
	}

	private readonly List<string> m_PirateSwappedBones = new List<string>();

	// Pirate-aligned pigs (wearing a pirate hat) swap every texture X their bones use for the rig's "XPirate"
	// texture, when one of the rig's atlases has it: BodyPirate (MoustachePig, PigBrute), Mouth_KOPirate etc.
	// (PigBrute). A bone like Mouth changes texture per animation, so each of its textures is swapped on its own.
	private void SetPirateVariants(IInventoryItemGameData headgear)
	{
		if (base.HasNoBoneAnimation)
		{
			return;
		}
		foreach (var swappedBone in m_PirateSwappedBones)
		{
			m_BoneAnimation.RestoreBoneTexture(swappedBone);
		}
		m_PirateSwappedBones.Clear();
		var headgearName = headgear == null || string.IsNullOrEmpty(headgear.ItemAssetName) ? string.Empty : headgear.ItemAssetName;
		if (!headgearName.Contains("Pirate") && !headgearName.Contains("RedBeard"))
		{
			return;
		}
		var atlases = m_BoneAnimation.textureAtlases.Where(a => a != null).ToList();
		foreach (var bone in m_BoneAnimation.mBoneSource)
		{
			var originalGUIDs = m_BoneAnimation.triggerFrames.Select(f => f.GetTriggerFrameBone(bone.boneNodeIndex)).Where(b => b != null && !string.IsNullOrEmpty(b.originalTextureGUID)).Select(b => b.originalTextureGUID).Distinct();
			foreach (var guid in originalGUIDs)
			{
				var originalAtlas = atlases.FirstOrDefault(a => a.GetTextureIndex(guid) != -1);
				if (originalAtlas == null)
				{
					continue;
				}
				var originalName = originalAtlas.textureNames[originalAtlas.GetTextureIndex(guid)];
				var pirateAtlas = atlases.FirstOrDefault(a => a.textureNames.Contains(originalName + "Pirate"));
				if (pirateAtlas == null)
				{
					continue;
				}
				m_BoneAnimation.SwapBoneTexture(bone.boneName, originalAtlas.name, originalName, pirateAtlas.name, originalName + "Pirate");
				if (!m_PirateSwappedBones.Contains(bone.boneName))
				{
					m_PirateSwappedBones.Add(bone.boneName);
				}
			}
		}
	}

	private const string ForgeAssetPrefix = "Forge_";

	private readonly List<string> m_ForgeSwappedBones = new List<string>();

	// Combatant Forge (Tools/CombatantForge): a forged combatant's class item is called Forge_<id>. Its atlas holds
	// Forge_<id>__<Bone> textures for non-equipment bones (Snout, Eye_Left, Mouth...), each replacing every texture of
	// that bone. Rigs with _A/_B/_C bones use the name without the suffix for all three.
	private void SetForgeFeatures(IInventoryItemGameData classItem)
	{
		if (base.HasNoBoneAnimation)
		{
			return;
		}
		foreach (var swappedBone in m_ForgeSwappedBones)
		{
			m_BoneAnimation.RestoreBoneTexture(swappedBone);
		}
		m_ForgeSwappedBones.Clear();
		var assetName = classItem == null || string.IsNullOrEmpty(classItem.ItemAssetName) ? string.Empty : classItem.ItemAssetName;
		if (!assetName.StartsWith(ForgeAssetPrefix))
		{
			return;
		}
		var atlases = m_BoneAnimation.textureAtlases.Where(a => a != null).ToList();
		foreach (var bone in m_BoneAnimation.mBoneSource)
		{
			var boneKey = bone.boneName;
			if (boneKey.EndsWith("_A") || boneKey.EndsWith("_B") || boneKey.EndsWith("_C"))
			{
				boneKey = boneKey.Substring(0, boneKey.Length - 2);
			}
			var forgeName = assetName + "__" + boneKey;
			var forgeAtlas = atlases.FirstOrDefault(a => a.textureNames.Contains(forgeName));
			if (forgeAtlas == null)
			{
				continue;
			}
			// Drops a pirate swap on this bone: the forged texture replaces all of the bone's textures anyway.
			m_BoneAnimation.RestoreBoneTexture(bone.boneName);
			var originalGUIDs = m_BoneAnimation.triggerFrames.Select(f => f.GetTriggerFrameBone(bone.boneNodeIndex)).Where(b => b != null && !string.IsNullOrEmpty(b.originalTextureGUID)).Select(b => b.originalTextureGUID).Distinct();
			foreach (var guid in originalGUIDs)
			{
				var originalAtlas = atlases.FirstOrDefault(a => a.GetTextureIndex(guid) != -1);
				if (originalAtlas == null)
				{
					continue;
				}
				m_BoneAnimation.SwapBoneTexture(bone.boneName, originalAtlas.name, originalAtlas.textureNames[originalAtlas.GetTextureIndex(guid)], forgeAtlas.name, forgeName);
			}
			m_ForgeSwappedBones.Add(bone.boneName);
		}
	}

	private readonly List<GameObject> m_CarriedHandItems = new List<GameObject>();

	// Shows or hides the main/off hand item (cutscenes; the world map hides hand items). itemOwner's item is
	// shown instead of our own when given. If this rig's atlases lack that texture (world map low-res rigs),
	// the item is carried as a quad built from the owner's atlas and pinned to the hand bone.
	public void ShowHandItem(InventoryItemType slot, bool show, CharacterAssetController itemOwner = null, float depthBehind = -1f, float turn = 0f)
	{
		var boneName = slot == InventoryItemType.MainHandEquipment ? "MainHand" : "OffHand";
		if (base.HasNoBoneAnimation || m_BoneAnimation == null || m_BoneAnimation.GetBoneTransform(boneName) == null)
		{
			return;
		}
		RemoveCarriedHandItems(boneName);
		var owner = itemOwner ?? this;
		var item = owner.Model == null ? null : slot == InventoryItemType.MainHandEquipment ? owner.Model.MainHandItem : owner.Model.OffHandItem;
		if (!show || item == null || string.IsNullOrEmpty(item.ItemAssetName))
		{
			m_BoneAnimation.HideBone(boneName, true);
			return;
		}
		var textureName = item.ItemAssetName.Replace("_Ancient", string.Empty);
		if (m_BoneAnimation.textureAtlases.Any(a => a != null && a.textureNames.Contains(textureName)))
		{
			SetEquipmentItem(item, boneName, string.Empty);
			return;
		}
		var atlas = owner.m_BoneAnimation == null ? null : owner.m_BoneAnimation.textureAtlases.FirstOrDefault(a => a != null && a.textureNames.Contains(textureName));
		if (atlas == null)
		{
			DebugLog.Warn(GetType(), "ShowHandItem: no atlas has " + textureName);
			m_BoneAnimation.HideBone(boneName, true);
			return;
		}
		m_BoneAnimation.HideBone(boneName, true);
		var hand = m_BoneAnimation.GetBoneTransform(boneName);
		var visual = hand.Find(boneName + "_Sprite") ?? hand;
		var carried = new GameObject("Carried_" + boneName + "_" + textureName);
		carried.transform.SetParent(visual, false);
		// Slightly towards the camera by default, so the item draws over the carrier's body.
		carried.transform.position = visual.position + new Vector3(0f, 0f, depthBehind);
		carried.transform.localRotation = Quaternion.Euler(0f, 0f, turn);
		var sprite = carried.AddComponent<CHMeshSprite>();
		sprite.m_AtlasType = AtlasTypes.SmoothMoves;
		sprite.m_SmoothMovesAtlas = atlas;
		sprite.m_SpriteName = textureName;
		sprite.UpdateSprite(true, true);
		UnityHelper.SetLayerRecusively(carried, base.gameObject.layer);
		m_CarriedHandItems.Add(carried);
	}

	private void RemoveCarriedHandItems(string boneName = null)
	{
		for (var i = m_CarriedHandItems.Count - 1; i >= 0; i--)
		{
			var carried = m_CarriedHandItems[i];
			if (carried == null || boneName == null || carried.name.StartsWith("Carried_" + boneName + "_"))
			{
				if (carried != null)
				{
					Object.Destroy(carried);
				}
				m_CarriedHandItems.RemoveAt(i);
			}
		}
	}

	// SmoothMoves' clip indexer logs an error for unknown names; the rig's legacy Animation lookup is silent.
	public bool HasClipSilently(string animationName)
	{
		if (base.HasNoBoneAnimation || m_BoneAnimation == null)
		{
			return true;
		}
		var clips = m_BoneAnimation.GetComponent<Animation>();
		return clips == null || clips[animationName] != null;
	}

	protected virtual void RegisterEventHandler()
	{
		DeregisterEventHandler();
		if (Model != null && Model.InventoryGameData != null)
		{
			Model.InventoryGameData.InventoryOfTypeChanged += InventoryGameData_InventoryOfTypeChanged;
		}
	}

	protected virtual void DeregisterEventHandler()
	{
		if (Model != null && Model.InventoryGameData != null)
		{
			Model.InventoryGameData.InventoryOfTypeChanged -= InventoryGameData_InventoryOfTypeChanged;
		}
	}

	public virtual void PlayDefeatAnim()
	{
		if (Model.CharacterFaction == Faction.Pigs)
		{
			base.gameObject.StopAnimationOrAnimatorState(new List<string> { "Idle", "Hit", "Affected", "Cheer" }, this);
		}
		else
		{
			base.gameObject.StopAnimationOrAnimatorState(new List<string> { "Idle", "Hit", "Cheer" }, this);
		}
		PlayAnimation("Defeated");
	}

	public virtual void PlayKnockoutAnim()
	{
		PlayAnimation("KnockedOut");
	}

	public virtual IEnumerator PlayDefeatAnimation()
	{
		yield return PlayAnimationAndWaitForFinish("Defeated");
	}

	public virtual IEnumerator PlayKnockOutAnimation()
	{
		yield return PlayAnimationAndWaitForFinish("KnockedOut");
	}

	public virtual float GetDefeatAnimationLength()
	{
		return GetAnimationLength("Defeated");
	}

	public virtual float GetKnockOutAnimationLength()
	{
		return GetAnimationLength("KnockedOut");
	}

	public virtual void PlayHitAnim()
	{
		if (this is BannerAssetController || this is BossAssetController || base.HasNoBoneAnimation || !m_BoneAnimation.IsPlaying("Action_Rage"))
		{
			PlayAnimation("Hit");
			PlayAnimationQueued("Idle");
		}
	}
	
	public virtual void PlayHideAnimation()
	{
		base.PlayAnimation("Hide");
	}

	public virtual float GetHitAnimationLength()
	{
		return GetAnimationLength("Hit");
	}

	public virtual void PlayAffectedAnim()
	{
		PlayAnimation("Affected");
		PlayAnimationQueued("Idle");
	}

	public virtual void PlayIdleAnimationQueued()
	{
		PlayAnimationQueued("Idle");
	}

	public virtual float GetAffectedAnimationLength()
	{
		return GetAnimationLength("Affected");
	}

	public virtual void PlayCheerAnim()
	{
		if (this is BannerAssetController || this is BossAssetController || base.HasNoBoneAnimation || !m_BoneAnimation.IsPlaying("Defeated"))
		{
			PlayAnimation("Cheer");
			PlayAnimationQueued("Idle");
		}
	}

	public virtual float GetCheerAnimationLength()
	{
		return GetAnimationLength("Cheer");
	}

	public virtual void PlayAttackAnim(bool useOffhand)
	{
		if (Model.MainHandItem == null)
		{
			PlayAnimation("Action_Attack_1");
		}
		else
		{
			PlayAnimation("Action_Attack_" + (!useOffhand ? Model.MainHandItem.BalancingData.AnimationIndex : Model.OffHandItem.BalancingData.AnimationIndex));
		}
		PlayAnimationQueued("Idle");
	}

	public virtual void PlaySecondaryAttackAnim()
	{
		PlayAnimation("Action_Attack_4");
		PlayAnimationQueued("Idle");
	}

	public virtual void PlayFocusOffHandAnimation()
	{
		PlayAnimation(HasClipSilently("Focus_OffHand") ? "Focus_OffHand" : "Cheer");
		PlayAnimationQueued("Idle");
	}

	public virtual void PlayFocusWeaponAnimation()
	{
		PlayAnimation(HasClipSilently("Focus_MainHand") ? "Focus_MainHand" : "Cheer");
		PlayAnimationQueued("Idle");
	}

	public virtual float GetAttackAnimationLength()
	{
		if (Model.MainHandItem == null)
		{
			return GetAnimationLength("Action_Attack_1");
		}
		return GetAnimationLength("Action_Attack_" + Model.MainHandItem.BalancingData.AnimationIndex);
	}

	public virtual float GetSecondaryAttackAnimationLength()
	{
		return GetAnimationLength("Action_Attack_4");
	}

	public virtual void PlayFailAnim()
	{
		PlayAnimation("Action_Fail");
		PlayAnimationQueued("Idle");
	}

	public virtual float GetFailAnimationLength()
	{
		return GetAnimationLength("Action_Fail");
	}

	public virtual void PlaySupportAnim()
	{
		PlayAnimation("Action_Support");
		PlayAnimationQueued("Idle");
	}

	public virtual float GetSupportAnimationLength()
	{
		return GetAnimationLength("Action_Support");
	}

	public virtual void PlayLaughAnim()
	{
		PlayAnimation("Action_Laugh");
		PlayAnimationQueued("Idle");
	}

	public virtual float GetLaughAnimationLength()
	{
		return GetAnimationLength("Action_Laugh");
	}

	public virtual void PlayRageSkillAnim()
	{
		PlayAnimation("Action_Rage");
		PlayAnimationQueued("Idle");
	}

	public virtual float GetRageSkillAnimationLength()
	{
		return GetAnimationLength("Action_Rage");
	}

	public virtual void PlayRageAnim(bool isStunned)
	{
		PlayAnimation("Rage");
		if (isStunned)
		{
			PlayAnimationQueued("Stunned");
		}
		else
		{
			PlayAnimationQueued("Idle");
		}
	}

	public virtual float GetRageAnimationLength()
	{
		return GetAnimationLength("Rage");
	}

	public void PlaySuprisedAnim(bool isStunned)
	{
		PlayAnimation("Surprised");
		if (isStunned)
		{
			PlayAnimationQueued("Stunned");
		}
		else
		{
			PlayAnimationQueued("Idle");
		}
	}

	public float GetSuprisedAnimationLength()
	{
		return GetAnimationLength("Surprised");
	}

	public void SetModel(string characterId, bool isWorldMap, bool showEquipment, bool useScaleController)
	{
		ICharacter model = null;
		BirdBalancingData balancing;
		PigBalancingData balancing2;
		BossBalancingData balancing3;
		if (DIContainerBalancing.Service.TryGetBalancingData<BirdBalancingData>(characterId, out balancing))
		{
			model = new BirdGameData(characterId);
		}
		else if (DIContainerBalancing.Service.TryGetBalancingData<PigBalancingData>(characterId, out balancing2))
		{
			model = new PigGameData(characterId).SetDifficulties(DIContainerInfrastructure.GetCurrentPlayer().Data.Level, null);
		}
		else if (DIContainerBalancing.Service.TryGetBalancingData<BossBalancingData>(characterId, out balancing3))
		{
			model = new BossGameData(characterId).SetDifficulties(DIContainerInfrastructure.GetCurrentPlayer().Data.Level, null);
		}
		SetModel(model, isWorldMap, showEquipment, useScaleController);
	}

	public virtual void PlayMournAnim()
	{
		PlayAnimation("Mourn");
		PlayAnimationQueued("Idle");
		WatchMournSound();
	}

	private const string MournSoundTrigger = "PlaySound_Mourn";

	private Coroutine m_MournSoundRoutine;

	private bool m_MournSoundFired;

	private bool m_MournTriggerListening;

	// Blink is an additive layer-1 clip (the brute's snort) that can animate the bone
	// carrying Mourn's PlaySound_Mourn trigger; while it is still mixed in, SmoothMoves
	// drops that trigger and the mourn plays silently. If that happens, fire the sound
	// ourselves once the snort has finished.
	private void WatchMournSound()
	{
		if (m_MournSoundRoutine != null)
		{
			StopCoroutine(m_MournSoundRoutine);
			m_MournSoundRoutine = null;
		}
		if (base.HasNoBoneAnimation || m_BoneAnimation == null || !base.gameObject.activeInHierarchy || !HasClipSilently("Blink") || !m_BoneAnimation.IsPlaying("Blink"))
		{
			return;
		}
		var sounds = m_BoneAnimation.GetComponent<TriggerSoundByBoneAnimation>();
		if (sounds == null || !sounds.m_SoundTriggerPairs.Any(p => p.TriggerId == MournSoundTrigger))
		{
			return;
		}
		var triggerFrame = FirstMournTriggerFrame();
		if (triggerFrame < 0)
		{
			return;
		}
		m_MournSoundRoutine = StartCoroutine(PlayMournSoundAfterSnort(sounds, triggerFrame));
	}

	private int FirstMournTriggerFrame()
	{
		var clipIndex = ClipIndexOf("Mourn");
		if (clipIndex < 0)
		{
			return -1;
		}
		var frames = m_BoneAnimation.triggerFrames
			.Where(f => f.clipIndex == clipIndex && f.triggerFrameBones.Any(b => b.userTriggerTag == MournSoundTrigger))
			.Select(f => f.frame)
			.ToList();
		return frames.Count == 0 ? -1 : frames.Min();
	}

	// The runtime BoneAnimation keeps its clip list private; triggerFrames reference clips by index into it.
	private int ClipIndexOf(string animationName)
	{
		const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
		var field = m_BoneAnimation.GetType().GetField("mAnimationClips", flags);
		var clips = field != null ? field.GetValue(m_BoneAnimation) as IList : null;
		if (clips == null)
		{
			return -1;
		}
		for (var i = 0; i < clips.Count; i++)
		{
			var clip = clips[i];
			var nameField = clip != null ? clip.GetType().GetField("animationName", flags) : null;
			if (nameField != null && (nameField.GetValue(clip) as string) == animationName)
			{
				return i;
			}
		}
		return -1;
	}

	private IEnumerator PlayMournSoundAfterSnort(TriggerSoundByBoneAnimation sounds, int triggerFrame)
	{
		m_MournSoundFired = false;
		ListenForMournTrigger(true);
		// SmoothMoves AnimationState.time is in frames.
		while (m_BoneAnimation.IsPlaying("Mourn") && (m_BoneAnimation.IsPlaying("Blink") || m_BoneAnimation["Mourn"].time < triggerFrame))
		{
			yield return null;
		}
		// Give the rig's own trigger one evaluation to fire if the snort ended in time.
		yield return null;
		ListenForMournTrigger(false);
		if (!m_MournSoundFired && m_BoneAnimation.IsPlaying("Mourn"))
		{
			sounds.OnTriggerEventFired(new UserTriggerEvent { tag = MournSoundTrigger });
		}
		m_MournSoundRoutine = null;
	}

	private void ListenForMournTrigger(bool listen)
	{
		if (m_BoneAnimation == null || listen == m_MournTriggerListening)
		{
			return;
		}
		if (listen)
		{
			m_BoneAnimation.RegisterUserTriggerDelegate(OnMournUserTrigger);
		}
		else
		{
			m_BoneAnimation.UnregisterUserTriggerDelegate(OnMournUserTrigger);
		}
		m_MournTriggerListening = listen;
	}

	private void OnMournUserTrigger(UserTriggerEvent triggerEvent)
	{
		if (triggerEvent.tag == MournSoundTrigger)
		{
			m_MournSoundFired = true;
		}
	}

	public float GetMournAnimationLength()
	{
		return GetAnimationLength("Mourn");
	}

	public void PlayTauntAnim()
	{
		PlayAnimation("Taunt");
		PlayAnimationQueued("Idle");
	}

	public float GetPlayTauntAnimationLength()
	{
		return GetAnimationLength("Taunt");
	}

	public void PlayIdleAnimation()
	{
		PlayAnimation("Idle");
		CancelInvoke();
		PlayAnimation("Blink");
		Invoke("PlayBlinkAnimation", Random.Range(MinMaxTimeForBlink.x, MinMaxTimeForBlink.y));
	}

	public virtual void PlayStunnedAnimation()
	{
		StopAnimation("Idle");
		CancelInvoke();
		PlayAnimationQueued("Stunned");
	}

	public virtual void PlayAffectedAnimQueued(string returnAnimation = "Idle")
	{
		StopAnimation(returnAnimation);
		PlayAnimationQueued("Affected");
		PlayAnimationQueued(returnAnimation);
	}

	public void PlayReviveAnim()
	{
		if (Model is BirdGameData || Model.IsPvPBird)
		{
			PlayAnimation("KnockedOutEnd");
		}
		else
		{
			PlayAnimation("Resurrection");
		}
		PlayAnimationQueued("Idle");
	}

	public float GetReviveAnimationLength()
	{
		return GetAnimationLength("Resurrection");
	}

	public void PlayAttentionAnimation()
	{
		PlayAnimation("Action_Attention");
		PlayAnimationQueued("Idle");
	}

	public float GetAttentionAnimationLength()
	{
		return GetAnimationLength("Action_Attention");
	}

	public void StopAnimations()
	{
		m_BoneAnimation.Stop();
	}
}
