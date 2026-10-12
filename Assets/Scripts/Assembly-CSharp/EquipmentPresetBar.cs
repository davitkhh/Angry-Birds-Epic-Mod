using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// One wooden bar in the preset list: name, the birds wearing the preset, and equip / edit / delete buttons.
public class EquipmentPresetBar : MonoBehaviour
{
	public UILabel m_NameLabel;

	public Transform m_BirdRoot;

	public UIInputTrigger m_EquipButton;

	public UISprite m_EquipButtonBody;

	public UIInputTrigger m_EditButton;

	public UIInputTrigger m_DeleteButton;

	// Shown when some of the preset's pieces have been scrapped or sold.
	public GameObject m_MissingMarker;

	public UILabel m_StatusLabel;

	[HideInInspector]
	public List<CharacterControllerCamp> m_Birds = new List<CharacterControllerCamp>();

	public void ShowStatus(string text)
	{
		if (m_StatusLabel == null)
		{
			return;
		}
		StopAllCoroutines();
		StartCoroutine(StatusRoutine(text));
	}

	private IEnumerator StatusRoutine(string text)
	{
		m_StatusLabel.text = text;
		m_StatusLabel.gameObject.SetActive(true);
		yield return new WaitForSeconds(2.5f);
		m_StatusLabel.gameObject.SetActive(false);
	}
}
