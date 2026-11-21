using System;
using System.Collections.Generic;
using System.Globalization;
using ABH.GameDatas.Battle;
using UnityEngine;

// Overlay colours for the health bar preview, read from Resources/HealthPreview/EffectColors.txt
// (effect ident = the skill's AssetId -> the colour of its in-battle visual effect). Effects without a
// visual effect yet fall back to red.
public static class HealthPreviewColors
{
	public static readonly Color Default = new Color(0.88f, 0.1f, 0.1f);

	private static Dictionary<string, Color> s_Colors;

	private static Color s_Heal = new Color(0.69f, 1f, 0.29f);

	private static Color s_Fallback = Default;

	public static Color Heal
	{
		get
		{
			Load();
			return s_Heal;
		}
	}

	public static Color Fallback
	{
		get
		{
			Load();
			return s_Fallback;
		}
	}

	public static Color ForEffect(BattleEffectGameData effect)
	{
		if (effect == null)
		{
			return Fallback;
		}
		return ForIdent(effect.m_EffectIdent);
	}

	public static Color ForIdent(string ident)
	{
		Load();
		Color color;
		if (!string.IsNullOrEmpty(ident) && s_Colors.TryGetValue(ident, out color))
		{
			return color;
		}
		return s_Fallback;
	}

	private static void Load()
	{
		if (s_Colors != null)
		{
			return;
		}
		s_Colors = new Dictionary<string, Color>(StringComparer.Ordinal);
		var asset = Resources.Load<TextAsset>("HealthPreview/EffectColors");
		if (asset == null)
		{
			return;
		}
		foreach (var rawLine in asset.text.Split('\n'))
		{
			var line = rawLine;
			var comment = line.IndexOf('#');
			if (comment >= 0)
			{
				line = line.Substring(0, comment);
			}
			var eq = line.IndexOf('=');
			if (eq <= 0)
			{
				continue;
			}
			var key = line.Substring(0, eq).Trim();
			var hex = line.Substring(eq + 1).Trim().TrimStart('#');
			int rgb;
			if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
			{
				continue;
			}
			var color = new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
			if (key == "@heal")
			{
				s_Heal = color;
			}
			else if (key == "@default")
			{
				s_Fallback = color;
			}
			else
			{
				s_Colors[key] = color;
			}
		}
	}
}
