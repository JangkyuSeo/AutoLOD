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

            public readonly GUIContent LODTrangleRange = EditorGUIUtility.TrTextContent("LOD Trangle Range");
            public readonly GUIContent LODTrangleMin = EditorGUIUtility.TrTextContent("Min");
            public readonly GUIContent LODTrangleMax = EditorGUIUtility.TrTextContent("Max");
            

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

        private LODSlider slider;

        private Vector2 windowScrollPos = Vector2.zero;

        public GenerateSceneLODWindow()
        {
        }

        void OnEnable()
        {
            slider = new LODSlider();
            slider.InsertRange("Detail", Config.LODRange);
            slider.InsertRange("HLOD", 0.0f);

        }

        void OnDisable()
        {
            slider = null;
        }
  
        private void OnFocus()
        {
            batcherTypes = ObjectUtils.GetImplementationsOfInterface(typeof(IBatcher)).ToArray();
            batcherDisplayNames = batcherTypes.Select(t => t.Name).ToArray();
        }

        void OnGUI()
        {
            windowScrollPos = EditorGUILayout.BeginScrollView(windowScrollPos);
            if (SceneLOD.instance.Updater != null)
            {
                GUI.enabled = false;
                DrawGenerate(false);
                GUI.enabled = true;
            }
            else
            {
                DrawGenerate(true);
            }
            EditorGUILayout.EndScrollView();

            DrawButtons();
            
        }

        void DrawGenerate(bool rootExists)
        {
           
            EditorGUI.BeginChangeCheck();

            GUI.enabled = rootExists && !SceneLODCreator.instance.IsCreating();
            DrawCommon();
            DrawGroups();            

            GUI.enabled = true;
        }

        private bool m_CommonExpanded = true;
        void DrawCommon()
        {
            m_CommonExpanded = EditorGUILayout.Foldout(m_CommonExpanded, "Common");
            if (m_CommonExpanded)
            {
                EditorGUI.indentLevel += 1;
                Config.VolumeSize = EditorGUILayout.FloatField(Styles.LODGroupSize, Config.VolumeSize);
                DrawSlider();
                EditorGUI.indentLevel -= 1;
            }

            GUILayout.Space(10);
        }

        private Dictionary<string, bool> m_GroupExpanded = new Dictionary<string, bool>();
        void DrawGroups()
        {
            var allGroups = HLODGroup.FindAllGroups();

            foreach (var groupName in allGroups.Keys)
            {
                if ( m_GroupExpanded.ContainsKey( groupName ) == false )
                    m_GroupExpanded.Add(groupName, true);

                m_GroupExpanded[groupName] = EditorGUILayout.Foldout(m_GroupExpanded[groupName], groupName);
                if (m_GroupExpanded[groupName])
                {
                    EditorGUI.indentLevel += 1;

                    var groupConfig = Config.GetGroupConfig(groupName);
                    DrawBatcher(groupConfig);
                    DrawSimplification(groupConfig);
                    DrawThreshold(groupConfig);

                    EditorGUI.indentLevel -= 1;
                }
                GUILayout.Space(10);
            }

        }

        void DrawSlider()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(Styles.LODGroupSetting);
            slider.SetRangeValue("Detail", Config.LODRange);
            slider.Draw();
            Config.LODRange = slider.GetRangeValue("Detail");
            EditorGUILayout.EndHorizontal();            
            EditorGUILayout.Space();

        }
        void DrawBatcher(Config.GroupConfig groupOptions)
        {
            if (groupOptions.BatcherType == null)
            {
                if (batcherTypes.Length > 0)
                {
                    groupOptions.BatcherType = batcherTypes[0];
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

        private void DrawSimplification(Config.GroupConfig groupOptions)
        {
            groupOptions.VolumeSimplification = EditorGUILayout.Toggle(Styles.VolumeSimplification, groupOptions.VolumeSimplification);
            if (groupOptions.VolumeSimplification)
            {
                EditorGUI.indentLevel += 1;
                groupOptions.VolumePolygonRatio = EditorGUILayout.Slider(Styles.PolygonRatio, groupOptions.VolumePolygonRatio, 0.0f, 1.0f);

                DrawTiangleRange(groupOptions);
                EditorGUI.indentLevel -= 1;
            }

            EditorGUILayout.Space();
        }

        private void DrawTiangleRange(Config.GroupConfig groupOptions)
        {
            EditorGUILayout.PrefixLabel(Styles.LODTrangleRange);
            EditorGUI.indentLevel += 1;

            groupOptions.LODTriangleMin = EditorGUILayout.IntSlider(Styles.LODTrangleMin, groupOptions.LODTriangleMin, 10, 100);
            groupOptions.LODTriangleMax = EditorGUILayout.IntSlider(Styles.LODTrangleMax, groupOptions.LODTriangleMax, 10, 5000);

            EditorGUI.indentLevel -= 1;
        }
        private void DrawThreshold(Config.GroupConfig groupOptions)
        {
            groupOptions.LODThresholdSize = EditorGUILayout.FloatField("LOD Threshold size", groupOptions.LODThresholdSize);
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

                DrawProgress();
            }
            else if (SceneLOD.instance.Updater != null)
            {
                if (GUILayout.Button(Styles.DestoryButton) == true)
                {
                    SceneLODCreator.instance.Destroy();
                }
            }
            else
            {
                if (GUILayout.Button(Styles.BuildButton) == true)
                {                    
                    SceneLODCreator.instance.Create(() =>
                    {
                        //redraw this window.
                        Repaint();
                        SceneLOD.instance.EnableHLOD();
                    });
                }
            }
            EditorGUILayout.Space();
        }

        private void DrawProgress()
        {
            var creator = SceneLODCreator.instance;
            
            float job = (float)creator.GetCurrentJobCount() / (float)creator.GetMaxJobCount();
            float progress = (float) creator.GetCurrentProgress() / (float) creator.GetMaxProgress();

            EditorGUILayout.Space();

            var jobRect = GUILayoutUtility.GetRect(0, 20.0f, GUILayout.ExpandWidth(true));
            jobRect.x += 10.0f;
            jobRect.width -= 20.0f;

            EditorGUI.ProgressBar(jobRect, job, job.ToString("p1"));
            EditorGUILayout.Space();

            var progressRect = GUILayoutUtility.GetRect(0, 20.0f, GUILayout.ExpandWidth(true));
            progressRect.x += 10.0f;
            progressRect.width -= 20.0f;

            EditorGUI.ProgressBar(progressRect, progress, progress.ToString("p1"));
            EditorGUILayout.Space();

            this.Repaint();
        }

    }

}