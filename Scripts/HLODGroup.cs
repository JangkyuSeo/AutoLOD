using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace Unity.AutoLOD
{
    public class HLODGroup : MonoBehaviour
    {
        [SerializeField] private string m_GroupName = "";

        public string GroupName
        {
            get { return m_GroupName; }
        }

#if UNITY_EDITOR
        public static Dictionary<string, List<HLODGroup>> FindAllGroups()
        {
            Dictionary<string, List<HLODGroup>> allGroups = new Dictionary<string, List<HLODGroup>>();

            var hlodGroups = FindObjectsOfType<HLODGroup>();
            
            foreach (var group in hlodGroups)
            {
                if (allGroups.ContainsKey(group.GroupName) == false)
                {
                    allGroups.Add(group.GroupName, new List<HLODGroup>());
                }

                allGroups[group.GroupName].Add(group);
            }

            return allGroups;
        }

#endif
    }

}