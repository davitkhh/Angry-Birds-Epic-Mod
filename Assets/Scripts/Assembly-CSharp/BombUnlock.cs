using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

// Bomb's unlock sequence (BirdUnlockSequence): a red-headband pirate (support, then attack tutorial), then five mateys.
// The middle matey holds a piece of Bomb's ship (the steering wheel), shows it off while the others watch, they all
// laugh, Bomb gets mad (rage animation + the rage potion's fill to full), the player uses the rage, the pigs are
// surprised as he jumps over, and Bomb picks the wheel up and cheers.
public static class BombUnlock
{
	private const string MiddleKey = "middle";

	private const string WheelKey = "wheel";

	private const string LaughKey = "laughing";

	public static BirdUnlockDefinition Create()
	{
		return new BirdUnlockDefinition
		{
			BirdNameId = "bird_black",
			BattleNameId = BirdUnlockSequence.BattlePrefix + "bird_black",
			ThemeClip = "BombsTheme",
			Steps = new List<BirdUnlockStep>
			{
				new BirdUnlockStep { Type = BirdUnlockStepType.Support, Wave = 0 },
				new BirdUnlockStep { Type = BirdUnlockStepType.Attack, Wave = 0 },
				new BirdUnlockStep { Type = BirdUnlockStepType.Rage, Wave = 1 }
			},
			OnWavePigsPlaced = WavePigsPlaced,
			OnWaveReady = WaveReady,
			OnBirdRageMove = BirdRageMove,
			Celebrate = Celebrate
		};
	}

	private static List<CharacterControllerBattleGroundBase> Pigs(BattleMgr battle)
	{
		return battle.Model.m_CombatantsPerFaction[Faction.Pigs].Where(p => p.IsParticipating && p.CombatantView != null).Select(p => p.CombatantView).ToList();
	}

	// Wave 2: the matey nearest the middle of the formation gets the wheel before they walk in.
	private static void WavePigsPlaced(BirdUnlockSequence sequence, BattleMgr battle, int wave)
	{
		if (wave != 1)
		{
			return;
		}
		var pigs = Pigs(battle);
		if (pigs.Count == 0)
		{
			return;
		}
		var centroid = Vector3.zero;
		foreach (var pig in pigs)
		{
			centroid += pig.CachedUnfocusedPos;
		}
		centroid /= pigs.Count;
		var middle = pigs.OrderBy(p => new Vector2(p.CachedUnfocusedPos.x - centroid.x, p.CachedUnfocusedPos.z - centroid.z).sqrMagnitude).First();
		sequence.Props[MiddleKey] = middle;
		var wheel = BirdUnlockWheel.Attach(middle, battle);
		if (wheel != null)
		{
			sequence.Props[WheelKey] = wheel;
			sequence.StartCoroutine(wheel.DropWhenCarrierFalls());
		}
	}

	private static IEnumerator WaveReady(BirdUnlockSequence sequence, BattleMgr battle, int wave)
	{
		if (wave != 1)
		{
			yield break;
		}
		object middleObject;
		sequence.Props.TryGetValue(MiddleKey, out middleObject);
		var middle = middleObject as CharacterControllerBattleGroundBase;
		var pigs = Pigs(battle);
		if (middle == null || pigs.Count == 0)
		{
			yield break;
		}
		yield return new WaitForSeconds(0.3f);
		// Everyone looks at the wheel: pigs face left (towards the birds), so the ones left of it turn round.
		foreach (var pig in pigs.Where(p => p != middle && p.transform.position.x < middle.transform.position.x))
		{
			RigFacing.Get(pig.m_AssetController.transform).Facing = -1f;
		}
		yield return new WaitForSeconds(0.4f);
		var showOff = BirdUnlockSequence.PlayFirstClip(middle, "Affected");
		middle.m_AssetController.PlayAnimationQueued("Idle");
		yield return new WaitForSeconds(Mathf.Max(showOff, 0.8f));
		sequence.Props[LaughKey] = true;
		foreach (var pig in pigs)
		{
			sequence.StartCoroutine(Laugh(sequence, pig));
		}
		yield return new WaitForSeconds(0.9f);
		// Bomb gets mad: his rage animation and the rage potion's effect, filling the chili to the top.
		var bird = sequence.BirdView;
		var fill = new SkillGameData("consumable_increase_rage", new Dictionary<string, float> { { "increase_rage", 100f } }).GenerateSkillBattleData();
		if (fill != null)
		{
			yield return battle.StartCoroutine(fill.DoAction(battle.Model, bird.GetModel(), bird.GetModel()));
		}
		yield return new WaitForSeconds(Mathf.Max(0.5f, bird.m_AssetController.GetRageAnimationLength()));
	}

	private static bool IsLaughing(BirdUnlockSequence sequence)
	{
		object laughing;
		return sequence.Props.TryGetValue(LaughKey, out laughing) && (bool)laughing;
	}

	// The pirate rig has no Taunt/Action_Laugh: its Cheer is its laugh.
	private static IEnumerator Laugh(BirdUnlockSequence sequence, CharacterControllerBattleGroundBase pig)
	{
		yield return new WaitForSeconds(Random.Range(0f, 0.2f));
		while (IsLaughing(sequence) && pig != null && pig.GetModel().IsAlive)
		{
			var length = BirdUnlockSequence.PlayFirstClip(pig, "Taunt", "Action_Laugh", "Cheer");
			if (length <= 0f)
			{
				yield break;
			}
			var end = Time.time + length;
			while (Time.time < end && IsLaughing(sequence))
			{
				yield return null;
			}
		}
	}

	// Bomb jumps over to them: they stop laughing, turn back to him and are surprised (Hit where a rig has no Surprised).
	private static void BirdRageMove(BirdUnlockSequence sequence, BattleMgr battle)
	{
		sequence.Props[LaughKey] = false;
		foreach (var pig in Pigs(battle))
		{
			var facing = pig.m_AssetController.GetComponent<RigFacing>();
			if (facing != null)
			{
				facing.Release();
			}
			if (pig.GetModel().IsAlive && BirdUnlockSequence.PlayFirstClip(pig, "Surprised", "Hit") > 0f)
			{
				pig.m_AssetController.PlayAnimationQueued("Idle");
			}
		}
	}

	// Back on his spot, Bomb takes the wheel into his hand and looks at it (Focus_MainHand), then cheers.
	private static IEnumerator Celebrate(BirdUnlockSequence sequence, BattleMgr battle)
	{
		var bird = sequence.BirdView;
		var timeout = 4f;
		while (timeout > 0f && (bird.transform.position - sequence.BirdSlot).magnitude > 3f)
		{
			timeout -= Time.deltaTime;
			yield return null;
		}
		yield return new WaitForSeconds(0.5f);
		object wheelObject;
		sequence.Props.TryGetValue(WheelKey, out wheelObject);
		var wheel = wheelObject as BirdUnlockWheel;
		var look = 0f;
		if (wheel != null && bird.m_AssetController.MainHandBone != null)
		{
			yield return sequence.StartCoroutine(wheel.HopInto(bird, 0.35f));
			look = BirdUnlockSequence.PlayFirstClip(bird, "Focus_MainHand");
		}
		yield return new WaitForSeconds(Mathf.Max(look, 0.3f));
		// Two cheers: the first holds the moment, the popup closes during the second, which carries on on the world map.
		yield return new WaitForSeconds(bird.PlayCheerCharacter());
		sequence.BirdClipEndsAt = Time.time + bird.PlayCheerCharacter();
	}
}

// A piece of Bomb's ship carried in a pig's main hand; dropped when the pig falls, then hopping into Bomb's hand. It is
// not parented to the rig: it follows the hand bone every frame (bone objects aren't reliably drawn or active).
[DefaultExecutionOrder(10100)]
public class BirdUnlockWheel : MonoBehaviour
{
	private const string PrefabPath = "Prefabs/1_WorldMap/2_Props/StorySequences/Prop_SteeringWheel";

	// Diameter in battle units (the prop's mesh is about 80 across).
	private const float Size = 64f;

	private const float MeshSize = 80f;

	private CharacterControllerBattleGroundBase m_Carrier;

	private BattleMgr m_Battle;

	private bool m_Dropped;

	private Transform m_FollowHand;

	private CharacterAssetController m_FollowRig;

	public static BirdUnlockWheel Attach(CharacterControllerBattleGroundBase carrier, BattleMgr battle)
	{
		var prefab = Resources.Load<GameObject>(PrefabPath);
		var hand = carrier.m_AssetController.MainHandBone;
		if (prefab == null || hand == null)
		{
			DebugLog.Error(typeof(BirdUnlockWheel), "No steering wheel prop or no main hand bone");
			return null;
		}
		var go = Instantiate(prefab, battle.m_BattleArea, false);
		go.name = "BirdUnlock_SteeringWheel";
		foreach (var destroyer in go.GetComponentsInChildren<DestroyGameObjectOnDisable>(true))
		{
			Destroy(destroyer);
		}
		foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
		{
			renderer.material.renderQueue = 3100;
		}
		var wheel = go.AddComponent<BirdUnlockWheel>();
		wheel.m_Carrier = carrier;
		wheel.m_Battle = battle;
		wheel.Follow(hand, carrier.m_AssetController);
		carrier.m_AssetController.ShowHandItem(InventoryItemType.MainHandEquipment, false);
		DebugLog.Log(typeof(BirdUnlockWheel), "Wheel on " + carrier.name + " at " + go.transform.position + " layer " + go.layer + " scale " + go.transform.lossyScale);
		return wheel;
	}

	private void Follow(Transform hand, CharacterAssetController rig)
	{
		m_FollowHand = hand;
		m_FollowRig = rig;
		UnityHelper.SetLayerRecusively(gameObject, rig.gameObject.layer);
		var parentScale = transform.parent != null ? Mathf.Max(0.0001f, Mathf.Abs(transform.parent.lossyScale.y)) : 1f;
		transform.localScale = Vector3.one * (Size / MeshSize / parentScale);
		LateUpdate();
	}

	private void LateUpdate()
	{
		if (m_FollowHand == null || m_FollowRig == null)
		{
			return;
		}
		var hand = m_FollowHand.position;
		transform.position = new Vector3(hand.x, hand.y, m_FollowRig.transform.position.z - 3f);
		transform.rotation = Quaternion.Euler(0f, 0f, m_FollowHand.eulerAngles.z);
	}

	// Leaves the hand as soon as the carrier's health is gone (before its defeat poof takes the rig away) and falls to
	// the ground where it stood.
	public IEnumerator DropWhenCarrierFalls()
	{
		while (!m_Dropped && m_Carrier != null && m_Carrier.GetModel() != null && m_Carrier.GetModel().CurrentHealth > 0f)
		{
			yield return null;
		}
		if (m_Dropped || this == null)
		{
			yield break;
		}
		m_Dropped = true;
		m_FollowHand = null;
		var ground = m_Carrier != null ? m_Carrier.transform.position.y : transform.position.y - 60f;
		var keepZ = transform.position.z;
		var rest = ground + Size * 0.45f;
		var velocity = new Vector2(Random.Range(-40f, 40f), 220f);
		var spin = Random.Range(-360f, 360f);
		const float gravity = -1400f;
		var bounces = 0;
		while (bounces < 2 && this != null && m_FollowHand == null)
		{
			velocity.y += gravity * Time.deltaTime;
			var p = transform.position + new Vector3(velocity.x, velocity.y, 0f) * Time.deltaTime;
			transform.Rotate(0f, 0f, spin * Time.deltaTime);
			if (p.y <= rest && velocity.y < 0f)
			{
				p.y = rest;
				velocity = new Vector2(velocity.x * 0.5f, -velocity.y * 0.3f);
				spin *= 0.4f;
				bounces++;
			}
			p.z = keepZ;
			transform.position = p;
			yield return null;
		}
	}

	// An arc from where it lies into the bird's main hand, which then holds it instead of its own weapon.
	public IEnumerator HopInto(CharacterControllerBattleGroundBase bird, float duration)
	{
		m_Dropped = true;
		m_FollowHand = null;
		var hand = bird.m_AssetController.MainHandBone;
		var start = transform.position;
		var startRotation = transform.rotation;
		var t = 0f;
		while (t < duration && hand != null)
		{
			t += Time.deltaTime;
			var x = Mathf.Clamp01(t / duration);
			var end = new Vector3(hand.position.x, hand.position.y, bird.m_AssetController.transform.position.z - 3f);
			var p = Vector3.Lerp(start, end, BirdUnlockSequence.Smooth(x));
			p.y += Mathf.Sin(x * Mathf.PI) * 90f;
			transform.position = p;
			transform.rotation = Quaternion.Slerp(startRotation, Quaternion.Euler(0f, 0f, hand.eulerAngles.z), x);
			yield return null;
		}
		if (hand != null)
		{
			bird.m_AssetController.ShowHandItem(InventoryItemType.MainHandEquipment, false);
			Follow(hand, bird.m_AssetController);
		}
	}
}
