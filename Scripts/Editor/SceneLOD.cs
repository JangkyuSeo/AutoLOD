using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.AutoLOD.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Dbg = UnityEngine.Debug;
using Debug = System.Diagnostics.Debug;
using UnityObject = UnityEngine.Object;


namespace Unity.AutoLOD
{
    public class SceneLOD : ScriptableSingleton<SceneLOD>
    {
        private const string k_GenerateSceneLODMenuPath = "AutoLOD/Generate SceneLOD";
        private const string k_DestroySceneLODMenuPath = "AutoLOD/Destroy SceneLOD";
        private const string k_UpdateSceneLODMenuPath = "AutoLOD/Update SceneLOD";
        private const string k_ShowVolumeBoundsMenuPath = "AutoLOD/Show Volume Bounds";

        private const string k_HLODRootContainer = "HLODs";

        static bool s_HLODEnabled = true;
        static bool s_Activated;
        
        LODVolume m_RootVolume;

        void Start()
        {
            Dbg.Log("SceneLOD start");
        }
        void OnEnable()
        {
#if UNITY_2017_3_OR_NEWER
            if (LayerMask.NameToLayer(LODVolume.HLODLayer) == -1)
            {
                Dbg.LogWarning("Adding missing HLOD layer");

                var layers = TagManager.GetRequiredLayers();
                foreach (var layer in layers)
                {
                    TagManager.AddLayer(layer);
                }
            }

            if (LayerMask.NameToLayer(LODVolume.HLODLayer) != -1)
            {
                Tools.lockedLayers |= LayerMask.GetMask(LODVolume.HLODLayer);
                s_Activated = true;
            }

            if (s_Activated)
                AddCallbacks();
#endif
            Dbg.Log("SceneLOD enable");
            Menu.SetChecked(k_ShowVolumeBoundsMenuPath, Settings.ShowVolumeBounds);
        }

        void OnDisable()
        {
            Dbg.Log("SceneLOD disable");
            s_Activated = false;
            RemoveCallbacks();

            if (m_RootVolume != null)
                m_RootVolume.ResetLODGroup();
        }

        void AddCallbacks()
        {
            EditorApplication.update += EditorUpdate;
            Camera.onPreCull += PreCull;
            SceneView.onSceneGUIDelegate += OnSceneGUI;
        }

        void RemoveCallbacks()
        {
            EditorApplication.update -= EditorUpdate;
            Camera.onPreCull -= PreCull;
            SceneView.onSceneGUIDelegate -= OnSceneGUI;
        }

        void OnSceneGUI(SceneView sceneView)
        {
            var activeSceneName = SceneManager.GetActiveScene().name;

            var rect = sceneView.position;
            rect.x = 0f;
            rect.y = 0f;

            Handles.BeginGUI();
            GUILayout.BeginArea(rect);
            GUILayout.BeginHorizontal();

            if (SceneLODCreator.instance.IsCreating() == false)
            {
                if (m_RootVolume && GUILayout.Button(s_HLODEnabled ? "Disable HLOD" : "Enable HLOD"))
                {
                    s_HLODEnabled = !s_HLODEnabled;

                    if (m_RootVolume != null)
                        m_RootVolume.ResetLODGroup();
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        


        IEnumerator SetRootLODVolume()
        {
            if (m_RootVolume)
            {
                var rootVolumeTransform = m_RootVolume.transform;
                var transformRoot = rootVolumeTransform.root;

                // Handle the case where the BVH has grown
                if (rootVolumeTransform != transformRoot)
                    m_RootVolume = transformRoot.GetComponent<LODVolume>();

                yield break;
            }

            // Handle initialization or the case where the BVH has shrunk
            LODVolume lodVolume = null;
            var scene = SceneManager.GetActiveScene();
            var rootGameObjects = scene.GetRootGameObjects();
            foreach (var go in rootGameObjects)
            {
                if (!go)
                    continue;

                lodVolume = go.GetComponent<LODVolume>();
                if (lodVolume)
                    break;

                yield return null;
            }

            if (lodVolume)
            {
                m_RootVolume = lodVolume;
                m_RootVolume.ResetLODGroup();
            }
        }

        void EditorUpdate()
        {
            if (m_RootVolume == null)
            {
                MonoBehaviourHelper.StartCoroutine(SetRootLODVolume());
            }

            if (SceneLODCreator.instance.IsCreating() == true)
            {
                s_HLODEnabled = false;
            }
        }

        // PreCull is called before LODGroup updates
        void PreCull(Camera camera)
        {

            //if playing in editor, not use this flow.
            if (Application.isPlaying == true)
                return;

            if (s_HLODEnabled == false)
                return;
            
            if (!m_RootVolume)
                return;

            var cameraTransform = camera.transform;
            var cameraPosition = cameraTransform.position;

            m_RootVolume.UpdateLODGroup(camera, cameraPosition, false);
        }

#region Menu
        //AutoLOD requires Unity 2017.3 or a later version
#if UNITY_2017_3_OR_NEWER
        [MenuItem(k_GenerateSceneLODMenuPath, true, priority = 1)]
        static bool CanGenerateSceneLOD(MenuCommand menuCommand)
        {
            return instance.m_RootVolume == null;
        }

        [MenuItem(k_GenerateSceneLODMenuPath, priority = 1)]
        static void GenerateSceneLOD(MenuCommand menuCommand)
        {
            EditorWindow.GetWindow<GenerateSceneLODWindow>(false, "Generate SceneLOD").Show();
        }


        [MenuItem(k_DestroySceneLODMenuPath, true, priority = 1)]
        static bool CanDestroySceneLOD(MenuCommand menuCommand)
        {
            return instance.m_RootVolume != null;
        }

        [MenuItem(k_DestroySceneLODMenuPath, priority = 1)]
        static void DestroySceneLOD(MenuCommand menuCommand)
        {
            
            if (instance.m_RootVolume != null)
                instance.m_RootVolume.ResetLODGroup();

            MonoBehaviourHelper.StartCoroutine(ObjectUtils.FindGameObject("HLODs",
                root => { DestroyImmediate(root); }));
            DestroyImmediate(instance.m_RootVolume.gameObject);

        }

        [MenuItem(k_UpdateSceneLODMenuPath, true, priority = 1)]
        static bool CanUpdateSceneLOD(MenuCommand menuCommand)
        {
            return instance.m_RootVolume != null;
        }

        [MenuItem(k_UpdateSceneLODMenuPath, priority = 1)]
        static void UpdateSceneLOD(MenuCommand menuCommand)
        {
            DestroySceneLOD(menuCommand);
            GenerateSceneLOD(menuCommand);
        }

        [MenuItem(k_ShowVolumeBoundsMenuPath, priority = 50)]
        static void ShowVolumeBounds(MenuCommand menuCommand)
        {
            bool showVolume = !Settings.ShowVolumeBounds;
            Menu.SetChecked(k_ShowVolumeBoundsMenuPath, showVolume);

            Settings.ShowVolumeBounds = showVolume;

            // Force more frequent updating
            var mouseOverWindow = EditorWindow.mouseOverWindow;
            if (mouseOverWindow)
                mouseOverWindow.Repaint();

        }
#endif
#endregion
    }
}
