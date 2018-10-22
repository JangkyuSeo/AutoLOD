using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Unity.AutoLOD
{
    public class SceneLODUpdater : MonoBehaviour
    {
        [SerializeField]
        private LODVolume m_RootLODVolume;

        public LODVolume RootLODVolume
        {
            set { m_RootLODVolume = value; }
            get { return m_RootLODVolume; }
        }


        interface IRendererProxy
        {
            void Initialize();
            void SetEnable(bool enable);
        }

        class LODGroupRendererProxy : IRendererProxy
        {
            private LODGroup m_Group;

            public LODGroupRendererProxy(LODGroup group)
            {
                m_Group = group;
                m_Group.SetEnabled(true);
            }

            public void Initialize()
            {
                m_Group.SetEnabled(true);
            }
            public void SetEnable(bool enable)
            {
                m_Group.SetEnabled(enable);
            }
        }

        class VolumeRendererProxy : IRendererProxy
        {
            private SceneLODUpdater m_Updater;
            private int m_Index;
            private bool? m_LastState;
            public VolumeRendererProxy(SceneLODUpdater updater, int index)
            {
                m_Updater = updater;
                m_Index = index;
                
            }
            public void Initialize()
            {
                m_LastState = null;
            }
            public void SetEnable(bool enable)
            {
                if (m_LastState == enable)
                    return;

                if (enable == true)
                {
                    m_Updater.m_ActiveVolumes.Add(m_Index);
                }
                else
                {
                    var renderer = m_Updater.m_Renderers[m_Index];
                    for (int li = 0; li < renderer.LODMeshes.Count; ++li)
                    {
                        renderer.LODMeshes[li].enabled = false;
                    }

                    for (int ri = 0; ri < renderer.Renderers.Count; ++ri)
                    {
                        renderer.Renderers[ri].SetEnable(false);
                    }
                    m_Updater.m_ActiveVolumes.Remove(m_Index);
                    
                }

                m_LastState = enable;
            }
        }

        class FrustumCullHelper
        {
            private Vector3[] normals = new Vector3[6];
            private float[] distances = new float[6];

            public void Set(Camera camera)
            {
                var vp = camera.projectionMatrix * camera.worldToCameraMatrix;

                normals[0].x = vp.m20 + vp.m30;
                normals[0].y = vp.m21 + vp.m31;
                normals[0].z = vp.m22 + vp.m32;
                distances[0] = vp.m23 + vp.m33;

                normals[1].x = -vp.m20 + vp.m30;
                normals[1].y = -vp.m21 + vp.m31;
                normals[1].z = -vp.m22 + vp.m32;
                distances[1] = -vp.m23 + vp.m33;

                normals[2].x = vp.m10 + vp.m30;
                normals[2].y = vp.m11 + vp.m31;
                normals[2].z = vp.m12 + vp.m32;
                distances[2] = vp.m13 + vp.m33;

                normals[3].x = -vp.m10 + vp.m30;
                normals[3].y = -vp.m11 + vp.m31;
                normals[3].z = -vp.m12 + vp.m32;
                distances[3] = -vp.m13 + vp.m33;

                normals[4].x = vp.m00 + vp.m30;
                normals[4].y = vp.m01 + vp.m31;
                normals[4].z = vp.m02 + vp.m32;
                distances[4] = vp.m03 + vp.m33;

                normals[5].x = -vp.m00 + vp.m30;
                normals[5].y = -vp.m01 + vp.m31;
                normals[5].z = -vp.m02 + vp.m32;
                distances[5] = -vp.m03 + vp.m33;

                //normalize
                for (int i = 0; i < 6; ++i)
                {
                    float len = Vector3.Magnitude(normals[i]);
                    normals[i] = normals[i] / len;
                    distances[i] = distances[i] / len;
                }
            }

            public bool IsOutside(Vector3 position, float radius)
            {
                for (int pi = 0; pi < 6; ++pi)
                {
                    float planeDistance = Vector3.Dot(normals[pi], position) + distances[pi];

                    if (planeDistance + radius < 0.0f)
                    {
                        return true;
                    }
                }

                return false;
            }

        }

        struct VolumeBounds
        {
            public Vector3 Center;
            public float Size;        //< volume is cuboid. so, every axis is the same size.
            public float Radius;
        }

        struct VolumeRenderer
        {
            public List<IRendererProxy> Renderers;
            public List<Renderer> LODMeshes;
        }

        //private NativeArray<VolumeBounds> m_Bounds;
        private List<VolumeBounds> m_Bounds;
        private List<VolumeRenderer> m_Renderers;

        private List<int> m_ActiveVolumes = new List<int>();

        void Initialize()
        {
            m_RootLODVolume.ResetLODGroup();
            for (int i = 0; i < m_Renderers.Count; ++i)
            {
                var renderer = m_Renderers[i];
                for (int ri = 0; ri < renderer.Renderers.Count; ++ri)
                {
                    renderer.Renderers[ri].Initialize();
                }

                for (int mi = 0; mi < renderer.LODMeshes.Count; ++mi)
                {
                    renderer.LODMeshes[mi].enabled = false;
                }
            }

            m_ActiveVolumes.Clear();
            m_ActiveVolumes.Add(0);
        }
#region UnityEvents
        void Awake()
        {            
            if (m_RootLODVolume != null)
            {
                Build();
            }
        }

        void OnDestroy()
        {
        }

        IEnumerator Start()
        {
            yield return new WaitForEndOfFrame();
            Initialize();
        }

        void OnEnable()
        {
            Camera.onPreCull += OnPreCull;
            if (m_RootLODVolume != null)
            {
                Initialize();
            }

        }

        void OnDisable()
        {
            Camera.onPreCull -= OnPreCull;
            if (m_RootLODVolume != null)
            {
                m_RootLODVolume.ResetLODGroup();
            }

        }
#endregion

        public void Build()
        {
            if (m_RootLODVolume == null)
            {
                Debug.LogError("SceneLODUpdate build failed. RootLODVolume is null.");
                return;
            }

            //it build by BFS.
            List<VolumeBounds> boundsList = new List<VolumeBounds>();
            List<VolumeRenderer> rendererList = new List<VolumeRenderer>();

            Queue<LODVolume> treeTrevelQueue = new Queue<LODVolume>();
            Queue<int> parentIndexQueue = new Queue<int>();

            treeTrevelQueue.Enqueue(m_RootLODVolume);
            parentIndexQueue.Enqueue(-1);

            while (treeTrevelQueue.Count > 0)
            {
                var current = treeTrevelQueue.Dequeue();
                VolumeBounds bounds;

                int currentIndex = boundsList.Count;
                int parentIndex = parentIndexQueue.Dequeue();

                if (current.VolumeGroups.Count == 0)
                    continue;

                bounds.Center = current.Bounds.center;
                bounds.Size = current.Bounds.size.x;
                bounds.Radius = current.Bounds.extents.magnitude;

                boundsList.Add(bounds);

                VolumeRenderer renderer;
                renderer.LODMeshes = new List<Renderer>();
                renderer.Renderers = new List<IRendererProxy>();

                LODGroup lodGroup = current.GetComponent<LODGroup>();
                if (lodGroup != null)
                {
                    LOD[] lods = lodGroup.GetLODs();

                    if (lods.Length >= 2)
                    {
                        for (int ri = 0; ri < lods[1].renderers.Length; ++ri)
                        {
                            renderer.LODMeshes.Add(lods[1].renderers[ri]);
                        }
                    }
                }

                if (current.ChildVolumes.Count == 0)
                {
                    for (int vi = 0; vi < current.VolumeGroups.Count; ++vi)
                    {
                        var volumeGroup = current.VolumeGroups[vi];
                        for (int gi = 0; gi < volumeGroup.LODGroups.Count; ++gi)
                        {
                            renderer.Renderers.Add(new LODGroupRendererProxy(volumeGroup.LODGroups[gi]));
                        }
                    }
                }

                rendererList.Add(renderer);

                //If FirstChildIndex of the parent is empty, this is the first node.
                if (parentIndex != -1)
                {
                    var parentRenderer = rendererList[parentIndex];
                    parentRenderer.Renderers.Add(new VolumeRendererProxy(this, currentIndex));
                }

                for (int i = 0; i < current.ChildVolumes.Count; ++i)
                {
                    treeTrevelQueue.Enqueue(current.ChildVolumes[i]);
                    parentIndexQueue.Enqueue(currentIndex);
                }
            }

            m_Bounds = boundsList;
            m_Renderers = rendererList;

            if ( m_Bounds.Count > 0)
                m_ActiveVolumes.Add(0);
        }

        private FrustumCullHelper cullHelper = new FrustumCullHelper();
        private void OnPreCull(Camera cam)
        {
            var cameraTransform = cam.transform;
            var cameraPosition = cameraTransform.position;

            cullHelper.Set(cam);
            
            float preRelative = 0.0f;
            if (cam.orthographic)
            {
                preRelative = 0.5f / cam.orthographicSize;
            }
            else
            {
                float halfAngle = Mathf.Tan(Mathf.Deg2Rad * cam.fieldOfView * 0.5F);
                preRelative = 0.5f / halfAngle;
            }

            preRelative = preRelative * QualitySettings.lodBias;

            //Items are added or removed in the loop.
            //That's OK because be edited item is always after current index.
            //DO NOT CHANGE this loop to foreach.
            for (int i = 0; i < m_ActiveVolumes.Count; ++i)
            {
                int index = m_ActiveVolumes[i];
                var bounds = m_Bounds[index];
                var renderer = m_Renderers[index];

                if (cullHelper.IsOutside(bounds.Center, bounds.Radius))
                {
                    continue;
                }

                float distance = 1.0f;
                if ( cam.orthographic == false)
                    distance = Vector3.Distance(bounds.Center, cameraPosition);

                float relativeHeight = bounds.Size * preRelative / distance;
                bool renderDetail = relativeHeight > 0.3f;

                for (int li = 0; li < renderer.LODMeshes.Count; ++li)
                {
                    renderer.LODMeshes[li].enabled = !renderDetail;
                }
                for (int ri = 0; ri < renderer.Renderers.Count; ++ri)
                {
                    renderer.Renderers[ri].SetEnable(renderDetail);
                }
            }
        }        
    }

}