using UnityEngine;

[AddComponentMenu("Chimera/TriggerSoundByAnimation")]
public class TriggerSoundByAnimation : MonoBehaviour
{
	// Skips PlaySound events (the bird unlock popup plays the bird's theme instead of its jingle).
	public bool m_Muted;

	public void PlaySound(string nameOfSound)
	{
		if (m_Muted)
		{
			return;
		}
		var audioManager = DIContainerInfrastructure.AudioManager;
		if (audioManager != null)
		{
			audioManager.PlaySound(nameOfSound, base.gameObject);
		}
	}

	public void PlaySoundFromDefaultSource(string nameOfSound)
	{
		DIContainerInfrastructure.AudioManager.PlaySound(nameOfSound);
	}

	public void StopSound(string nameOfSound)
	{
		DIContainerInfrastructure.AudioManager.StopSound(nameOfSound);
	}
}
