using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Draws a HealthForecast over a CharacterHealthBar while a move is hovered over a target:
// - hits: white over the health that will be lost, damage over time in the effect's colour (each tick flashing
//   in turn order, further left), heals in green to the right of the current health. 70% at most, faded in/out.
// - random changes sit at the far end of the stack, their opacity scaled by the chance.
// - boosted amounts: the extra part gets a tint of the boosting effect's colour; weakened amounts are tinted over.
// - maximum health changes: the lost chunk fades while the rest takes the bar's rounded tip / the gained chunk
//   fades in right of the bar. On release the bar keeps that size until the change happens, then drifts back.
// - a sure knock-out: the killing chunk fades up to 100% and a crossed-out skull fades above the bar.
public class HealthBarPreview : MonoBehaviour
{
	private const float MaxAlpha = 0.7f;

	private const float SteadyPeriod = 1.6f;

	private const float FlashDuration = 1f;

	private const float TickStep = 0.45f;

	private const float CyclePause = 0.35f;

	private const float HoldAfterChange = 0.35f;

	private const float DriftDuration = 0.6f;

	private const float HoldTimeout = 10f;

	private enum State
	{
		Idle,
		Preview,
		Hold,
		Drift
	}

	private struct Segment
	{
		public float From;

		public float To;

		public Texture2D Texture;

		public Color Color;

		// Opacity at the peak of the fade (chance and tint strength already applied)
		public float Alpha;

		public int Tick;

		// Fades with the steady pulse rather than the tick sequence (maximum health chunks)
		public bool Steady;

		// Tints draw over the base layers
		public bool Tint;

		// Uv range of the texture to show
		public float UvFrom;

		public float UvTo;
	}

	private static Texture2D s_White;

	private static Texture2D s_Health;

	private static Texture2D s_Skull;

	private CharacterHealthBar m_Bar;

	private UISprite m_Current;

	private UISprite m_Changed;

	private Transform m_Root;

	private int m_CurrentWidth;

	private int m_ChangedWidth;

	private Vector3 m_CurrentPos;

	private Vector3 m_ChangedPos;

	private readonly List<UITexture> m_Pool = new List<UITexture>();

	private readonly List<Segment> m_Segments = new List<Segment>();

	private UITexture m_Skull;

	private float m_SkullX;

	private bool m_Lethal;

	// A sure damage over time tick that knocks the target out (impact not lethal): small skull on its overlay
	private int m_KillTick = -1;

	private int m_MaxTick;

	private State m_State;

	private float m_StartTime;

	private float m_Scale = 1f;

	private float m_OldMax;

	private float m_NewMax;

	private float m_ChangedAt = -1f;

	private float m_DriftStart;

	private int m_NoInputFrames;

	private bool m_AddingTint;

	private float Now
	{
		get { return Time.realtimeSinceStartup; }
	}

	public bool IsBusy
	{
		get { return m_State == State.Hold || m_State == State.Drift; }
	}

	private bool Init()
	{
		if (m_Bar != null)
		{
			return m_Current != null;
		}
		m_Bar = GetComponent<CharacterHealthBar>();
		if (m_Bar == null || m_Bar.CurrentBar == null)
		{
			return false;
		}
		m_Current = m_Bar.CurrentBar;
		m_Changed = m_Bar.ChangedBar;
		m_CurrentWidth = m_Current.width;
		m_CurrentPos = m_Current.transform.localPosition;
		if (m_Changed != null)
		{
			m_ChangedWidth = m_Changed.width;
			m_ChangedPos = m_Changed.transform.localPosition;
		}
		if (s_White == null)
		{
			s_White = Resources.Load<Texture2D>("HealthPreview/Bar_White");
			s_Health = Resources.Load<Texture2D>("HealthPreview/Bar_Health");
			s_Skull = Resources.Load<Texture2D>("HealthPreview/KillSkull");
		}
		var root = new GameObject("HealthPreview");
		root.layer = m_Current.gameObject.layer;
		m_Root = root.transform;
		m_Root.parent = m_Current.transform.parent;
		m_Root.localRotation = m_Current.transform.localRotation;
		m_Root.localScale = m_Current.transform.localScale;
		m_Root.localPosition = m_CurrentPos + new Vector3(0f, 0f, -0.5f);
		return true;
	}

	// Left edge of the bar in m_Root space (the bar sprite is centred)
	private float Left
	{
		get { return -m_CurrentWidth * 0.5f; }
	}

	public bool Show(HealthForecast forecast)
	{
		if (forecast == null || !Init() || s_White == null)
		{
			return false;
		}
		if (IsBusy)
		{
			FinishDrift();
		}
		if (m_State == State.Preview)
		{
			// the previous hover may have shrunk the bar
			RestoreBar();
		}
		BuildSegments(forecast);
		m_State = State.Preview;
		m_StartTime = Now;
		m_NoInputFrames = 0;
		ApplyScale(m_Scale);
		ApplyPreviewFill();
		Render();
		return true;
	}

	public void Hide()
	{
		if (m_State != State.Preview)
		{
			return;
		}
		m_State = State.Idle;
		HideOverlays();
		RestoreBar();
	}

	// The move was released on the target: keep a changed maximum health's size until it happens.
	public void Commit()
	{
		if (m_State != State.Preview)
		{
			return;
		}
		HideOverlays();
		if (Mathf.Abs(m_NewMax - m_OldMax) < 0.5f)
		{
			m_State = State.Idle;
			RestoreBar();
			return;
		}
		m_State = State.Hold;
		m_StartTime = Now;
		m_ChangedAt = -1f;
		m_Scale = m_NewMax / m_OldMax;
		ApplyScale(m_Scale);
		ApplyHoldFill();
	}

	private void BuildSegments(HealthForecast forecast)
	{
		m_Segments.Clear();
		m_OldMax = forecast.OldMaxHealth;
		m_NewMax = forecast.NewMaxHealth;
		m_Lethal = forecast.IsLethal;
		m_KillTick = m_Lethal ? -1 : forecast.LethalTick;
		if (m_KillTick == 0)
		{
			m_KillTick = -1;
		}
		var lower = m_NewMax < m_OldMax - 0.5f;
		var raise = m_NewMax > m_OldMax + 0.5f;
		// While hovering, a lowered maximum shrinks the bar to its new size; a raised one shows the extra chunk
		m_Scale = lower ? m_NewMax / m_OldMax : 1f;
		var extent = raise ? m_NewMax / m_OldMax : 1f;
		var health = Mathf.Min(forecast.Combatant.CurrentHealth, m_NewMax) / m_OldMax;
		if (lower)
		{
			var filledTo = Mathf.Min(1f, forecast.Combatant.CurrentHealth / m_OldMax);
			if (filledTo > m_Scale)
			{
				AddSegment(m_Scale, filledTo, s_Health, Color.white, MaxAlpha, 0, true, 1f, 1f);
			}
			AddSegment(Mathf.Max(m_Scale, filledTo), 1f, s_White, Color.white, MaxAlpha, 0, true, 1f, 1f);
		}
		if (raise)
		{
			AddSegment(1f, extent, s_Health, Color.white, MaxAlpha, 0, true, extent, extent);
		}
		// Damage stacks leftwards from the current health: sure changes in turn order, then random ones
		var cursor = health;
		var damage = forecast.Changes.Where(c => !c.IsHeal && c.Amount > 0).ToList();
		var killed = 0f;
		foreach (var change in damage.Where(c => c.Guaranteed).OrderBy(c => c.Tick).Concat(damage.Where(c => !c.Guaranteed).OrderBy(c => c.Tick)))
		{
			var lethalPart = m_Lethal && change.Tick == 0 && change.Guaranteed;
			var peak = (lethalPart ? 1f : MaxAlpha) * change.Chance;
			var baseAmount = change.Amount - Mathf.Clamp(change.BonusAmount, 0, change.Amount);
			var from = cursor;
			cursor = StackDamage(cursor, baseAmount, change, peak, false, extent);
			cursor = StackDamage(cursor, change.Amount - baseAmount, change, peak, true, extent);
			if (lethalPart || (m_KillTick > 0 && change.Tick == m_KillTick && change.Guaranteed && from > 0f && cursor <= 0.0001f))
			{
				killed = (from + cursor) * 0.5f;
			}
			if (cursor <= 0f)
			{
				break;
			}
		}
		// Heals stack rightwards up to the (new) maximum
		cursor = health;
		var cap = m_NewMax / m_OldMax;
		var heals = forecast.Changes.Where(c => c.IsHeal && c.Amount > 0).ToList();
		foreach (var change in heals.Where(c => c.Guaranteed).OrderBy(c => c.Tick).Concat(heals.Where(c => !c.Guaranteed).OrderBy(c => c.Tick)))
		{
			var peak = MaxAlpha * change.Chance;
			var baseAmount = change.Amount - Mathf.Clamp(change.BonusAmount, 0, change.Amount);
			cursor = StackHeal(cursor, baseAmount, change, peak, false, cap, extent);
			cursor = StackHeal(cursor, change.Amount - baseAmount, change, peak, true, cap, extent);
			if (cursor >= cap)
			{
				break;
			}
		}
		m_MaxTick = m_Segments.Where(s => !s.Steady).Select(s => s.Tick).DefaultIfEmpty(0).Max();
		m_SkullX = Left + killed * m_CurrentWidth;
	}

	private float StackDamage(float cursor, int amount, HealthChange change, float peak, bool bonus, float extent)
	{
		if (amount <= 0 || cursor <= 0f)
		{
			return cursor;
		}
		var from = Mathf.Max(0f, cursor - amount / m_OldMax);
		AddLayers(from, cursor, change, peak, bonus, extent);
		return from;
	}

	private float StackHeal(float cursor, int amount, HealthChange change, float peak, bool bonus, float cap, float extent)
	{
		if (amount <= 0 || cursor >= cap)
		{
			return cursor;
		}
		var to = Mathf.Min(cap, cursor + amount / m_OldMax);
		AddLayers(cursor, to, change, peak, bonus, extent);
		return to;
	}

	private void AddLayers(float from, float to, HealthChange change, float peak, bool bonus, float extent)
	{
		m_AddingTint = false;
		AddRange(from, to, s_White, change.BaseColor, peak, change.Tick, extent);
		m_AddingTint = true;
		if (bonus && change.BonusTintAlpha > 0f)
		{
			AddRange(from, to, s_White, change.BonusTint, change.BonusTintAlpha * change.Chance, change.Tick, extent);
		}
		if (change.ReducedTintAlpha > 0f)
		{
			AddRange(from, to, s_White, change.ReducedTint, change.ReducedTintAlpha * change.Chance, change.Tick, extent);
		}
		m_AddingTint = false;
	}

	// A range of the health bar; past the bar's end (raised maximum) it takes the bar's right tip.
	private void AddRange(float from, float to, Texture2D texture, Color color, float alpha, int tick, float extent)
	{
		if (to <= from)
		{
			return;
		}
		if (from < 1f && to > 1f)
		{
			AddRange(from, 1f, texture, color, alpha, tick, extent);
			AddRange(1f, to, texture, color, alpha, tick, extent);
			return;
		}
		if (from >= 1f)
		{
			AddSegment(from, to, texture, color, alpha, tick, false, extent, extent);
		}
		else
		{
			// inside the bar: map onto the bar as drawn (shrunk to m_Scale while a lowered maximum is shown)
			AddSegment(from, to, texture, color, alpha, tick, false, m_Scale, 1f);
		}
	}

	private void AddSegment(float from, float to, Texture2D texture, Color color, float alpha, int tick, bool steady, float uvExtent, float extent)
	{
		if (to - from <= 0.0001f || alpha <= 0.001f)
		{
			return;
		}
		float uvFrom;
		float uvTo;
		if (from >= 1f && extent > 1f)
		{
			// beyond the bar: the last (extent - 1) of the texture, so the chunk ends in the rounded tip
			uvFrom = 1f - (extent - from);
			uvTo = 1f - (extent - to);
		}
		else
		{
			uvFrom = from / Mathf.Max(0.0001f, uvExtent);
			uvTo = to / Mathf.Max(0.0001f, uvExtent);
		}
		m_Segments.Add(new Segment
		{
			From = from,
			To = to,
			Texture = texture,
			Color = color,
			Alpha = alpha,
			Tick = tick,
			Steady = steady,
			Tint = m_AddingTint,
			UvFrom = Mathf.Clamp01(uvFrom),
			UvTo = Mathf.Clamp01(uvTo)
		});
	}

	private UITexture GetTexture(int index)
	{
		while (m_Pool.Count <= index)
		{
			var go = new GameObject("Overlay");
			go.layer = m_Root.gameObject.layer;
			go.transform.parent = m_Root;
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = Vector3.one;
			var texture = go.AddComponent<UITexture>();
			texture.pivot = UIWidget.Pivot.Left;
			texture.height = m_Current.height;
			m_Pool.Add(texture);
		}
		return m_Pool[index];
	}

	private float Envelope(Segment segment, float time)
	{
		if (segment.Steady || m_MaxTick == 0)
		{
			return 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * time / SteadyPeriod);
		}
		var cycle = m_MaxTick * TickStep + FlashDuration + CyclePause;
		var local = time % cycle - segment.Tick * TickStep;
		if (local < 0f || local > FlashDuration)
		{
			return 0f;
		}
		return Mathf.Sin(Mathf.PI * local / FlashDuration);
	}

	private void Render()
	{
		var time = Now - m_StartTime;
		for (var i = 0; i < m_Segments.Count; i++)
		{
			var segment = m_Segments[i];
			var texture = GetTexture(i);
			if (!texture.gameObject.activeSelf)
			{
				texture.gameObject.SetActive(true);
			}
			if (texture.mainTexture != segment.Texture)
			{
				texture.mainTexture = segment.Texture;
			}
			texture.depth = m_Current.depth + (segment.Tint ? 2 : 1);
			texture.height = m_Current.height;
			texture.width = Mathf.Max(2, Mathf.RoundToInt((segment.To - segment.From) * m_CurrentWidth));
			texture.transform.localPosition = new Vector3(Left + segment.From * m_CurrentWidth, 0f, 0f);
			texture.uvRect = new Rect(segment.UvFrom, 0f, segment.UvTo - segment.UvFrom, 1f);
			var color = segment.Color;
			color.a = segment.Alpha * Envelope(segment, time);
			texture.color = color;
		}
		for (var j = m_Segments.Count; j < m_Pool.Count; j++)
		{
			if (m_Pool[j].gameObject.activeSelf)
			{
				m_Pool[j].gameObject.SetActive(false);
			}
		}
		RenderSkull(time);
	}

	private void RenderSkull(float time)
	{
		if ((!m_Lethal && m_KillTick <= 0) || s_Skull == null)
		{
			if (m_Skull != null && m_Skull.gameObject.activeSelf)
			{
				m_Skull.gameObject.SetActive(false);
			}
			return;
		}
		if (m_Skull == null)
		{
			var go = new GameObject("KillSkull");
			go.layer = m_Root.gameObject.layer;
			go.transform.parent = m_Root;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = Vector3.one;
			m_Skull = go.AddComponent<UITexture>();
			m_Skull.mainTexture = s_Skull;
			m_Skull.pivot = UIWidget.Pivot.Bottom;
		}
		if (!m_Skull.gameObject.activeSelf)
		{
			m_Skull.gameObject.SetActive(true);
		}
		m_Skull.depth = m_Current.depth + 3;
		float envelope;
		if (m_Lethal)
		{
			var height = Mathf.RoundToInt(m_Current.height * 2.6f);
			m_Skull.height = height;
			m_Skull.width = Mathf.RoundToInt(height * (float)s_Skull.width / s_Skull.height);
			m_Skull.pivot = UIWidget.Pivot.Bottom;
			// centred over the bar, a little above it
			m_Skull.transform.localPosition = new Vector3(0f, m_Current.height * 0.5f + 4f, -0.1f);
			// fades with the killing (impact) chunk, up to 100%
			envelope = m_MaxTick == 0 ? 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * time / SteadyPeriod) : Envelope(new Segment { Tick = 0 }, time);
		}
		else
		{
			// small, centred on the killing tick's overlay and flashing with it
			var height = Mathf.RoundToInt(m_Current.height * 1.1f);
			m_Skull.height = height;
			m_Skull.width = Mathf.RoundToInt(height * (float)s_Skull.width / s_Skull.height);
			m_Skull.pivot = UIWidget.Pivot.Center;
			m_Skull.transform.localPosition = new Vector3(m_SkullX, 0f, -0.1f);
			envelope = Envelope(new Segment { Tick = m_KillTick }, time);
		}
		m_Skull.color = new Color(1f, 1f, 1f, envelope);
	}

	private void HideOverlays()
	{
		foreach (var texture in m_Pool)
		{
			if (texture != null && texture.gameObject.activeSelf)
			{
				texture.gameObject.SetActive(false);
			}
		}
		if (m_Skull != null)
		{
			m_Skull.gameObject.SetActive(false);
		}
	}

	private void ApplyScale(float scale)
	{
		if (m_Current == null)
		{
			return;
		}
		var width = Mathf.Max(2, Mathf.RoundToInt(m_CurrentWidth * scale));
		m_Current.width = width;
		m_Current.transform.localPosition = m_CurrentPos + new Vector3((width - m_CurrentWidth) * 0.5f * m_Current.transform.localScale.x, 0f, 0f);
		if (m_Changed != null)
		{
			var changedWidth = Mathf.Max(2, Mathf.RoundToInt(m_ChangedWidth * scale));
			m_Changed.width = changedWidth;
			m_Changed.transform.localPosition = m_ChangedPos + new Vector3((changedWidth - m_ChangedWidth) * 0.5f * m_Changed.transform.localScale.x, 0f, 0f);
		}
	}

	private void ApplyPreviewFill()
	{
		if (m_Scale < 1f && m_Bar.Model != null)
		{
			var fill = Mathf.Clamp01(Mathf.Min(m_Bar.Model.CurrentHealth, m_NewMax) / m_NewMax);
			m_Current.fillAmount = fill;
			if (m_Changed != null)
			{
				m_Changed.fillAmount = fill;
			}
		}
	}

	// Until the maximum actually changes the bar shows today's health against the new maximum
	private void ApplyHoldFill()
	{
		if (m_Bar.Model == null)
		{
			return;
		}
		var fill = Mathf.Clamp01(Mathf.Min(m_Bar.Model.CurrentHealth, m_NewMax) / m_NewMax);
		m_Current.fillAmount = fill;
		if (m_Changed != null)
		{
			m_Changed.fillAmount = fill;
		}
	}

	private void RestoreBar()
	{
		m_Scale = 1f;
		ApplyScale(1f);
		if (m_Bar != null && m_Bar.Model != null && m_Bar.Model.ModifiedHealth > 0f)
		{
			var fill = Mathf.Clamp01(m_Bar.Model.CurrentHealth / m_Bar.Model.ModifiedHealth);
			m_Current.fillAmount = fill;
			if (m_Changed != null)
			{
				m_Changed.fillAmount = fill;
			}
		}
	}

	private void FinishDrift()
	{
		m_State = State.Idle;
		m_Scale = 1f;
		ApplyScale(1f);
	}

	private void LateUpdate()
	{
		switch (m_State)
		{
		case State.Preview:
			// A drag that ended without a release reaching us (input locked, scene change, ...)
			if (!Input.GetMouseButton(0) && Input.touchCount == 0)
			{
				if (++m_NoInputFrames > 2)
				{
					Hide();
					return;
				}
			}
			else
			{
				m_NoInputFrames = 0;
			}
			ApplyPreviewFill();
			Render();
			break;
		case State.Hold:
			if (m_Bar.Model == null)
			{
				FinishDrift();
				break;
			}
			if (m_ChangedAt < 0f && Mathf.Abs(m_Bar.Model.ModifiedHealth - m_OldMax) >= 0.5f)
			{
				m_ChangedAt = Now;
			}
			if (m_ChangedAt < 0f)
			{
				ApplyHoldFill();
				if (Now - m_StartTime > HoldTimeout)
				{
					m_ChangedAt = Now - HoldAfterChange;
				}
			}
			else if (Now - m_ChangedAt >= HoldAfterChange)
			{
				m_State = State.Drift;
				m_DriftStart = Now;
			}
			break;
		case State.Drift:
		{
			var t = Mathf.Clamp01((Now - m_DriftStart) / DriftDuration);
			ApplyScale(Mathf.Lerp(m_Scale, 1f, Mathf.SmoothStep(0f, 1f, t)));
			if (t >= 1f)
			{
				FinishDrift();
			}
			break;
		}
		}
	}

	private void OnDisable()
	{
		if (m_State != State.Idle && m_Current != null)
		{
			m_State = State.Idle;
			HideOverlays();
			RestoreBar();
		}
	}
}
