using System.Collections.Generic;
using System.Linq;
using ABH.GameDatas.Battle;
using ABH.GameDatas.Interfaces;
using ABH.Shared.Generic;
using UnityEngine;

// The health bar preview for the rage chili (fed by HealthPreviewDirector with a RageForecast):
// - rage a move surely adds fades in and out right of the current fill, up to 70%.
// - rage it surely empties: that part of the fill fades down to 30% and back.
// - random changes sit at the far right end of the racked-up rage, scaled by their chance: random fills point
//   right, random empties point left from the right end (over a sure fill they pulse between 20% and 45%,
//   past it they behave like an empty).
// - a move that surely fills the chili: the glow behind a full chili fades along with the overlay.
// Using the rage itself (emptying the chili) never shows anything.
public class RageMeterPreview : MonoBehaviour
{
	private const float GainAlpha = 0.7f;

	private const float DrainAlpha = 0.3f;

	// Random empties over a sure fill pulse between these instead
	private const float ChanceOverGainPeak = 0.45f;

	private const float ChanceOverGainRest = 0f;

	private const float Period = 1.6f;

	private struct Piece
	{
		public float From;

		public float To;

		// Opacity now and at the peak of the pulse
		public float Rest;

		public float Peak;
	}

	private struct Range
	{
		public float From;

		public float To;

		public float Chance;

		public bool Contains(float x)
		{
			return x > From && x < To;
		}
	}

	private static readonly List<RageMeterPreview> s_All = new List<RageMeterPreview>();

	private RageMeterController m_Controller;

	private UISprite m_Fill;

	private UIWidget m_GoldBody;

	private UISprite m_GlowSource;

	private UISprite m_Glow;

	private readonly List<UISprite> m_Pool = new List<UISprite>();

	private readonly List<float> m_PoolFrom = new List<float>();

	private readonly List<Piece> m_Pieces = new List<Piece>();

	private bool m_Showing;

	private float m_StartTime;

	private float m_BaseFill;

	private bool m_ShowGlow;

	private bool m_FadeGold;

	private int m_NoInputFrames;

	private float Now
	{
		get { return Time.realtimeSinceStartup; }
	}

	public static void Attach(RageMeterController controller)
	{
		if (controller == null || controller.GetComponent<RageMeterPreview>() != null)
		{
			return;
		}
		var preview = controller.gameObject.AddComponent<RageMeterPreview>();
		preview.m_Controller = controller;
		if (!s_All.Contains(preview))
		{
			s_All.Add(preview);
		}
	}

	private void OnEnable()
	{
		if (!s_All.Contains(this))
		{
			s_All.Add(this);
		}
	}

	private void OnDestroy()
	{
		s_All.Remove(this);
	}

	// Shows what source's move does to its side's chili; false when there is nothing to show.
	public static RageMeterPreview Show(BattleGameData battle, ICombatant source, RageForecast forecast, bool isRage)
	{
		var preview = s_All.FirstOrDefault(p => p != null && p.m_Controller != null && p.m_Controller.BattleMgr != null && p.m_Controller.BattleMgr.Model == battle);
		if (preview == null)
		{
			return null;
		}
		if (isRage || source == null || source.CombatantFaction != Faction.Birds || forecast == null || forecast.IsEmpty || !preview.Begin(battle, forecast))
		{
			preview.Hide();
			return null;
		}
		return preview;
	}

	private bool Init()
	{
		if (m_Fill != null)
		{
			return true;
		}
		if (m_Controller == null || m_Controller.RageMeterProgress == null)
		{
			return false;
		}
		m_Fill = m_Controller.RageMeterProgress;
		if (m_Controller.GoldBody != null)
		{
			m_GoldBody = m_Controller.GoldBody.GetComponent<UIWidget>();
		}
		var glow = m_Controller.transform.Find("Animation/RageOMeter/GlowEffect/Sprite");
		if (glow != null)
		{
			m_GlowSource = glow.GetComponent<UISprite>();
		}
		return true;
	}

	private bool Begin(BattleGameData battle, RageForecast forecast)
	{
		if (!Init() || !m_Fill.gameObject.activeInHierarchy || m_Controller.IsDragging)
		{
			return false;
		}
		if (m_Showing)
		{
			Restore();
		}
		var current = Mathf.Clamp01(battle.GetFactionRage(Faction.Birds) / 100f);
		Build(current, forecast);
		if (m_Pieces.Count == 0)
		{
			return false;
		}
		m_Showing = true;
		m_StartTime = Now;
		m_NoInputFrames = 0;
		Render();
		return true;
	}

	private void Build(float current, RageForecast forecast)
	{
		m_Pieces.Clear();
		// Sure changes first, in order
		var sure = current;
		foreach (var change in forecast.Changes.Where(c => c.Guaranteed))
		{
			sure = Mathf.Clamp01(sure + change.Amount / 100f);
		}
		// Random fills point right from there, random empties point left from the right end
		var cursor = sure;
		var gains = new List<Range>();
		foreach (var change in forecast.Changes.Where(c => !c.Guaranteed && c.Amount > 0f))
		{
			var to = Mathf.Min(1f, cursor + change.Amount / 100f);
			if (to > cursor)
			{
				gains.Add(new Range { From = cursor, To = to, Chance = change.Chance });
			}
			cursor = to;
		}
		var right = cursor;
		var drains = new List<Range>();
		foreach (var change in forecast.Changes.Where(c => !c.Guaranteed && c.Amount < 0f))
		{
			var from = Mathf.Max(0f, cursor + change.Amount / 100f);
			if (from < cursor)
			{
				drains.Add(new Range { From = from, To = cursor, Chance = change.Chance });
			}
			cursor = from;
		}
		m_BaseFill = Mathf.Min(current, Mathf.Min(sure, cursor));
		var high = Mathf.Max(current, right);
		var points = new List<float> { m_BaseFill, high, current, sure };
		points.AddRange(gains.SelectMany(r => new[] { r.From, r.To }));
		points.AddRange(drains.SelectMany(r => new[] { r.From, r.To }));
		points = points.Where(p => p >= m_BaseFill && p <= high).Distinct().OrderBy(p => p).ToList();
		for (var i = 0; i + 1 < points.Count; i++)
		{
			var from = points[i];
			var to = points[i + 1];
			if (to - from < 0.0005f)
			{
				continue;
			}
			var mid = (from + to) * 0.5f;
			var pg = gains.Where(r => r.Contains(mid)).Select(r => r.Chance).DefaultIfEmpty(0f).Max();
			var pd = drains.Where(r => r.Contains(mid)).Select(r => r.Chance).DefaultIfEmpty(0f).Max();
			float rest;
			float peak;
			if (mid < current)
			{
				rest = 1f;
				// surely emptied (maybe filled again by chance), or emptied by chance
				peak = mid > sure ? Mathf.Lerp(DrainAlpha, 1f, pg) : 1f;
				peak = Mathf.Lerp(peak, DrainAlpha, pd);
			}
			else if (mid < sure)
			{
				// surely filled; an empty by chance caps the peak at 45% (faded end stays at 0%)
				peak = pd > 0f ? ChanceOverGainPeak : GainAlpha;
				rest = ChanceOverGainRest * pd;
			}
			else if (pg > 0f)
			{
				peak = GainAlpha * pg;
				rest = DrainAlpha * pd * pg;
			}
			else
			{
				continue;
			}
			m_Pieces.Add(new Piece { From = from, To = to, Rest = rest, Peak = peak });
		}
		m_ShowGlow = current < 0.999f && sure >= 0.999f && drains.Count == 0;
		m_FadeGold = current >= 0.999f && m_Pieces.Any(p => p.Peak < p.Rest - 0.001f);
	}

	private UISprite GetSprite(int index)
	{
		while (m_Pool.Count <= index)
		{
			var go = new GameObject("RagePreview");
			go.layer = m_Fill.gameObject.layer;
			go.transform.parent = m_Fill.transform.parent;
			go.transform.localPosition = m_Fill.transform.localPosition;
			go.transform.localRotation = m_Fill.transform.localRotation;
			go.transform.localScale = m_Fill.transform.localScale;
			var sprite = go.AddComponent<UISprite>();
			sprite.atlas = m_Fill.atlas;
			sprite.spriteName = m_Fill.spriteName;
			sprite.type = UIBasicSprite.Type.Filled;
			sprite.fillDirection = m_Fill.fillDirection;
			sprite.invert = m_Fill.invert;
			sprite.pivot = m_Fill.pivot;
			sprite.width = m_Fill.width;
			sprite.height = m_Fill.height;
			sprite.depth = m_Fill.depth;
			sprite.color = m_Fill.color;
			var slot = m_Pool.Count;
			// fillAmount ends the piece; this cuts off the part left of its start
			sprite.onPostFill = (widget, offset, verts, uvs, cols) => ClipStart(slot, offset, verts, uvs);
			m_Pool.Add(sprite);
			m_PoolFrom.Add(0f);
		}
		return m_Pool[index];
	}

	private void ClipStart(int slot, int offset, List<Vector3> verts, List<Vector2> uvs)
	{
		var sprite = m_Pool[slot];
		if (verts.Count - offset < 4 || sprite.fillAmount <= 0.0001f || sprite.fillDirection != UIBasicSprite.FillDirection.Horizontal)
		{
			return;
		}
		var t = Mathf.Clamp01(m_PoolFrom[slot] / sprite.fillAmount);
		// horizontal fill quad: 0/1 on the start edge, 2/3 on the end edge (inverted: the other way round)
		var start = sprite.invert ? 2 : 0;
		var end = sprite.invert ? 0 : 2;
		var x = Mathf.Lerp(verts[offset + start].x, verts[offset + end].x, t);
		var u = Mathf.Lerp(uvs[offset + start].x, uvs[offset + end].x, t);
		for (var k = start; k < start + 2; k++)
		{
			verts[offset + k] = new Vector3(x, verts[offset + k].y, verts[offset + k].z);
			uvs[offset + k] = new Vector2(u, uvs[offset + k].y);
		}
	}

	private UISprite GetGlow()
	{
		if (m_Glow == null && m_GlowSource != null)
		{
			// The real glow is scaled to nothing until the chili is full (RageOMeter_Start): a copy at full size
			var effect = m_GlowSource.transform.parent;
			var holder = new GameObject("RageGlowPreview");
			holder.layer = m_GlowSource.gameObject.layer;
			holder.transform.parent = effect.parent;
			holder.transform.localPosition = effect.localPosition;
			holder.transform.localRotation = effect.localRotation;
			holder.transform.localScale = Vector3.one;
			var go = new GameObject("Sprite");
			go.layer = holder.layer;
			go.transform.parent = holder.transform;
			go.transform.localPosition = m_GlowSource.transform.localPosition;
			go.transform.localRotation = m_GlowSource.transform.localRotation;
			go.transform.localScale = m_GlowSource.transform.localScale;
			m_Glow = go.AddComponent<UISprite>();
			m_Glow.atlas = m_GlowSource.atlas;
			m_Glow.spriteName = m_GlowSource.spriteName;
			m_Glow.type = m_GlowSource.type;
			m_Glow.pivot = m_GlowSource.pivot;
			m_Glow.width = m_GlowSource.width;
			m_Glow.height = m_GlowSource.height;
			m_Glow.depth = m_GlowSource.depth;
			var color = m_GlowSource.color;
			color.a = 0f;
			m_Glow.color = color;
		}
		return m_Glow;
	}

	private void Render()
	{
		var time = Now - m_StartTime;
		var envelope = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * time / Period);
		m_Fill.fillAmount = m_BaseFill;
		for (var i = 0; i < m_Pieces.Count; i++)
		{
			var piece = m_Pieces[i];
			var sprite = GetSprite(i);
			if (!sprite.gameObject.activeSelf)
			{
				sprite.gameObject.SetActive(true);
			}
			if (Mathf.Abs(m_PoolFrom[i] - piece.From) > 0.00001f || Mathf.Abs(sprite.fillAmount - piece.To) > 0.00001f)
			{
				m_PoolFrom[i] = piece.From;
				sprite.fillAmount = piece.To;
				sprite.MarkAsChanged();
			}
			sprite.alpha = Mathf.Lerp(piece.Rest, piece.Peak, envelope);
		}
		for (var j = m_Pieces.Count; j < m_Pool.Count; j++)
		{
			if (m_Pool[j].gameObject.activeSelf)
			{
				m_Pool[j].gameObject.SetActive(false);
			}
		}
		var glow = m_ShowGlow ? GetGlow() : m_Glow;
		if (glow != null)
		{
			glow.transform.parent.gameObject.SetActive(m_ShowGlow);
			glow.alpha = GainAlpha * envelope;
		}
		if (m_GoldBody != null && m_FadeGold)
		{
			// a full chili's gold body hides the fill: fade it out so the emptied part shows
			m_GoldBody.alpha = 1f - envelope;
		}
	}

	public void Hide()
	{
		if (!m_Showing)
		{
			return;
		}
		m_Showing = false;
		Restore();
	}

	private void Restore()
	{
		foreach (var sprite in m_Pool)
		{
			if (sprite != null && sprite.gameObject.activeSelf)
			{
				sprite.gameObject.SetActive(false);
			}
		}
		if (m_Glow != null)
		{
			m_Glow.transform.parent.gameObject.SetActive(false);
		}
		if (m_GoldBody != null && m_FadeGold)
		{
			m_GoldBody.alpha = 1f;
		}
		m_FadeGold = false;
		// the fill goes back to the chili's rage (unless it's being animated or dragged right now)
		if (m_Fill != null && !m_Controller.m_Changing && !m_Controller.IsDragging && m_Controller.BattleMgr != null && m_Controller.BattleMgr.Model != null)
		{
			m_Fill.fillAmount = Mathf.Clamp01(m_Controller.BattleMgr.Model.GetFactionRage(Faction.Birds) / 100f);
		}
	}

	private void LateUpdate()
	{
		if (!m_Showing)
		{
			return;
		}
		// The chili got picked up, or a drag ended without a release reaching the preview
		if (m_Controller.IsDragging)
		{
			m_Showing = false;
			Restore();
			return;
		}
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
		Render();
	}

	private void OnDisable()
	{
		if (m_Showing)
		{
			m_Showing = false;
			Restore();
		}
	}
}
