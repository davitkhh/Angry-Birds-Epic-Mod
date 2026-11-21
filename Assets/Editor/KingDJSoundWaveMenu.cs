using UnityEditor;

// Toggle for the wave King DJ's Fog Machine fires (SoundWaveVisual.Style, stored in PlayerPrefs, so it also switches
// mid-play): Tools/Build/King DJ/Sound Wave Style/...
public static class KingDJSoundWaveMenu
{
	private const string Root = "Tools/Build/King DJ/Sound Wave Style/";

	private const string PhysicallyAccurate = Root + "Physically Accurate";

	private const string Longitudinal = Root + "Longitudinal";

	private const string Ribbon = Root + "Ribbon";

	[MenuItem(PhysicallyAccurate)]
	private static void SetPhysicallyAccurate()
	{
		SoundWaveVisual.Style = SoundWaveVisual.WaveStyle.PhysicallyAccurate;
	}

	[MenuItem(Longitudinal)]
	private static void SetLongitudinal()
	{
		SoundWaveVisual.Style = SoundWaveVisual.WaveStyle.Longitudinal;
	}

	[MenuItem(Ribbon)]
	private static void SetRibbon()
	{
		SoundWaveVisual.Style = SoundWaveVisual.WaveStyle.Ribbon;
	}

	[MenuItem(PhysicallyAccurate, true)]
	private static bool CheckPhysicallyAccurate()
	{
		return Check(PhysicallyAccurate, SoundWaveVisual.WaveStyle.PhysicallyAccurate);
	}

	[MenuItem(Longitudinal, true)]
	private static bool CheckLongitudinal()
	{
		return Check(Longitudinal, SoundWaveVisual.WaveStyle.Longitudinal);
	}

	[MenuItem(Ribbon, true)]
	private static bool CheckRibbon()
	{
		return Check(Ribbon, SoundWaveVisual.WaveStyle.Ribbon);
	}

	private static bool Check(string path, SoundWaveVisual.WaveStyle style)
	{
		Menu.SetChecked(path, SoundWaveVisual.Style == style);
		return true;
	}
}
