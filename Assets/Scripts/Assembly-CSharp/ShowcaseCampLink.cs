using UnityEngine;

public class ShowcaseCampLink : MonoBehaviour
{
	public string m_ShowcaseId = ShowcaseCamps.PiggyMcCool;

	public UIInputTrigger m_Trigger;

	private void OnEnable()
	{
		if (m_Trigger == null)
		{
			m_Trigger = GetComponent<UIInputTrigger>();
		}
		if (m_Trigger)
		{
			m_Trigger.Clicked -= Trigger_Clicked;
			m_Trigger.Clicked += Trigger_Clicked;
		}
	}

	private void OnDisable()
	{
		if (m_Trigger)
		{
			m_Trigger.Clicked -= Trigger_Clicked;
		}
	}

	private void Trigger_Clicked()
	{
		if (ClientInfo.IsFriend)
		{
			return;
		}
		DIContainerInfrastructure.GetCoreStateMgr().GotoShowcaseCampScreen(m_ShowcaseId);
	}
}
