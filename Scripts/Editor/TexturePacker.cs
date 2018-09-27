﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.AutoLOD
{
    public class TexturePacker
    {
        public TexturePacker()
        {

        }

        public void  AddTextureGroup(GameObject go, Texture2D[] textures)
        {
            Group group = new Group();
            group.go = go;
            group.textures = new HashSet<Texture2D>(textures);

            groups.Add(group);
        }

        public TextureAtlas GetAtlas(GameObject go)
        {
            foreach (var group in atlasGroups)
            {
                if (group.GameObjects.Contains(go))
                    return group.Atlas;
            }
            return null;
        }

        class PackTexture
        {
            public List<GameObject> GameObjects;
            public HashSet<Texture2D> Textures;
            public int PackableTextureCount;


        }

        class Score
        {
            public PackTexture Lhs;
            public PackTexture Rhs;
            public int Match;

            public static Score GetScore(PackTexture lhs, PackTexture rhs)
            {
                int match = lhs.Textures.Intersect(rhs.Textures).Count();
                return new Score()
                {
                    Lhs = lhs,
                    Rhs = rhs,
                    Match = match
                };
            }
        }
        class Group
        {
            public GameObject go;
            public HashSet<Texture2D> textures;
        }

        class AtlasGroup
        {
            public List<GameObject> GameObjects;
            public TextureAtlas Atlas;
        }

        List<Group> groups = new List<Group>();
        List<AtlasGroup> atlasGroups = new List<AtlasGroup>();


        public IEnumerator Pack(int packTextureSize, int maxPieceSize)
        {
            //First, we should separate each group by count.
            Dictionary<int, List<Group>> groupCluster = new Dictionary<int, List<Group>>();

            for (int i = 0; i < groups.Count; ++i)
            {
                Group group = groups[i];
                int maximum = GetMaximumTextureCount(packTextureSize, maxPieceSize, group.textures.Count);
                if ( groupCluster.ContainsKey(maximum) == false )
                    groupCluster[maximum] = new List<Group>();

                groupCluster[maximum].Add(group);
            }

            //Second, we should figure out which group should be combined from each cluster.
            foreach (var cluster in groupCluster)
            {
                int maximum = cluster.Key;
                List<PackTexture> packTextures = new List<PackTexture>();

                foreach (var group in cluster.Value)
                {
                    packTextures.Add(new PackTexture()
                    {
                        GameObjects = new List<GameObject>() {group.go},
                        Textures = new HashSet<Texture2D>(group.textures),
                        PackableTextureCount = maximum
                    });                    
                }

                List<Score> scoreList = new List<Score>();
                for (int i = 0; i < packTextures.Count; ++i)
                {
                    for (int j = i + 1; j < packTextures.Count; ++j)
                    {
                        scoreList.Add(Score.GetScore(packTextures[i], packTextures[j]));
                    }
                }

                scoreList.Sort((lhs, rhs) => rhs.Match - lhs.Match);

                for (int i = 0; i < scoreList.Count; ++i)
                {
                    HashSet<Texture2D> unionTextures = new HashSet<Texture2D>(scoreList[i].Lhs.Textures.Union(scoreList[i].Rhs.Textures));
                    if (unionTextures.Count <= maximum)
                    {
                        PackTexture lhs = scoreList[i].Lhs;
                        PackTexture rhs = scoreList[i].Rhs;

                        List<GameObject> newGameObjects = new List<GameObject>(scoreList[i].Lhs.GameObjects.Concat(scoreList[i].Rhs.GameObjects));

                        PackTexture newPackTexture = new PackTexture()
                        {
                            GameObjects = newGameObjects,
                            Textures = unionTextures,
                            PackableTextureCount = maximum
                        };

                        
                        packTextures.Remove(lhs);
                        packTextures.Remove(rhs);
                        packTextures.Add(newPackTexture);

                        //Remove merged Score and make Score by new Pack Texture.
                        scoreList.RemoveAll(score => score.Lhs == lhs || score.Lhs == rhs ||
                                                     score.Rhs == lhs || score.Rhs == rhs );

                        for (int j = 0; j < packTextures.Count - 1; ++j)
                        {
                            scoreList.Add(Score.GetScore(packTextures[j], newPackTexture));
                        }

                        scoreList.Sort((l, r) => r.Match - l.Match);

                        //for start first loop
                        i = -1;
                    }
                }

                foreach (var pack in packTextures)
                {
                    var atlas = MakeTextureAtlas(pack, packTextureSize);
                    atlasGroups.Add(new AtlasGroup()
                    {
                        GameObjects = pack.GameObjects,
                        Atlas = atlas
                    });

                    yield return null;
                }
                
                Debug.Log("Packing count : " + maximum + ", textures : " + packTextures.Count);
            }
        }

        private TextureAtlas MakeTextureAtlas(PackTexture packTexture, int packTextureSize)
        {
            TextureAtlas atlas = ScriptableObject.CreateInstance<TextureAtlas>();
            Texture2D textureAtlas = new Texture2D(packTextureSize, packTextureSize, TextureFormat.RGBA32, false);

            int itemCount = (int) Math.Sqrt(packTexture.PackableTextureCount);
            int itemSize = packTextureSize / itemCount;

            int index = 0;

            atlas.uvs = new Rect[packTexture.Textures.Count];
            atlas.textures = packTexture.Textures.ToArray();

            foreach (var texture in atlas.textures)
            {
                int width, height;
                Color[] buffer = GetTextureColors(texture, itemSize, out width, out height);

                int col = index % itemCount;
                int row = index / itemCount;

                int x = col * itemSize;
                int y = row * itemSize;

                textureAtlas.SetPixels(x, y, width, height, buffer);

                atlas.uvs[index] = new Rect(
                    (float)x / (float)packTextureSize,
                    (float)y / (float)packTextureSize,
                    (float)width / (float)packTextureSize,
                    (float)height / (float)packTextureSize);

                index += 1;
            }

            textureAtlas.Apply();

            var name = Path.GetRandomFileName();
            textureAtlas = SaveTexture(textureAtlas, name);

            atlas.textureAtlas = textureAtlas;
            return atlas;
        }


        private Color[] GetTextureColors(Texture2D texture, int maxItemSize, out int width, out int height)
        {
            //make to texture readable.
            var assetImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            var textureImporter = assetImporter as TextureImporter;
            if (textureImporter && !textureImporter.isReadable)
            {
                textureImporter.isReadable = true;
                textureImporter.SaveAndReimport();
            }
      
            int sideSize = Math.Max(texture.width, texture.height);

            //if texture can put into an atlas by original size, go ahead.
            //Also, check mipmap is able to put into an atlas.
            for (int i = 0; i < texture.mipmapCount; ++i)
            {
                if ((sideSize >> i) <= maxItemSize)
                {
                    width = texture.width >> i;
                    height = texture.height >> i;
                    return texture.GetPixels(i);
                }
            }

            //we should resize texture and return it buffers.
            float ratio = (float)texture.width / (float)texture.height;
            if (ratio > 1.0f)
            {
                width = maxItemSize;
                height = (int)(maxItemSize / ratio);
            }
            else
            {
                width = (int) (maxItemSize / ratio);
                height = maxItemSize;
            }

            Texture2D resizeTexture = new Texture2D(texture.width, texture.height, texture.format, false);
            Graphics.CopyTexture(texture, resizeTexture);
            resizeTexture.Resize(width, height);

            return resizeTexture.GetPixels();
        }

        private int GetMaximumTextureCount(int packTextureSize, int maxPieceSize, int textureCount)
        {
            int minTextureCount = packTextureSize / maxPieceSize;
            //width * height
            minTextureCount = minTextureCount * minTextureCount;

            //we can't pack one texture.
            //so, we should use half size texture.
            while (minTextureCount < textureCount)
                minTextureCount = minTextureCount * 4;

            return minTextureCount;
        }

        static Texture2D SaveTexture(Texture2D texture, string name)
        {
            var path = GetAtlasesDirectory() + name;            
            path = Path.ChangeExtension(path, "PNG");
            
            //remove first assets/
            path = path.Substring(path.IndexOf(Path.DirectorySeparatorChar) + 1);                       

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


        static string GetAtlasesDirectory()
        {
            var scene = SceneManager.GetActiveScene();

            var path = Path.GetDirectoryName(scene.path);
  
            path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            path = path + Path.DirectorySeparatorChar + scene.name
                   + Path.DirectorySeparatorChar + "Atlases" + Path.DirectorySeparatorChar;

            return path;

        }

       
    }

}