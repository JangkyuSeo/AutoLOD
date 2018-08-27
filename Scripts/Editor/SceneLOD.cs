using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor.Experimental.AutoLOD.Utilities;
using UnityEngine;
using UnityEngine.Experimental.AutoLOD;
using UnityEngine.SceneManagement;
using Dbg = UnityEngine.Debug;
using Debug = System.Diagnostics.Debug;
using UnityObject = UnityEngine.Object;


namespace UnityEditor.Experimental.AutoLOD
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
        Stopwatch m_ServiceCoroutineExecutionTime = new Stopwatch();

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
            if (m_RootVolume && GUILayout.Button(s_HLODEnabled ? "Disable HLOD" : "Enable HLOD"))
            {
                s_HLODEnabled = !s_HLODEnabled;

                if ( m_RootVolume != null )
                    m_RootVolume.ResetLODGroup();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        IEnumerator UpdateOctree()
        {
            if (!m_RootVolume)
            {
                yield return SetRootLODVolume();

                if (!m_RootVolume)
                {

                    Dbg.Log("Creating root volume");
                    m_RootVolume = LODVolume.Create();

                }
            }

            List<LODGroup> lodGroups = new List<LODGroup>();
            yield return ObjectUtils.FindObjectsOfType(lodGroups);

            int hlodLayerMask = LayerMask.NameToLayer(LODVolume.HLODLayer);

            // Remove any lodgroups that should not be there (e.g. HLODs)
            lodGroups.RemoveAll(r =>
            {
                if (r)
                {
                    if (r.gameObject.layer == hlodLayerMask)
                        return true;

                    LOD lastLod = r.GetLODs().Last();

                    if (lastLod.renderers.Length == 0)
                        return true;
                }

                return false;
            });


            yield return m_RootVolume.SetLODGruops(lodGroups);
        }
        public IEnumerator UpdateHLODs(LODVolume volume)
        {
            // Process children first, since we are now combining children HLODs to make parent HLODs
            foreach (Transform child in volume.transform)
            {
                var childLODVolume = child.GetComponent<LODVolume>();
                if (childLODVolume)
                    yield return UpdateHLODs(childLODVolume);

                if (!this)
                    yield break;
            }
            yield return GenerateHLOD(volume);
        }


        public IEnumerator GenerateHLOD(LODVolume volume)
        {
            HashSet<Renderer> hlodRenderers = new HashSet<Renderer>();

            foreach (var group in volume.LodGroups)
            {
                var lastLod = group.GetLODs().Last();
                foreach (var lr in lastLod.renderers)
                {
                    if (lr && lr.GetComponent<MeshFilter>())
                        hlodRenderers.Add(lr);
                }
            }

            CleanupHLOD(volume);

            if (hlodRenderers.Count == 0)
                yield break;

            GameObject hlodRootContainer = null;
            yield return ObjectUtils.FindGameObject(k_HLODRootContainer, root =>
            {
                if (root)
                    hlodRootContainer = root;
            });

            if (!hlodRootContainer)
            {
                hlodRootContainer = new GameObject(k_HLODRootContainer);
                hlodRootContainer.AddComponent<SceneLODUpdater>();
            }

            var hlodLayer = LayerMask.NameToLayer(LODVolume.HLODLayer);
            var hlodRoot = new GameObject("HLOD " + volume.gameObject.name);
            hlodRoot.layer = LayerMask.NameToLayer(LODVolume.HLODLayer);
            hlodRoot.transform.parent = hlodRootContainer.transform;

            volume.HLODRoot = hlodRoot;


            var parent = hlodRoot.transform;
            foreach (var r in hlodRenderers)
            {
                var rendererTransform = r.transform;

                var child = new GameObject(r.name, typeof(MeshFilter), typeof(MeshRenderer));
                child.layer = hlodLayer;
                var childTransform = child.transform;
                childTransform.SetPositionAndRotation(rendererTransform.position, rendererTransform.rotation);
                childTransform.localScale = rendererTransform.lossyScale;
                childTransform.SetParent(parent, true);

                var mr = child.GetComponent<MeshRenderer>();
                EditorUtility.CopySerialized(r.GetComponent<MeshFilter>(), child.GetComponent<MeshFilter>());
                EditorUtility.CopySerialized(r.GetComponent<MeshRenderer>(), mr);
            }

            LOD lod = new LOD();
            LOD detailLOD = new LOD();

            detailLOD.screenRelativeTransitionHeight = 0.3f;
            lod.screenRelativeTransitionHeight = 0.0f;

            var lodGroup = volume.GetComponent<LODGroup>();
            if (!lodGroup)
                lodGroup = volume.gameObject.AddComponent<LODGroup>();

            volume.LodGroup = lodGroup;


            var batcher = (IBatcher)System.Activator.CreateInstance(LODVolume.batcherType);
            yield return batcher.Batch(hlodRoot);

            lod.renderers = hlodRoot.GetComponentsInChildren<Renderer>(false);
            lodGroup.SetLODs(new LOD[] { detailLOD, lod });
        }

        
        void CleanupHLOD(LODVolume volume)
        {
            var root = volume.HLODRoot;
            if (root) // Clean up old HLOD
            {
                var mf = root.GetComponent<MeshFilter>();
                if (mf)
                    DestroyImmediate(mf.sharedMesh, true); // Clean up file on disk

                DestroyImmediate(root);

                volume.HLODRoot = null;
            }
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
        }

        IEnumerator ServiceCoroutineQueue()
        {
            m_ServiceCoroutineExecutionTime.Start();

            yield return UpdateOctree();
            yield return UpdateHLODs(m_RootVolume);
            
            m_ServiceCoroutineExecutionTime.Reset();
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
            MonoBehaviourHelper.StartCoroutine(instance.ServiceCoroutineQueue());            
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
