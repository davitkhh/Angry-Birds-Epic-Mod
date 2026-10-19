using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ActionTreeEditor.Nodes
{
    public class MoveArcNode : GameObjectNode
    {
        public MoveArcNode(ActionNode node) : base(node)
        {
        }

        protected override void DrawOptions()
        {
            base.DrawOptions();

            Node.secondaryText = EditorGUILayout.TextField(
                new GUIContent("Lowest point at (object name)", "Saved runtime object whose x is where the curve is lowest."),
                Node.secondaryText
            );

            Node.customFloat2 = EditorGUILayout.FloatField(
                new GUIContent("Dip", "How far below the start the curve gets at its lowest point."),
                Node.customFloat2
            );

            Node.customVec1.x = EditorGUILayout.FloatField(
                new GUIContent("End x past lowest point", "Where the flight ends, relative to the lowest point's x (negative = left)."),
                Node.customVec1.x
            );

            Node.customFloat = EditorGUILayout.FloatField(new GUIContent("Duration (s)"), Node.customFloat);

            Node.text = EditorGUILayout.TextField(
                new GUIContent("Bone clip", "Clip the mover plays during the flight, e.g. Move_Fly. Empty keeps the current one."),
                Node.text
            );

            Node.customInt = EditorGUILayout.Toggle(new GUIContent("Wait until it lands"), Node.customInt == 1) ? 1 : 0;
        }

        protected override Rect GetDefaultNodeSize()
        {
            return new Rect(0, 0, 220, 230);
        }

        public override bool MatchesSearchFilters(Dictionary<SearchFilter, List<string>> filters)
        {
            if (base.MatchesSearchFilters(filters))
                return true;

            return filters.TryGetValue(SearchFilter.ObjectName, out var objectNames)
                && !string.IsNullOrEmpty(Node.secondaryText)
                && objectNames.Any(name => Node.secondaryText.Contains(name));
        }
    }
}
