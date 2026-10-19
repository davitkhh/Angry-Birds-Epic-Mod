using UnityEditor;
using UnityEngine;

namespace ActionTreeEditor.Nodes
{
    public class MoveToObjectNode : GameObjectNode
    {
        public MoveToObjectNode(ActionNode node) : base(node)
        {
        }

        protected override void DrawOptions()
        {
            base.DrawOptions();

            Node.secondaryText = EditorGUILayout.TextField(new GUIContent("Target (object name)", "Saved runtime object to move to. Empty: refObject1, or the mover itself."), Node.secondaryText);
            Node.refObject1 = EditorGUILayout.ObjectField(new GUIContent("Target (reference)"), Node.refObject1, typeof(Transform), true);
            Node.customVec1 = EditorGUILayout.Vector3Field(new GUIContent("Offset (world)"), Node.customVec1);
            Node.customBool = EditorGUILayout.Toggle(new GUIContent("Only along x"), Node.customBool);
            Node.customFloat = EditorGUILayout.FloatField(new GUIContent("Speed (units/s)"), Node.customFloat);
            Node.customFloat2 = EditorGUILayout.FloatField(new GUIContent("Duration (s)", "Above 0: the move takes this long instead of going at the speed."), Node.customFloat2);
            Node.text = EditorGUILayout.TextField(new GUIContent("Bone clip on the way", "Looped while moving, then Idle. Empty keeps the current clip."), Node.text);
            Node.customBool2 = EditorGUILayout.Toggle(new GUIContent("Clip from a random point"), Node.customBool2);
            Node.customInt2 = EditorGUILayout.Popup(new GUIContent("Face the way it moves"), Node.customInt2, new[] { new GUIContent("No"), new GUIContent("Pig rig"), new GUIContent("Bird") });
            Node.customInt = EditorGUILayout.Toggle(new GUIContent("Wait until it arrives"), Node.customInt == 1) ? 1 : 0;
        }

        protected override Rect GetDefaultNodeSize()
        {
            return new Rect(0, 0, 230, 280);
        }
    }
}
