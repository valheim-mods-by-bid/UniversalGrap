using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniversalGrasp
{
    /// <summary>
    /// Creates inactive visual-only clones and removes components in dependency order.
    /// Dependency sorting is adapted from Jotunn's RenderManager (MIT license).
    /// </summary>
    internal static class VisualCloneCleaner
    {
        private static readonly Dictionary<Type, List<Type>> ComponentDependencies =
            new Dictionary<Type, List<Type>>();

        internal static bool TryCreate(GameObject source, out GameObject clone, out string error)
        {
            clone = null;
            error = null;
            GameObject temporaryParent = new GameObject("UniversalGrasp_VisualClone");
            temporaryParent.SetActive(false);

            try
            {
                // Instantiate(source, temporaryParent) keeps the source's world position
                // by default. Capture the prefab-local transform explicitly; otherwise
                // detaching the clone later can turn a prefab position into a large hand
                // offset (for example y=-50 on potion visuals).
                Vector3 sourceLocalScale = source.transform.localScale;

                ZNetView.m_forceDisableInit = true;
                try
                {
                    clone = Object.Instantiate(source, temporaryParent.transform);
                }
                finally
                {
                    ZNetView.m_forceDisableInit = false;
                }

                if (!RemoveComponentsRecursively(clone.transform, out error))
                {
                    clone = null;
                    return false;
                }

                // AttachItem expects a newly created visual to be aligned with the
                // target joint. Some Valheim attach prefabs carry authoring/world
                // offsets (the Mead prefab, for example, has a local Y of about -50),
                // which must not become a hand offset. Keep only the authored scale;
                // the vanilla method applies the joint position and rotation.
                clone.transform.localPosition = Vector3.zero;
                clone.transform.localRotation = Quaternion.identity;
                clone.transform.localScale = sourceLocalScale;
                clone.transform.SetParent(null, false);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                clone = null;
                return false;
            }
            finally
            {
                Object.DestroyImmediate(temporaryParent);
            }
        }

        private static bool RemoveComponentsRecursively(Transform parent, out string error)
        {
            for (int index = 0; index < parent.childCount; index++)
            {
                if (!RemoveComponentsRecursively(parent.GetChild(index), out error))
                {
                    return false;
                }
            }

            List<Type> removalOrder = GetRemovalOrder(parent);
            if (removalOrder == null)
            {
                error = $"Cyclic component dependencies detected on {parent.name}.";
                return false;
            }

            foreach (Type type in removalOrder)
            {
                foreach (Component component in parent.gameObject.GetComponents(type))
                {
                    if (IsVisualComponent(component))
                    {
                        continue;
                    }

                    try
                    {
                        Object.DestroyImmediate(component);
                    }
                    catch (Exception exception)
                    {
                        error = $"Could not remove {type.FullName} from {parent.name}: {exception.Message}";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        private static bool IsVisualComponent(Component component)
        {
            return component is Transform
                || component is MeshFilter
                || component is Renderer
                || component is Light
                || component is ParticleSystem
                || component is TrailRenderer
                || component is LineRenderer
                || component is Projector;
        }

        private static List<Type> GetRemovalOrder(Transform transform)
        {
            var result = new List<Type>();
            var visited = new HashSet<Type>();
            var recursionStack = new HashSet<Type>();

            foreach (Component component in transform.gameObject.GetComponents<Component>())
            {
                if (component != null
                    && !TopologicalSort(component.GetType(), visited, recursionStack, result))
                {
                    return null;
                }
            }

            result.Reverse();
            return result;
        }

        private static bool TopologicalSort(
            Type type,
            HashSet<Type> visited,
            HashSet<Type> recursionStack,
            List<Type> result)
        {
            if (visited.Contains(type))
            {
                return true;
            }

            List<Type> tail = null;
            int splitIndex = result.FindIndex(type.IsSubclassOf);
            if (splitIndex >= 0)
            {
                tail = result.Skip(splitIndex).ToList();
                result.RemoveRange(splitIndex, result.Count - splitIndex);
            }

            if (!recursionStack.Add(type))
            {
                return false;
            }

            foreach (Type dependency in GetDependencies(type))
            {
                if (!TopologicalSort(dependency, visited, recursionStack, result))
                {
                    return false;
                }
            }

            if (tail != null)
            {
                result.AddRange(tail);
            }
            else
            {
                result.Add(type);
            }

            recursionStack.Remove(type);
            visited.Add(type);
            return true;
        }

        private static List<Type> GetDependencies(Type type)
        {
            if (ComponentDependencies.TryGetValue(type, out List<Type> dependencies))
            {
                return dependencies;
            }

            dependencies = new List<Type>();
            foreach (RequireComponent requirement in type.GetCustomAttributes<RequireComponent>(true))
            {
                dependencies.Add(requirement.m_Type0);
                dependencies.Add(requirement.m_Type1);
                dependencies.Add(requirement.m_Type2);
            }

            dependencies = dependencies.Where(dependency => dependency != null).Distinct().ToList();
            ComponentDependencies[type] = dependencies;
            return dependencies;
        }
    }
}
