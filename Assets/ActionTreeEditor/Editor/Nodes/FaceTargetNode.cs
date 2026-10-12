using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ActionTreeEditor.Nodes
{
    public class FaceTargetNode : GameObjectNode
    {
        public FaceTargetNode(ActionNode node) : base(node)
        {
        }

        protected override void DrawOptions()
        {
            base.DrawOptions();

            Node.secondaryText = EditorGUILayout.TextField(
                new GUIContent("Target (object name)", "Saved runtime object to keep facing. Empty stops tracking; the current facing is kept."),
                Node.secondaryText
            );

            Node.customBool = EditorGUILayout.Toggle(
                new GUIContent("Faces right at scale +1", "On for birds. Off for pig rigs, which face left at scale +1."),
                Node.customBool
            );
        }

        protected override Rect GetDefaultNodeSize()
        {
            return new Rect(0, 0, 200, 170);
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
