using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.AutoLOD
{
    public class SceneLODUpdater : MonoBehaviour
    {
        //Removed interface because unity does not support interface serialize.
        [Serializable]
        class LODGroupRendererProxy
        {
            [SerializeField]
            private LODGroup m_Group;

            public LODGroupRendererProxy(LODGroup group)
            {
                m_Group = group;
                if (m_Group != null)
                    m_Group.SetEnabled(true);
            }

            public void Initialize()
            {
                if (m_Group != null)
                    m_Group.SetEnabled(true);
            }
            public void SetEnable(bool enable)
            {
                if (m_Group != null)
                    m_Group.SetEnabled(enable);
            }
        }

        [Serializable]
        class VolumeRendererProxy
        {
            [SerializeField]
            private int m_Index;

            private SceneLODUpdater m_Updater;
            private bool? m_LastState;
            public VolumeRendererProxy(int index)
            {
                m_Index = index;
                
            }
            public void Initialize(SceneLODUpdater updater)
            {
                m_Updater = updater;
                m_LastState = null;
            }
            public void SetEnable(bool enable)
            {
                if (m_LastState == enable)
                    return;

                if (enable == true)
                {
                    m_Updater.m_ActiveVolumes.AddLast(m_Index);
                }
                else
                {
                    var renderer = m_Updater.m_Renderers[m_Index];
                    for (int li = 0; li < renderer.LODMeshes.Count; ++li)
                    {
                        renderer.LODMeshes[li].enabled = false;
                    }

                    for (int li = 0; li < renderer.LODGroups.Count; ++li)
                    {
                        renderer.LODGroups[li].SetEnable(false);
                    }
                    for (int vi = 0; vi < renderer.Volumes.Count; ++vi)
                    {
                        renderer.Volumes[vi].SetEnable(false);
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

        [Serializable]
        struct VolumeBounds
        {
            public Vector3 Center;
            public float Size;        //< volume is cuboid. so, every axis is the same size.
            public float Radius;
        }

        [Serializable]
        struct VolumeRenderer
        {
            public List<LODGroupRendererProxy> LODGroups;
            public List<VolumeRendererProxy> Volumes;

            public List<Renderer> LODMeshes;
        }

        [SerializeField]
        [HideInInspector]
        private List<VolumeBounds> m_Bounds;
        [SerializeField]
        [HideInInspector]
        private List<VolumeRenderer> m_Renderers;

        private LinkedList<int> m_ActiveVolumes = new LinkedList<int>();

        void ResetLODGroups()
        {
            for (int i = 0; i < m_Renderers.Count; ++i)
            {
                var renderer = m_Renderers[i];
                for (int li = 0; li < renderer.LODGroups.Count; ++li)
                {
                    renderer.LODGroups[li].Initialize();
                }
                for (int vi = 0; vi < renderer.Volumes.Count; ++vi)
                {
                    renderer.Volumes[vi].Initialize(this);
                }

                for (int mi = 0; mi < renderer.LODMeshes.Count; ++mi)
                {
                    renderer.LODMeshes[mi].enabled = false;
                }
            }

            m_ActiveVolumes.Clear();
            if(m_Bounds.Count > 0)
                m_ActiveVolumes.AddLast(0);
        }
#region UnityEvents

        void Start()
        {
        }

        void OnEnable()
        {
            Camera.onPreCull += OnPreCull;
            ResetLODGroups();
        }

        void OnDisable()
        {
            Camera.onPreCull -= OnPreCull;
            ResetLODGroups();
        }
#endregion

        public void Build(LODVolume rootLODVolume)
        {
            if (rootLODVolume== null)
            {
                Debug.LogError("SceneLODUpdate build failed. RootLODVolume is null.");
                return;
            }

            //it build by BFS.
            List<VolumeBounds> boundsList = new List<VolumeBounds>();
            List<VolumeRenderer> rendererList = new List<VolumeRenderer>();

            Queue<LODVolume> treeTrevelQueue = new Queue<LODVolume>();
            Queue<int> parentIndexQueue = new Queue<int>();

            treeTrevelQueue.Enqueue(rootLODVolume);
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
                renderer.LODGroups = new List<LODGroupRendererProxy>();
                renderer.Volumes = new List<VolumeRendererProxy>();
                

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
                            renderer.LODGroups.Add(new LODGroupRendererProxy(volumeGroup.LODGroups[gi]));
                        }
                    }
                }

                rendererList.Add(renderer);

                //If FirstChildIndex of the parent is empty, this is the first node.
                if (parentIndex != -1)
                {
                    var parentRenderer = rendererList[parentIndex];
                    parentRenderer.Volumes.Add(new VolumeRendererProxy( currentIndex));
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
                m_ActiveVolumes.AddLast(0);
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
            for(var node = m_ActiveVolumes.First; node != null; node=node.Next)
            {
                int index = node.Value;
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
                for (int li = 0; li < renderer.LODGroups.Count; ++li)
                {
                    renderer.LODGroups[li].SetEnable(renderDetail);
                }
                for (int vi = 0; vi < renderer.Volumes.Count; ++vi)
                {
                    renderer.Volumes[vi].SetEnable(renderDetail);
                }

            }
        }        
    }

}