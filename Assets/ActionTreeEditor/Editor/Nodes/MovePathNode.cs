using UnityEditor;
using UnityEngine;

namespace ActionTreeEditor.Nodes
{
    public class MovePathNode : GameObjectNode
    {
        public MovePathNode(ActionNode node) : base(node)
        {
        }

        protected override void DrawOptions()
        {
            base.DrawOptions();

            EditorGUILayout.LabelField(new GUIContent("Points (world)", "One \"x,y,z\" per line, optionally followed by \";x,y,z\", the point on the ground under it."));
            var text = string.Join("\n", Node.param ?? new string[0]);
            var edited = EditorGUILayout.TextArea(text, GUILayout.Height(80));
            if (edited != text)
            {
                Node.param = edited.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            }
            Node.customFloat = EditorGUILayout.FloatField(new GUIContent("Speed (units/s)"), Node.customFloat);
            Node.customFloat2 = EditorGUILayout.FloatField(new GUIContent("Duration (s)", "Above 0: the move takes this long instead of going at the speed."), Node.customFloat2);
            Node.text = EditorGUILayout.TextField(new GUIContent("Bone clip on the way", "Looped while moving, then Idle. Empty keeps the current clip."), Node.text);
            Node.customBool2 = EditorGUILayout.Toggle(new GUIContent("Clip from a random point"), Node.customBool2);
            Node.customInt2 = EditorGUILayout.Popup(new GUIContent("Face the way it moves"), Node.customInt2, new[] { new GUIContent("No"), new GUIContent("Pig rig"), new GUIContent("Bird") });
            Node.refObject2 = EditorGUILayout.ObjectField(new GUIContent("Ground shadow", "Moved along the ground points."), Node.refObject2, typeof(GameObject), true);
            Node.customBool = EditorGUILayout.Toggle(new GUIContent("Hide the shadow at the end"), Node.customBool);
            Node.customInt = EditorGUILayout.Toggle(new GUIContent("Wait until it arrives"), Node.customInt == 1) ? 1 : 0;
        }

        protected override Rect GetDefaultNodeSize()
        {
            return new Rect(0, 0, 230, 360);
        }
    }
}
