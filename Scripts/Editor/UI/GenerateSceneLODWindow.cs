using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Configuration;
using Unity.AutoLOD.Utilities;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.WindowsStandalone;
using UnityEngine;

namespace Unity.AutoLOD
{
    public class GenerateSceneLODWindow : EditorWindow
    {
        class GUIStyles
        {

            public readonly GUIContent NoBatcherMsg = EditorGUIUtility.TrTextContentWithIcon("No IBatchers found!", MessageType.Error);

            public readonly GUIContent LODGroupSize = EditorGUIUtility.TrTextContent("LODGroup size");
            public readonly GUIContent LODGroupSetting = EditorGUIUtility.TrTextContent("LODGroup setting");
            public readonly GUIContent BuildButton = EditorGUIUtility.TrTextContent("Build HLOD");
            public readonly GUIContent DestoryButton = EditorGUIUtility.TrTextContent("Destroy HLOD");
            public readonly GUIContent CancelButton = EditorGUIUtility.TrTextContent("Cancel");

            public readonly GUIContent Batcher = EditorGUIUtility.TrTextContent("Batcher");

            public readonly GUIContent VolumeSimplification = EditorGUIUtility.TrTextContent("Volume Simplification");
            public readonly GUIContent PolygonRatio = EditorGUIUtility.TrTextContent("Polygon Ratio");
            

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

        private System.Type[] batcherTypes;
        private string[] batcherDisplayNames;

        private SerializedObject serializedObject;
        private LODSlider slider;

        public GenerateSceneLODWindow()
        {
        }

        void OnEnable()
        {
            Initialize();

        }

        void OnDisable()
        {
            slider = null;
            serializedObject = null;
        }

        void Initialize()
        {
            var options = SceneLODCreator.instance.GetOptions();
            options.LoadFromEditorPrefs();

            serializedObject = new SerializedObject(options);

            slider = new LODSlider();
            slider.InsertRange("Detail", serializedObject.FindProperty("LODRange"));
            slider.InsertRange("HLOD", null);
        }

  
        private void OnFocus()
        {
            batcherTypes = ObjectUtils.GetImplementationsOfInterface(typeof(IBatcher)).ToArray();
            batcherDisplayNames = batcherTypes.Select(t => t.Name).ToArray();

            SceneLODCreator.instance.GetOptions().LoadFromEditorPrefs();
        }

        void OnGUI()
        {
            if (SceneLOD.instance.RootVolume != null)
            {
                GUI.enabled = false;
                DrawGenerate(false);
                GUI.enabled = true;
            }
            else
            {
                DrawGenerate(true);
            }

            DrawButtons();
            
        }

        void DrawGenerate(bool rootExists)
        {
            serializedObject.Update();

            //if (currentBatcher == null)
            //{
            //    EditorGUILayout.HelpBox(Styles.NoBatcherMsg);
            //    return;
            //}

            EditorGUI.BeginChangeCheck();

            GUI.enabled = rootExists && !SceneLODCreator.instance.IsCreating();

            
            DrawCommon();
            DrawGroups();
            

            if (serializedObject.ApplyModifiedProperties() || EditorGUI.EndChangeCheck())
            {
                SceneLODCreator.instance.GetOptions().SaveToEditorPrefs();
            }

            GUI.enabled = true;
        }

        private bool m_CommonExpanded = true;
        void DrawCommon()
        {
            var options = SceneLODCreator.instance.GetOptions();
            m_CommonExpanded = EditorGUILayout.Foldout(m_CommonExpanded, "Common");
            if (m_CommonExpanded)
            {
                EditorGUI.indentLevel += 1;
                options.VolumeSize = EditorGUILayout.FloatField(Styles.LODGroupSize, options.VolumeSize);
                DrawSlider();
                EditorGUI.indentLevel -= 1;
            }

            GUILayout.Space(10);
        }

        private Dictionary<string, bool> m_GroupExpanded = new Dictionary<string, bool>();
        void DrawGroups()
        {
            var options = SceneLODCreator.instance.GetOptions();

            foreach (var pair in options.GroupOptions)
            {
                string groupName = pair.Key;
                if ( m_GroupExpanded.ContainsKey( groupName ) == false )
                    m_GroupExpanded.Add(groupName, true);

                m_GroupExpanded[groupName] = EditorGUILayout.Foldout(m_GroupExpanded[groupName], groupName);
                if (m_GroupExpanded[groupName])
                {
                    EditorGUI.indentLevel += 1;

                    DrawBatcher(pair.Value);
                    DrawSimplification(pair.Value);

                    EditorGUI.indentLevel -= 1;
                }
                GUILayout.Space(10);
            }

        }

        void DrawSlider()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(Styles.LODGroupSetting);
            slider.Draw();
            EditorGUILayout.EndHorizontal();            
            EditorGUILayout.Space();

        }
        void DrawBatcher(SceneLODCreator.GroupOptions groupOptions)
        {
            if (groupOptions.BatcherType == null)
            {
                if (batcherTypes.Length > 0)
                {
                    groupOptions.BatcherType = batcherTypes[0];
                    groupOptions.Batcher = (IBatcher) Activator.CreateInstance(groupOptions.BatcherType);
                    GUI.changed = true; //< for store value.
                }
            }
            if (groupOptions.BatcherType != null)
            {
                int batcherIndex = Array.IndexOf(batcherDisplayNames, groupOptions.BatcherType.Name);
                int newIndex = EditorGUILayout.Popup(Styles.Batcher, batcherIndex, batcherDisplayNames.ToArray());
                if (batcherIndex != newIndex)
                {
                    groupOptions.BatcherType = batcherTypes[newIndex];
                    groupOptions.Batcher = (IBatcher) Activator.CreateInstance(groupOptions.BatcherType);
                    //we don't need GUI.changed here. 
                    //Already set a value when popup index was changed.
                }

                if (groupOptions.Batcher != null)
                {
                    var option = groupOptions.Batcher.GetBatcherOption();
                    if (option != null)
                    {
                        EditorGUI.indentLevel += 1;
                        option.OnGUI();
                        EditorGUI.indentLevel -= 1;
                    }
                }
            }
            EditorGUILayout.Space();
        }

        private void DrawSimplification(SceneLODCreator.GroupOptions groupOptions)
        {
            groupOptions.VolumeSimplification = EditorGUILayout.Toggle(Styles.VolumeSimplification, groupOptions.VolumeSimplification);
            if (groupOptions.VolumeSimplification)
            {
                EditorGUI.indentLevel += 1;
                groupOptions.VolumePolygonRatio = EditorGUILayout.Slider(Styles.PolygonRatio, groupOptions.VolumePolygonRatio, 0.0f, 1.0f);
                EditorGUI.indentLevel -= 1;
            }

            EditorGUILayout.Space();
        }


        private void DrawButtons()
        {
            if (SceneLODCreator.instance.IsCreating())
            {
                GUI.enabled = false;
                GUILayout.Button(Styles.BuildButton);
                GUI.enabled = true;
                EditorGUILayout.Space();
                if (GUILayout.Button(Styles.CancelButton) == true)
                {
                    SceneLODCreator.instance.CancelCreating();
                }
            }
            else if (SceneLOD.instance.RootVolume != null)
            {
                if (GUILayout.Button(Styles.DestoryButton) == true)
                {
                    if (SceneLOD.instance.RootVolume != null)
                        SceneLOD.instance.RootVolume.ResetLODGroup();

                    MonoBehaviourHelper.StartCoroutine(ObjectUtils.FindGameObject("HLODs",
                        root => { DestroyImmediate(root); }));
                    DestroyImmediate(SceneLOD.instance.RootVolume.gameObject);

                }
            }
            else
            {
                if (GUILayout.Button(Styles.BuildButton) == true)
                {
                    var hlodGroups = HLODGroup.FindAllGroups();
                    
                    SceneLODCreator.instance.Create(hlodGroups, () =>
                    {
                        //redraw this window.
                        Repaint();
                        SceneLOD.instance.EnableHLOD();
                    });
                }
            }
            EditorGUILayout.Space();
        }

    }

}