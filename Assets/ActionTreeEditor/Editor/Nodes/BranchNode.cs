using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ActionTreeEditor.Nodes
{
    public class BranchNode : VisualNode
    {
        public BranchNode(ActionNode node) : base(node)
        {
        }

        protected override void DrawOptions()
        {
            Node.secondaryText = EditorGUILayout.TextField(
                new GUIContent("Toggle", "A StoryToggles name, e.g. PorkyStealsMcCoolBag. While it is on the tree goes on to the connected node."),
                Node.secondaryText
            );

            Node.customInt = EditorGUILayout.IntField(
                new GUIContent("When off, jump to node ID", "The node the tree continues at while the toggle is off."),
                Node.customInt
            );
        }

        protected override Rect GetDefaultNodeSize()
        {
            return new Rect(0, 0, 200, 150);
        }

        public override bool MatchesSearchFilters(Dictionary<SearchFilter, List<string>> filters)
        {
            return filters.TryGetValue(SearchFilter.ObjectName, out var names)
                && !string.IsNullOrEmpty(Node.secondaryText)
                && names.Any(name => Node.secondaryText.Contains(name));
        }
    }
}
