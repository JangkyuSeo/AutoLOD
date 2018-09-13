using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Configuration;
using Unity.AutoLOD.Utilities;
using UnityEditor;
using UnityEditor.WindowsStandalone;
using UnityEngine;

namespace Unity.AutoLOD
{
    public class GenerateSceneLODWindow : EditorWindow
    {
        const string k_DefaultBatcher = "AutoLOD.DefaultBatcher";

        class GUIStyles
        {

            public readonly GUIContent NoBatcherMsg = EditorGUIUtility.TrTextContentWithIcon("No IBatchers found!", MessageType.Error);

            public readonly GUIContent LODGroupCount = EditorGUIUtility.TrTextContent("LODGroup count in level");
            public readonly GUIContent LODGroupSetting = EditorGUIUtility.TrTextContent("LODGroup setting");
            public readonly GUIContent BuildButton = EditorGUIUtility.TrTextContent("Build HLOD");
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
        private IBatcher currentBatcher;

        private LODSlider slider;

        private SceneLODCreator.Options options;


        public GenerateSceneLODWindow()
        {
        }

        void OnEnable()
        {
            slider = new LODSlider();
            slider.InsertRange("Detail", 0.0f);
            slider.InsertRange("LOD", options.DetailRange);
        }

        void OnDisable()
        {

        }
        [MenuItem("AutoLOD/CreateWindow")]
        static void Init()
        {
            EditorWindow.GetWindow<GenerateSceneLODWindow>(false, "Generate SceneLOD").Show();
        }

  
        private void OnFocus()
        {
            batcherTypes = ObjectUtils.GetImplementationsOfInterface(typeof(IBatcher)).ToArray();
            batcherDisplayNames = batcherTypes.Select(t => t.Name).ToArray();
            var type = System.Type.GetType(EditorPrefs.GetString(k_DefaultBatcher, null));

            if (type == null && batcherTypes.Length > 0)
                type = Type.GetType(batcherTypes[0].AssemblyQualifiedName);


            SetBatcher(type);
            options.LoadFromEditorPrefs();
        }

        void SetBatcher(Type type)
        {
            if (type != null)
            {
                currentBatcher = (IBatcher) Activator.CreateInstance(type);
                EditorPrefs.SetString(k_DefaultBatcher, type.AssemblyQualifiedName);
            }
            else
            {
                currentBatcher = null;
                EditorPrefs.DeleteKey(k_DefaultBatcher);
            }
        }



        void OnGUI()
        {

            if (currentBatcher == null)
            {
                EditorGUILayout.HelpBox(Styles.NoBatcherMsg);
                return;
            }

            EditorGUI.BeginChangeCheck();

            GUI.enabled = !SceneLODCreator.instance.IsCreating();
            options.VolumeSplitCount = EditorGUILayout.IntField(Styles.LODGroupCount, options.VolumeSplitCount);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(Styles.LODGroupSetting);
            slider.Draw();
            options.DetailRange = slider.GetRangeValue("LOD");
            EditorGUILayout.EndHorizontal();            
            EditorGUILayout.Space();

            DrawBatcher();
            DrawSimplification();

            DrawButtons();

            if (EditorGUI.EndChangeCheck())
            {
                options.SaveToEditorPrefs();
            }
        }

        
        void DrawBatcher()
        {
            if (currentBatcher != null)
            {
                int batcherIndex = Array.IndexOf(batcherDisplayNames, currentBatcher.GetType().Name);
                int newIndex = EditorGUILayout.Popup(Styles.Batcher, batcherIndex, batcherDisplayNames.ToArray());
                if (batcherIndex != newIndex)
                {
                    SetBatcher(batcherTypes[newIndex]);
                }

                var option = currentBatcher.GetBatcherOption();
                if (option != null)
                {
                    EditorGUI.indentLevel = 1;
                    option.OnGUI();
                    EditorGUI.indentLevel = 0;
                }
            }
            EditorGUILayout.Space();
        }

        private void DrawSimplification()
        {
            options.VolumeSimplification = EditorGUILayout.Toggle(Styles.VolumeSimplification, options.VolumeSimplification);
            if (options.VolumeSimplification)
            {
                EditorGUI.indentLevel = 1;
                options.VolumePolygonRatio = EditorGUILayout.Slider(Styles.PolygonRatio, options.VolumePolygonRatio, 0.0f, 1.0f);
                EditorGUI.indentLevel = 0;
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
                if (GUILayout.Button(Styles.CancelButton) == true)
                {
                    SceneLODCreator.instance.CancelCreating();
                }
            }
            else
            {
                if (GUILayout.Button(Styles.BuildButton) == true)
                {
                    SceneLODCreator.instance.Create(options, currentBatcher);
                }
            }
            EditorGUILayout.Space();
        }
    }

}