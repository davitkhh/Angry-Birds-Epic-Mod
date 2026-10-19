using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ActionTreeEditor.Nodes
{
    public class MoveByClipNode : GameObjectNode
    {
        public MoveByClipNode(ActionNode node) : base(node)
        {
        }

        protected override void DrawOptions()
        {
            base.DrawOptions();

            Node.refObject = EditorGUILayout.ObjectField(
                new GUIContent("Clip", "Legacy clip whose position motion is played, e.g. a prop's entrance."),
                Node.refObject as AnimationClip,
                typeof(AnimationClip),
                false
            );

            Node.text = EditorGUILayout.TextField(
                new GUIContent("Curve path", "Path of the animated transform in the clip, e.g. Animation. Empty for the clip's root."),
                Node.text
            );

            Node.refObject1 = EditorGUILayout.ObjectField(
                new GUIContent("Curve space", "Transform the curve's positions are local to (the animated transform's parent in the prop)."),
                Node.refObject1 as Transform,
                typeof(Transform),
                true
            );

            Node.customInt2 = EditorGUILayout.Popup(
                new GUIContent("Mode", "Entrance: the motion ends where the object stands. Exit: starts where it stands and ends where the clip ends."),
                Node.customInt2,
                new[] { new GUIContent("Entrance (end here)"), new GUIContent("Exit (start here)") }
            );

            Node.customBool = EditorGUILayout.Toggle(
                new GUIContent("Start pose only", "Only puts the object where the clip starts; a later node plays the motion."),
                Node.customBool
            );

            Node.customInt = EditorGUILayout.Toggle(new GUIContent("Wait until it ends"), Node.customInt == 1) ? 1 : 0;
        }

        protected override Rect GetDefaultNodeSize()
        {
            return new Rect(0, 0, 220, 230);
        }
    }
}
