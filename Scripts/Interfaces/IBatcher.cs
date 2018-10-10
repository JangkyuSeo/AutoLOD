using System.Collections;
using UnityEngine;

namespace Unity.AutoLOD
{
    public interface IBatcher
    {
        /// <summary>
        /// Combine children renderers of this GameObject (NOTE: Runs as a coroutine)
        /// </summary>
        /// <param name="go">GameObject hierarchy to batch</param>
        IEnumerator Batch(GameObject hlodRoot, System.Action<float> progress);

        IBatcherOption GetBatcherOption();
    }

    public interface IBatcherOption
    {
        void OnGUI();
    }
}
