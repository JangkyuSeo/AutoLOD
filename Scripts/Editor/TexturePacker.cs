using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEditor.Experimental.AutoLOD
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
                        Textures = new HashSet<Texture2D>(group.textures)

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
                            Textures = unionTextures
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
                    yield return TextureAtlasModule.instance.GetTextureAtlas(pack.Textures.ToArray(), atlas =>
                    {
                        atlasGroups.Add(new AtlasGroup()
                        {
                            GameObjects = pack.GameObjects,
                            Atlas = atlas
                        });
                    });
                }
                
                Debug.Log("Packing count : " + maximum + ", textures : " + packTextures.Count);
            }
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

       
    }

}