using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public class RageSplineLite : MonoBehaviour
{
	[SerializeField]
	private RageCurve m_SplineCurve;

	[SerializeField]
	private bool m_IsOpenEnded;

	#if UNITY_EDITOR
	[ContextMenu("Create from RageSpline")]
	#endif
	public void CreateFromSpline()
	{
		var component = GetComponent<RageSpline>();
		if (component)
		{
			m_SplineCurve = component.spline.Clone();
			m_IsOpenEnded = component.SplineIsOpenEnded();
		}
	}
	
	#if UNITY_EDITOR
	[ContextMenu("Create new RageSpline")]
	public void CreateNewSpline()
	{
		try
		{
			gameObject.AddComponent<RageSpline>();
		}
		catch (Exception)
		{
			// always blows up in awake xddd
		}
		var component = GetComponent<RageSpline>();
		if (component)
		{
			component.style = AssetDatabase.LoadAssetAtPath<RageSplineStyle>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("GroundPathStyle")[0]));
			component.spline = m_SplineCurve.Clone();
			component.VertexDensity = 10;
			component.enabled = true;
			component.Awake();
		}
	}
	#endif

	// Runtime path through these local points (straight between them), walked from first to last.
	public void SetPolyline(Vector3[] points)
	{
		var ctrl = new Vector3[points.Length * 2];
		var natural = new bool[points.Length];
		var width = new float[points.Length];
		for (var i = 0; i < width.Length; i++)
		{
			width[i] = 1f;
		}
		m_SplineCurve = new RageCurve(points, ctrl, natural, width);
		m_IsOpenEnded = true;
	}

	public Vector3 GetPositionWorldSpace(float splinePosition)
	{
		return base.transform.TransformPoint(m_SplineCurve.GetPoint(splinePosition * GetLastSplinePosition()));
	}

	public float GetLastSplinePosition()
	{
		if (m_IsOpenEnded)
		{
			return (float)(GetPointCount() - 1) / (float)GetPointCount();
		}
		return 1f;
	}

	public int GetPointCount()
	{
		return m_SplineCurve.points.Length;
	}

	public float GetLength()
	{
		return m_SplineCurve.GetLength(128, GetLastSplinePosition());
	}
}