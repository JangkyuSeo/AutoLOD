using System.Collections;
using UnityEngine;

namespace Unity.AutoLOD
{
    /// <summary>
    /// A batcher that preserves materials when combining meshes (does not reduce draw calls)
    /// </summary>
    class MaterialPreservingBatcher : IBatcher
    {
        public MaterialPreservingBatcher(string groupName)
        {
            
        }
        public IEnumerator Batch(GameObject hlodRoot, System.Action<float> progress)
        {
            //foreach (Transform child in hlodRoot.transform)
            for(int i = 0; i < hlodRoot.transform.childCount; ++i)
            {
                var child = hlodRoot.transform.GetChild(i);
                var go = child.gameObject;
                var renderers = go.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    r.enabled = true;
                    yield return null;
                }

                StaticBatchingUtility.Combine(go);
                if (progress != null)
                    progress((float) i / (float) hlodRoot.transform.childCount);
                yield return null;
            }
        }

        public IBatcherOption GetBatcherOption()
        {
            return null;

        }
    }
}
