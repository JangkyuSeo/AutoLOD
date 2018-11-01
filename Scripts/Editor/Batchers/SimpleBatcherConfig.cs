using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.AutoLOD
{
    public class SimpleBatcherConfig : ScriptableObject
    {
        [Serializable]
        public class ConfigData
        {
            [SerializeField]
            private string m_GroupName;

            [SerializeField]
            private int m_PackTextureSelect = 3;  //< default size is 1024
            [SerializeField]
            private int m_LimitTextureSelect = 2; //< default size is 128

            [SerializeField]
            private Material m_BatchMaterial;

            public ConfigData(string groupName)
            {
                m_GroupName = groupName;
            }
            public string GroupName
            {
                get { return m_GroupName; }
            }

            public int PackTextureSelect
            {
                set
                {
                    m_PackTextureSelect = value; 
                    EditorUtility.SetDirty(GetInstance());
                }
                get { return m_PackTextureSelect; }
            }

            public int LimitTextureSelect
            {
                set
                {
                    m_LimitTextureSelect = value; 
                    EditorUtility.SetDirty(GetInstance());

                }
                get { return m_LimitTextureSelect; }
            }

            public Material BatchMaterial
            {
                set
                {
                    m_BatchMaterial = value; 
                    EditorUtility.SetDirty(GetInstance());
                }
                get { return m_BatchMaterial; }
            }


        }

        private static SimpleBatcherConfig s_Instance;

        [SerializeField]
        private List<ConfigData> m_ConfigDatas = new List<ConfigData>();

        public static ConfigData Get(string name)
        {
            var config = GetInstance();
            foreach (var data in config.m_ConfigDatas)
            {
                if (data.GroupName == name)
                    return data;
            }

            ConfigData newData = new ConfigData(name);
            config.m_ConfigDatas.Add(newData);
            EditorUtility.SetDirty(config);
            
            return newData;
        }

        private static SimpleBatcherConfig GetInstance()
        {
            if (s_Instance != null)
                return s_Instance;

            string path = GetConfigPath();
            s_Instance = AssetDatabase.LoadAssetAtPath<SimpleBatcherConfig>(path);

            if (s_Instance == null)
            {
                s_Instance = CreateInstance<SimpleBatcherConfig>();
                AssetDatabase.CreateAsset(s_Instance, path);
            }
            return s_Instance;
        }

        private static string GetConfigPath()
        {
            return "Assets/" + SceneLOD.GetScenePath() + "SceneLODSimpleBatcherConfig.asset";
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