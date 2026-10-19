using UnityEditor;
using UnityEngine;

namespace ActionTreeEditor.Nodes
{
    public class FaceDirectionNode : GameObjectNode
    {
        public FaceDirectionNode(ActionNode node) : base(node)
        {
        }

        protected override void DrawOptions()
        {
            base.DrawOptions();

            Node.customInt = EditorGUILayout.Toggle(new GUIContent("Face right"), Node.customInt == 1) ? 1 : 0;
            Node.customBool = EditorGUILayout.Toggle(new GUIContent("Faces right at scale +1", "On for birds, off for pig rigs."), Node.customBool);
            Node.customInt2 = EditorGUILayout.Toggle(new GUIContent("Flip its rig", "For scene pigs whose container must not flip."), Node.customInt2 == 1) ? 1 : 0;
        }

        protected override Rect GetDefaultNodeSize()
        {
            return new Rect(0, 0, 230, 170);
        }
    }
}
