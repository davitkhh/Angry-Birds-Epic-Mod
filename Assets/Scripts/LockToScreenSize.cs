using System.Linq;
using UnityEngine;

public class LockToScreenSize : MonoBehaviour
{
    [SerializeField] 
    public bool shouldUpdateWidth = true;
    [SerializeField] 
    public bool shouldUpdateHeight = true;
    [SerializeField] 
    public int padding = 4;

    private Camera m_fallbackCamera;
    
    private UIWidget m_overlaySprite;
    private BoxCollider m_collider;

    private void Awake()
    {
        m_overlaySprite = GetComponent<UIWidget>();
        m_collider = GetComponent<BoxCollider>();
    }
    
    private void Update()
    {
        var cam = DIContainerInfrastructure.GetCoreStateMgr()?.m_InterfaceCamera;
        
        if (cam == null || !cam.gameObject.activeInHierarchy)
        {
            if (m_fallbackCamera == null)
                m_fallbackCamera = Camera.allCameras.FirstOrDefault(c => c.gameObject.layer == LayerMask.NameToLayer("StorySequence"));
            
            if (m_fallbackCamera == null || !m_fallbackCamera.gameObject.activeInHierarchy)
                return;
            cam = m_fallbackCamera;
        }
        
        var height = (int)((cam.orthographicSize * 2));
        var width = (int)((height * cam.aspect));
        height += padding;
        width += padding;

        if (m_overlaySprite != null)
        {
            if (shouldUpdateWidth)
                m_overlaySprite.width = width;
            
            if (shouldUpdateHeight)
                m_overlaySprite.height = height;
        }

        if (m_collider != null)
        {
            m_collider.size = new Vector3(shouldUpdateWidth ? width : m_collider.size.x, shouldUpdateHeight ? height : m_collider.size.y, 0);
        }
    }
}
