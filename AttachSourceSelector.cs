using System.Linq;
using UnityEngine;

namespace UniversalGrasp
{
    internal static class AttachSourceSelector
    {
        internal static GameObject Find(GameObject item)
        {
            GameObject attach = item.transform.Find("attach")?.gameObject;
            if (attach != null && attach.GetComponentsInChildren<Renderer>(true).Any())
            {
                return attach;
            }

            GameObject candidate = null;
            bool multipleCandidates = false;
            foreach (Transform child in item.transform)
            {
                if (child.gameObject.layer != item.layer) continue;
                if (candidate != null)
                {
                    multipleCandidates = true;
                    break;
                }

                candidate = child.gameObject;
            }

            if (!multipleCandidates && candidate != null) return candidate;

            candidate = null;
            multipleCandidates = false;
            foreach (Transform child in item.transform)
            {
                if (candidate != null)
                {
                    multipleCandidates = true;
                    break;
                }

                candidate = child.gameObject;
            }

            if (!multipleCandidates && candidate != null) return candidate;

#if DEBUG
            UniversalGraspPlugin.Log?.LogDebug(
                $"Using prefab root as attach source: prefab={item.name}.");
#endif
            return item;
        }
    }
}
