using System.Collections;
using UnityEngine;

namespace Unity.AutoLOD
{
    /// <summary>
    /// A batcher that preserves materials when combining meshes (does not reduce draw calls)
    /// </summary>
    class MaterialPreservingBatcher : IBatcher
    {
        public IEnumerator Batch(GameObject hlodRoot)
        {
            foreach (Transform child in hlodRoot.transform)
            {
                var go = child.gameObject;
                var renderers = go.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    r.enabled = true;
                    yield return null;
                }

                StaticBatchingUtility.Combine(go);
                yield return null;
            }
        }

        public IBatcherOption GetBatcherOption()
        {
            return null;

        }
    }
}
