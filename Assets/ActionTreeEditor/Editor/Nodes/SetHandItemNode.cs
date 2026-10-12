using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ActionTreeEditor.Nodes
{
    public class SetHandItemNode : GameObjectNode
    {
        public SetHandItemNode(ActionNode node) : base(node)
        {
        }

        protected override void DrawOptions()
        {
            base.DrawOptions();

            GUIUtils.Dropdown(
                "Hand",
                Node.customInt,
                i => Node.customInt = i,
                "Off hand",
                "Main hand"
            );

            Node.customBool = EditorGUILayout.Toggle(
                new GUIContent("Show", "Off hides the item. The world map hides hand items unless a node shows them."),
                Node.customBool
            );

            if (Node.customBool)
            {
                Node.secondaryText = EditorGUILayout.TextField(
                    new GUIContent("Item from (object name)", "Optional. Shows this saved character's item instead of the object's own, e.g. a stolen off hand. Hide it on that character with another node."),
                    Node.secondaryText
                );
            }
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
