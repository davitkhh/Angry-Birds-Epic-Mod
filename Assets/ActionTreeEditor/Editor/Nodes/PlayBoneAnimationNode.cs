using ActionTreeEditor.Loca;
using UnityEditor;
using UnityEngine;

namespace ActionTreeEditor.Nodes
{
    public class PlayBoneAnimationNode : GameObjectNode
    {
        public PlayBoneAnimationNode(ActionNode node) : base(node)
        {
        }

        protected override void DrawOptions()
        {
            base.DrawOptions();
            
            Node.text = EditorGUILayout.TextField("Animation name", Node.text);
            
            GUIUtils.Dropdown(
                new GUIContent("After animation starts", Tooltips.WaitModeTooltip), 
                Node.customInt, 
                i => Node.customInt = i, 
                "Continue instantly", 
                "Wait for animation to finish", 
                "Wait for X seconds"
            );

            if (Node.customInt == 2)
            {
                Node.customFloat = EditorGUILayout.FloatField(
                    new GUIContent("Delay (s)", Tooltips.DelaySecondsTooltip),
                    Node.customFloat
                );
            }
            
            EditorGUILayout.Space();
            
            Node.QueueIdle = EditorGUILayout.Toggle(
                new GUIContent("Queue another animation", Tooltips.QueueIdleTooltip), 
                Node.QueueIdle
            );
            
            if (Node.QueueIdle)
            {
                Node.secondaryText = EditorGUILayout.TextField("Queued animation name", Node.secondaryText);
            }

            Node.customBool2 = EditorGUILayout.Toggle(
                new GUIContent("Random start point", "Starts the clip at a random point, e.g. to put a crowd's walk cycles out of step."),
                Node.customBool2
            );

            Node.customFloat2 = EditorGUILayout.FloatField(
                new GUIContent("Start at (s)", "Starts the clip this many seconds in, e.g. to skip a wind-up. 0 plays it from the start."),
                Node.customFloat2
            );
        }

        protected override Rect GetDefaultNodeSize()
        {
            return new Rect(0, 0, 200, 200);
        }
    }
}