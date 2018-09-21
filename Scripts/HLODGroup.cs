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

            if ( allGroups.ContainsKey("") == false )
                allGroups.Add("", new List<HLODGroup>());

            //sort by key
            return allGroups.OrderBy(key => key.Key).ToDictionary(keyItem=>keyItem.Key, valueItem=>valueItem.Value);
        }

#endif
    }

}