using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SLZReflectionProbe : MonoBehaviour
{
	[SerializeField]
	float m_importance;
	[SerializeField]
	float m_intensity;
	[SerializeField]
	uint m_projection;
	[SerializeField]
	Bounds m_projectionBounds;
	[SerializeField]
	Bounds m_blendingBounds;
	
}
