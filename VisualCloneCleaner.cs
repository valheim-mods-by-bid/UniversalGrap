using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniversalGrasp
{
    /// <summary>
    /// Creates an inactive visual-only source and removes components in dependency
    /// order. Dependency sorting is adapted from Jotunn's RenderManager (MIT license).
    /// </summary>
    internal static class VisualCloneCleaner
    {
        private static readonly Dictionary<Type, List<Type>> ComponentDependencies =
            new Dictionary<Type, List<Type>>();

        [ThreadStatic]
        private static PreparedSourceScope currentScope;

        internal static PreparedSourceScope BeginScope()
        {
            var scope = new PreparedSourceScope(currentScope);
            currentScope = scope;
            return scope;
        }

        internal static void ReleaseScope(PreparedSourceScope scope)
        {
            if (scope == null)
            {
                return;
            }

            scope.Release();
            if (!ReferenceEquals(currentScope, scope))
            {
                return;
            }

            currentScope = scope.Parent;
            while (currentScope != null && currentScope.IsReleased)
            {
                currentScope = currentScope.Parent;
            }
        }

        internal static bool TryPrepareSource(
            GameObject source,
            out GameObject result,
            out string error)
        {
            result = null;
            error = null;
            PreparedSourceScope scope = currentScope;
            if (scope == null || scope.IsReleased)
            {
                error = "No active AttachItem prepared-source scope.";
                return false;
            }

            GameObject temporaryParent = new GameObject("UniversalGrasp_VisualSource");
            temporaryParent.SetActive(false);

            try
            {
                Vector3 sourceLocalScale = source.transform.localScale;

                result = InstantiateWithoutZNetViewInitialization(
                    source,
                    temporaryParent.transform);

                if (!RemoveComponentsRecursively(result.transform, out error))
                {
                    result = null;
                    return false;
                }

                // This object becomes local 1 in AttachItem. Preserve the original
                // name because vanilla uses it to detect attach_skin semantics.
                result.name = source.name;
                result.transform.localPosition = Vector3.zero;
                result.transform.localRotation = Quaternion.identity;
                result.transform.localScale = sourceLocalScale;
                result.SetActive(false);
                result.transform.SetParent(null, false);
                scope.SetPreparedSource(result);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                result = null;
                return false;
            }
            finally
            {
                Object.DestroyImmediate(temporaryParent);
            }
        }

        private static GameObject InstantiateWithoutZNetViewInitialization(
            GameObject source,
            Transform parent)
        {
            bool previousForceDisableInit = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;
            try
            {
                return Object.Instantiate(source, parent);
            }
            finally
            {
                ZNetView.m_forceDisableInit = previousForceDisableInit;
            }
        }

        internal sealed class PreparedSourceScope
        {
            private GameObject preparedSource;

            internal PreparedSourceScope(PreparedSourceScope parent)
            {
                Parent = parent;
            }

            internal PreparedSourceScope Parent { get; }
            internal bool IsReleased { get; private set; }

            internal void SetPreparedSource(GameObject source)
            {
                if (IsReleased)
                {
                    throw new InvalidOperationException("Cannot prepare a source in a released scope.");
                }

                if (preparedSource != null)
                {
                    Object.DestroyImmediate(preparedSource);
                }

                preparedSource = source;
            }

            internal void Release()
            {
                if (IsReleased)
                {
                    return;
                }

                IsReleased = true;
                if (preparedSource != null)
                {
                    Object.DestroyImmediate(preparedSource);
                    preparedSource = null;
                }
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
