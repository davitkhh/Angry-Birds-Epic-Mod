using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ActionTreeEditor.Nodes
{
    public class ControlActionTreeNode : VisualNode
    {
        public ControlActionTreeNode(ActionNode node) : base(node)
        {
        }

        protected override void DrawOptions()
        {
            Node.refObject = EditorGUILayout.ObjectField(
                new GUIContent("Action tree", "Another tree in the scene, e.g. a story *_State tree whose idle loop should pause during this cutscene."),
                Node.refObject as ActionTree,
                typeof(ActionTree),
                true
            );

            Node.customBool = EditorGUILayout.Toggle(
                new GUIContent("Resume", "Off stops the tree. On restarts it at the node below, keeping the characters it spawned."),
                Node.customBool
            );

            if (Node.customBool)
            {
                Node.customInt = EditorGUILayout.IntField(
                    new GUIContent("Resume at node ID", "Usually the first node of the tree's loop."),
                    Node.customInt
                );
            }
        }

        protected override Rect GetDefaultNodeSize()
        {
            return new Rect(0, 0, 200, 150);
        }

        public override bool MatchesSearchFilters(Dictionary<SearchFilter, List<string>> filters)
        {
            return filters.TryGetValue(SearchFilter.ObjectName, out var objectNames)
                && Node.refObject != null
                && objectNames.Any(name => Node.refObject.name.Contains(name));
        }
    }
}
