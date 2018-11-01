using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.AutoLOD
{
    public class Config : ScriptableObject
    {
        [Serializable]
        public class GroupConfig
        {
            [SerializeField]
            private string m_Name;
            [SerializeField]
            private bool m_VolumeSimplification = true;
            [SerializeField]
            private float m_VolumePolygonRatio = 0.5f;

            [SerializeField]
            private string m_BatcherTypeStr;
            private IBatcher m_Batcher;
            
            [SerializeField]
            private float m_LODThresholdSize = 5.0f;

            [SerializeField]
            private int m_LODTriangleMin = 10;
            [SerializeField]
            private int m_LODTriangleMax = 500;

            public string Name
            {
                get { return m_Name; }
            }

            public bool VolumeSimplification
            {
                set
                {
                    m_VolumeSimplification = value;
                    EditorUtility.SetDirty(GetInstance());
                }
                get { return m_VolumeSimplification; }
            }

            public float VolumePolygonRatio
            {
                set
                {
                    m_VolumePolygonRatio = value; 
                    EditorUtility.SetDirty(GetInstance());
                }
                get { return m_VolumePolygonRatio; }
            }

            public Type BatcherType
            {
                set
                {
                    string typeStr = "";
                    if (value != null)
                    {
                        typeStr = value.AssemblyQualifiedName;
                    }

                    if (typeStr != m_BatcherTypeStr)
                    {
                        m_BatcherTypeStr = typeStr;
                        m_Batcher = null;
                        EditorUtility.SetDirty(GetInstance());
                    }
                }
                get
                {
                    if (string.IsNullOrEmpty(m_BatcherTypeStr))
                        return null;

                    return Type.GetType(m_BatcherTypeStr);
                }
            }

            public IBatcher Batcher
            {
                get
                {
                    if (m_Batcher == null)
                        m_Batcher = (IBatcher) Activator.CreateInstance(BatcherType, Name);
                    return m_Batcher;
                }
            }

            public float LODThresholdSize
            {
                set
                {
                    m_LODThresholdSize = value;
                    EditorUtility.SetDirty(GetInstance());
                }
                get { return m_LODThresholdSize; }
            }

            public int LODTriangleMin
            {
                set
                {
                    m_LODTriangleMin = value;
                    EditorUtility.SetDirty(GetInstance());
                }
                get { return m_LODTriangleMin; }
            }

            public int LODTriangleMax
            {
                set
                {
                    m_LODTriangleMax = value; 
                    EditorUtility.SetDirty(GetInstance());
                }
                get { return m_LODTriangleMax; }
            }

            public GroupConfig(string name)
            {
                m_Name = name;
            }
        }

        [SerializeField]
        private float m_VolumeSize = 30.0f;
        [SerializeField]
        private float m_LODRange = 0.3f;
        [SerializeField]
        private List<GroupConfig> m_GroupConfigs = new List<GroupConfig>();


        private static Config s_Instance;
        public static float VolumeSize
        {
            set
            {
                var instance = GetInstance();

                if (instance.m_VolumeSize != value)
                {
                    instance.m_VolumeSize = value;
                    EditorUtility.SetDirty(instance);
                }
            }
            get
            {
                return GetInstance().m_VolumeSize;
            }
        }

        public static float LODRange
        {
            set
            {
                var instance = GetInstance();
                
                if (instance.m_LODRange != value)
                {
                    instance.m_LODRange = value;
                    EditorUtility.SetDirty(instance);
                }

            }
            get
            {
                return GetInstance().m_LODRange;
            }
        }

        public static GroupConfig GetGroupConfig(string name)
        {
            var instance = GetInstance();

            foreach (var groupConfig in instance.m_GroupConfigs)
            {
                if (groupConfig.Name == name)
                    return groupConfig;
            }

            var newGroupConfig = new GroupConfig(name);
            instance.m_GroupConfigs.Add(newGroupConfig);
            EditorUtility.SetDirty(instance);

            return newGroupConfig;
        }

        private static Config GetInstance()
        {
            if (s_Instance != null)
                return s_Instance;

            string path = GetConfigPath();
            s_Instance = AssetDatabase.LoadAssetAtPath<Config>(path);

            if (s_Instance == null)
            {
                s_Instance = CreateInstance<Config>();
                AssetDatabase.CreateAsset(s_Instance, path);
            }

            return s_Instance;
        }
      

        private static string GetConfigPath()
        {
            return "Assets/" + SceneLOD.GetScenePath() + "SceneLODConfig.asset";
        }

        
        void OnEnable()
        {
            SceneManager.sceneLoaded += SceneManager_sceneLoaded;
        }

        
        void OnDisable()
        {
            SceneManager.sceneLoaded -= SceneManager_sceneLoaded;
        }

        private void SceneManager_sceneLoaded(Scene scene, LoadSceneMode mode)
        {
            s_Instance = null;
        }
        
    }

}