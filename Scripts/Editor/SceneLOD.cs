﻿using System;
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
        private const string k_SceneLODWindowMenuPath = "AutoLOD/Generate SceneLOD Window";
        private const string k_ShowVolumeBoundsMenuPath = "AutoLOD/Show Volume Bounds";
        private const string k_ClearCacheMemoryMenuPath = "AutoLOD/Clear Cache Memory";
        private const string k_ClearCacheInDiskyMenuPath = "AutoLOD/Clear Cache in Disk";

        private const string k_HLODRootContainer = "HLODs";

        static bool s_HLODEnabled = true;
        static bool s_Activated;


        private SceneLODUpdater m_Updater;

        public SceneLODUpdater Updater
        {
            get { return m_Updater; }
        }

        public void EnableHLOD()
        {
            s_HLODEnabled = true;
        }

        public static string GetScenePath()
        {
            var scene = SceneManager.GetActiveScene();

            var path = Path.GetDirectoryName(scene.path);
  
            path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            path = path + Path.DirectorySeparatorChar + scene.name + Path.DirectorySeparatorChar;

            //remove first assets/
            return path.Substring(path.IndexOf(Path.DirectorySeparatorChar) + 1);
        }
        
        public static string GetSceneLODPath()
        {
            return GetScenePath() + "SceneLOD" + Path.DirectorySeparatorChar;
        }

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


            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            Handles.EndGUI();
        }
        

        IEnumerator SetLODUpdater()
        {
            if (m_Updater)
            {
                yield break;
            }

            // Handle initialization or the case where the BVH has shrunk
            SceneLODUpdater updater = null;
            var scene = SceneManager.GetActiveScene();
            var rootGameObjects = scene.GetRootGameObjects();
            foreach (var go in rootGameObjects)
            {
                if (!go)
                    continue;

                updater = go.GetComponent<SceneLODUpdater>();
                if (updater)
                    break;

                yield return null;
            }

            if (updater)
            {
                m_Updater = updater;
            }
        }

        void EditorUpdate()
        {
            if (m_Updater == null)
            {
                MonoBehaviourHelper.StartCoroutine(SetLODUpdater());
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
        }

#region Menu
        //AutoLOD requires Unity 2017.3 or a later version
#if UNITY_2017_3_OR_NEWER
        [MenuItem(k_SceneLODWindowMenuPath, priority = 1)]
        static void GenerateSceneLOD(MenuCommand menuCommand)
        {
            EditorWindow.GetWindow<GenerateSceneLODWindow>(false, "Generate SceneLOD").Show();
        }

        [MenuItem(k_ShowVolumeBoundsMenuPath, priority = 100)]
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

        [MenuItem(k_ClearCacheMemoryMenuPath, priority = 50)]
        static void ClearCacheMemory(MenuCommand menuCommand)
        {
            LODCache.Cache.ClearMemory();
        }

        [MenuItem(k_ClearCacheInDiskyMenuPath, priority = 51)]
        static void ClearCacheInDisk(MenuCommand menuCommand)
        { 
            LODCache.Cache.ClearDisk();
        }
#endif
#endregion
    }
}
