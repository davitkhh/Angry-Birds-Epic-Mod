using UnityEditor;
using UnityEngine;

namespace ActionTreeEditor.Nodes
{
    public class ShowBubbleNode : GameObjectNode
    {
        public ShowBubbleNode(ActionNode node) : base(node)
        {
        }

        protected override void DrawOptions()
        {
            base.DrawOptions();

            Node.refObject = EditorGUILayout.ObjectField(new GUIContent("Bubble prefab"), Node.refObject as GameObject, typeof(GameObject), false);
            Node.customFloat = EditorGUILayout.FloatField(new GUIContent("Scale (0 = 1)"), Node.customFloat);
            Node.customFloat2 = EditorGUILayout.FloatField(new GUIContent("Lifetime (s, 0 = kept)"), Node.customFloat2);
        }

        protected override Rect GetDefaultNodeSize()
        {
            return new Rect(0, 0, 230, 170);
        }
    }
}
