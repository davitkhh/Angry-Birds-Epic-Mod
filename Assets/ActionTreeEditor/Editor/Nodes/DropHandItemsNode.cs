using UnityEditor;
using UnityEngine;

namespace ActionTreeEditor.Nodes
{
    public class DropHandItemsNode : GameObjectNode
    {
        public DropHandItemsNode(ActionNode node) : base(node)
        {
        }

        protected override void DrawOptions()
        {
            base.DrawOptions();

            Node.text = EditorGUILayout.TextField(new GUIContent("Bones", "e.g. MainHand,OffHand"), Node.text);
            Node.secondaryText = EditorGUILayout.TextField(new GUIContent("Saved as (prefix)", "Each dropped item is saved as prefix + bone name."), Node.secondaryText);
            Node.customBool = EditorGUILayout.Toggle(new GUIContent("Also collapse its shadow"), Node.customBool);
        }

        protected override Rect GetDefaultNodeSize()
        {
            return new Rect(0, 0, 230, 170);
        }
    }
}
