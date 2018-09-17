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
        const string k_DefaultBatcher = "AutoLOD.DefaultBatcher";

        class GUIStyles
        {

            public readonly GUIContent NoBatcherMsg = EditorGUIUtility.TrTextContentWithIcon("No IBatchers found!", MessageType.Error);

            public readonly GUIContent LODGroupCount = EditorGUIUtility.TrTextContent("LODGroup count in level");
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
        private IBatcher currentBatcher;

        private LODSlider slider;

        private SceneLODCreator.Options options;

        private SerializedObject serializedObject;


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
            options = null;
        }

        void Initialize()
        {
            options = CreateInstance<SceneLODCreator.Options>();
            options.LoadFromEditorPrefs();

            serializedObject = new SerializedObject(options);

            slider = new LODSlider();
            slider.InsertRange("Detail", serializedObject.FindProperty("LODRange"));
            slider.InsertRange("LOD", null);
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
            if (options == null)
            {
                Initialize();
            }
            serializedObject.Update();

            if (currentBatcher == null)
            {
                EditorGUILayout.HelpBox(Styles.NoBatcherMsg);
                return;
            }

            EditorGUI.BeginChangeCheck();

            GUI.enabled = rootExists && !SceneLODCreator.instance.IsCreating();
            options.VolumeSplitCount = EditorGUILayout.IntField(Styles.LODGroupCount, options.VolumeSplitCount);

            DrawSlider();
            DrawBatcher();
            DrawSimplification();


            if (serializedObject.ApplyModifiedProperties() || EditorGUI.EndChangeCheck())
            {
                options.SaveToEditorPrefs();
            }

            GUI.enabled = true;
        }

        void DrawSlider()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(Styles.LODGroupSetting);
            slider.Draw();
            EditorGUILayout.EndHorizontal();            
            EditorGUILayout.Space();

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
                    SceneLODCreator.instance.Create(options, currentBatcher, () =>
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