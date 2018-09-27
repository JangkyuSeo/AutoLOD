using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.AutoLOD
{
    public class HLODGroupPopupContent : PopupWindowContent
    {
        private bool m_FocusTextEntry = false;
        private List<string> m_GroupNames;
        private Vector2 m_ScreenPosition;

        public string SelectGroupName { set; get; }

        public HLODGroupPopupContent(List<string> groupNames)
        {
            m_GroupNames = groupNames;
        }

        public override void OnGUI(Rect rect)
        {
            m_ScreenPosition = GUILayout.BeginScrollView(m_ScreenPosition);

            foreach (var name in m_GroupNames)
            {
                DrawNameField(name);
            }

            DrawNewField();
            GUILayout.EndScrollView();
        }

        private void DrawNameField(string name)
        {
            EditorGUILayout.BeginHorizontal();

            bool oldState = SelectGroupName == name;
            bool newState = false;

            newState = GUILayout.Toggle(oldState, new GUIContent(name), GUILayout.ExpandWidth(false));

            if (oldState != newState)
            {
                SelectGroupName = name;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        private void DrawNewField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("New"), GUILayout.Width(50), GUILayout.ExpandWidth(false));
            GUI.SetNextControlName("MaskTextEntry");
            var newEntry = EditorGUILayout.DelayedTextField("", GUILayout.ExpandWidth(true));
            //for return focus to text field.
            if (m_FocusTextEntry)
            {
                m_FocusTextEntry = false;
                GUI.FocusWindow(GUIUtility.hotControl);
                EditorGUI.FocusTextInControl("MaskTextEntry");
            }
            if (!string.IsNullOrEmpty(newEntry))
            {
                m_FocusTextEntry = true;
                m_GroupNames.Add(newEntry);
                m_GroupNames.Sort();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    [CustomEditor(typeof(HLODGroup))]
    public class HLODGroupEditor : Editor
    {
        class GUIStyles
        {
            public readonly GUIContent GroupName = EditorGUIUtility.TrTextContent("Group Name");
            public GUIStyles()
            {

            }
        }
        private static GUIStyles s_Styles;

        private static GUIStyles Styles
        {
            get
            {
                if (s_Styles == null)
                    s_Styles = new GUIStyles();
                return s_Styles;
            }
        }
        
        private SerializedProperty m_GroupName;
        private List<string> m_AllGroupNames;
        private HLODGroupPopupContent m_PopupContent;
        void OnEnable()
        {
            m_GroupName = serializedObject.FindProperty("m_GroupName");
            m_AllGroupNames = HLODGroup.FindAllGroups().Keys.ToList();
            m_PopupContent = new HLODGroupPopupContent(m_AllGroupNames);
            m_PopupContent.SelectGroupName = m_GroupName.stringValue;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var groupStr = m_GroupName.stringValue;            
            var rect = EditorGUILayout.GetControlRect(true, 16.0f);
            rect = EditorGUI.PrefixLabel(rect, Styles.GroupName);
            if (EditorGUI.DropdownButton(rect, new GUIContent(groupStr), FocusType.Passive) == true)
            {
                PopupWindow.Show(rect, m_PopupContent);
            }

            m_GroupName.stringValue = m_PopupContent.SelectGroupName;
            serializedObject.ApplyModifiedProperties();
        }
    }

}