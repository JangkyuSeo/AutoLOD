﻿#if ENABLE_SIMPLYGON
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Simplygon;
using Simplygon.SPL.v80.Processor;
using Simplygon.SPL.v80.Settings;
using Simplygon.Unity.EditorPlugin;
using Simplygon.Unity.EditorPlugin.Jobs;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;
#endif

#if UNITY_2017_3_OR_NEWER
[assembly: Unity.AutoLOD.OptionalDependency("Simplygon.Unity.EditorPlugin.Window", "ENABLE_SIMPLYGON")]
#endif

#if ENABLE_SIMPLYGON
namespace Unity.AutoLOD
{
    public class SimplygonMeshSimplifier : IMeshSimplifier
    {
        private const string lodPath = "Assets/LODs/";
        private const string materialPath = lodPath + "Materials";

        IEnumerator Simplify(WorkingMesh inputMesh, WorkingMesh outputMesh, float quality)
        {

            Renderer renderer = null;

            UnityCloudJob job = null;
            string jobName = null;

            var assembly = typeof(SharedData).Assembly;
            var cloudJobType = assembly.GetType("Simplygon.Cloud.Yoda.IntegrationClient.CloudJob");
            var jobNameField = cloudJobType.GetField("name", BindingFlags.NonPublic | BindingFlags.Instance);

            MonoBehaviourHelper.ExecuteOnMainThread(() =>
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags("Temp", HideFlags.HideAndDontSave,
                        typeof(MeshRenderer), typeof(MeshFilter));
                var mf = go.GetComponent<MeshFilter>();
                var mesh = new Mesh();
                inputMesh.ApplyToMesh(mesh);
                mf.sharedMesh = mesh;
                renderer = go.GetComponent<MeshRenderer>();

                var sharedMaterials = new Material[mesh.subMeshCount];

                if (Directory.Exists(materialPath) == false)
                {
                    Directory.CreateDirectory(materialPath);
                }

                    //For submesh, we should create material asset.
                    //otherwise, simplygon will be combine uv of submesh.
                    for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var material = new Material(Shader.Find("Standard"));
                    material.name = "Material " + i.ToString();

                    AssetDatabase.CreateAsset(material, materialPath + "/" + material.name);

                    sharedMaterials[i] = material;
                }

                renderer.sharedMaterials = sharedMaterials;
                renderer.enabled = false;

                EditorWindow.GetWindow<Window>(); // Must be visible for background processing

                    SharedData.Instance.Settings.SetDownloadAssetsAutomatically(true);

                var lodChainProperty = typeof(SharedData).GetProperty("LODChain");
                var lodChainList = lodChainProperty.GetValue(SharedData.Instance, null) as IList;
                var lodChain = lodChainList[0];

                var processNodeType = assembly.GetType("Simplygon.SPL.v80.Node.ProcessNode");
                var processorProperty = processNodeType.GetProperty("Processor");
                var processor = processorProperty.GetValue(lodChain, null);

                var reductionProcessorType = assembly.GetType("Simplygon.SPL.v80.Processor.ReductionProcessor");
                var reductionSettingsProperty = reductionProcessorType.GetProperty("ReductionSettings");
                var reductionSettingsType = assembly.GetType("Simplygon.SPL.v80.Settings.ReductionSettings");
                var reductionSettings = reductionSettingsProperty.GetValue(processor, null);

                var triangleRatioProperty = reductionSettingsType.GetProperty("TriangleRatio");
                triangleRatioProperty.SetValue(reductionSettings, quality, null);

                jobName = Path.GetRandomFileName().Replace(".", string.Empty);
                var prefabList = PrefabUtilityEx.GetPrefabsForSelection(new List<GameObject>() { go });
                var generalManager = SharedData.Instance.GeneralManager;
                generalManager.CreateJob(jobName, "myPriority", prefabList, () =>
                {
                    foreach (var j in generalManager.JobManager.Jobs)
                    {
                        var name = (string)jobNameField.GetValue(j.CloudJob);
                        if (name == jobName)
                            job = j;
                    }
                });


            });

            while (job == null)
            {
                yield return null;
            }


            while (string.IsNullOrEmpty(job.AssetDirectory))
            {
                yield return null;
            }

            MonoBehaviourHelper.ExecuteOnMainThread(() =>
            {
                var customDataType = assembly.GetType("Simplygon.Cloud.Yoda.IntegrationClient.CloudJob+CustomData");
                var pendingFolderNameProperty = customDataType.GetProperty("UnityPendingLODFolderName");
                var jobCustomDataProperty = cloudJobType.GetProperty("JobCustomData");
                var jobCustomData = jobCustomDataProperty.GetValue(job.CloudJob, null);
                var jobFolderName = pendingFolderNameProperty.GetValue(jobCustomData, null) as string;

                var lodAssetDir = lodPath + job.AssetDirectory;
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(string.Format("{0}/{1}_LOD1.prefab", lodAssetDir,
                    jobName));
                mesh.ApplyToWorkingMesh(outputMesh);

                    //job.CloudJob.StateHandler.RequestJobDeletion();
                    AssetDatabaseEx.DeletePendingLODFolder(jobFolderName);
                AssetDatabase.DeleteAsset(lodAssetDir);
                AssetDatabase.DeleteAsset(materialPath);

                UnityObject.DestroyImmediate(renderer.gameObject);
            });
        }
        public void Simplify(WorkingMesh inputMesh, WorkingMesh outputMesh, float quality, Action completeAction)
        {
            SimplifierRunner.instance.EnqueueSimplification(Simplify(inputMesh, outputMesh, quality), completeAction);

        }



    }
}
#endif