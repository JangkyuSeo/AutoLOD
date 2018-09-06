using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Unity.AutoLOD
{
    public class TextureAtlasModule : ScriptableSingleton<TextureAtlasModule>
    {
        List<TextureAtlas> m_Atlases = new List<TextureAtlas>();

        void OnEnable()
        {
            m_Atlases.Clear();

            var atlases = Resources.FindObjectsOfTypeAll<TextureAtlas>();
            m_Atlases.AddRange(atlases);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="texture"></param>
        /// <returns>imported texture</returns>
        static Texture2D SaveTexture(Texture2D texture, string name)
        {
            var path = "AutoLOD/Generated/Atlases/" + name;
            path = Path.ChangeExtension(path, "PNG");

            var assetPath = "Assets/" + path;
            var dataPath = Application.dataPath + "/" + path;

            var dirPath = Path.GetDirectoryName(dataPath);
            if (Directory.Exists(dirPath) == false)
            {
                Directory.CreateDirectory(dirPath);
            }

            byte[] binary = texture.EncodeToPNG();
            File.WriteAllBytes(dataPath,binary);

            AssetDatabase.ImportAsset(assetPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);


        }

        static void SaveUniqueAtlasAsset(UnityObject asset, string name)
        {
            var directory = "Assets/AutoLOD/Generated/Atlases/";
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var path = directory + name;
            path = Path.ChangeExtension(path, "asset");
            AssetDatabase.CreateAsset(asset, path);


        }

        public IEnumerator GetTextureAtlas(Texture2D[] textures, Action<TextureAtlas> callback)
        {
            TextureAtlas atlas = null;

            // Clear out any atlases that were removed
            m_Atlases.RemoveAll(a => a == null);
            yield return null;


            atlas = ScriptableObject.CreateInstance<TextureAtlas>();

            foreach (var t in textures)
            {
                var assetImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t));
                var textureImporter = assetImporter as TextureImporter;
                if (textureImporter && !textureImporter.isReadable)
                {
                    textureImporter.isReadable = true;
                    textureImporter.SaveAndReimport();
                }
                else if (!assetImporter)
                {
                    // In-memory textures need to be saved to disk in order to be referenced by the texture atlas
                    SaveUniqueAtlasAsset(t, Path.GetRandomFileName());
                }
                yield return null;
            }

            var textureAtlas = new Texture2D(0, 0, TextureFormat.RGBA32, true, PlayerSettings.colorSpace == ColorSpace.Linear);
            var uvs = textureAtlas.PackTextures(textures.ToArray(), 0, 1024, false);

            //for use same name texture and atlas.
            var name = Path.GetRandomFileName();

            textureAtlas = SaveTexture(textureAtlas, name);

            if (uvs != null)
            {
                atlas.textureAtlas = textureAtlas;
                atlas.uvs = uvs;
                atlas.textures = textures;

                SaveUniqueAtlasAsset(atlas, name);

                m_Atlases.Add(atlas);
            }

            yield return null;


            if (callback != null)
                callback(atlas);
        }
    }
}
