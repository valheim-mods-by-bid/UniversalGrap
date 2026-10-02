using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniversalGrasp.Patches
{
    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.AttachItem), typeof(int), typeof(int), typeof(Transform), typeof(bool), typeof(bool), typeof(int))]
    internal static class VisEquipmentAttachItemPatch
    {
        private static readonly MethodInfo ObjectEqualityMethod = AccessTools.Method(
            typeof(Object),
            "op_Equality",
            new[] { typeof(Object), typeof(Object) });

        private static readonly MethodInfo ChooseAttachSourceMethod = AccessTools.Method(
            typeof(VisEquipmentAttachItemPatch),
            nameof(ChooseAttachSource));

        private static readonly MethodInfo InstantiateVisualMethod = AccessTools.Method(
            typeof(VisEquipmentAttachItemPatch),
            nameof(InstantiateVisual));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int selectionIndex = FindAttachSelectionNullCheck(code);

            if (selectionIndex < 0)
            {
                Debug.LogWarning("[UniversalGrasp] VisEquipment.AttachItem changed; leaving the original method unmodified.");
                return code;
            }

            int instantiateIndex = selectionIndex + 7;
            if (!IsGameObjectInstantiate(code[instantiateIndex]))
            {
                Debug.LogWarning("[UniversalGrasp] VisEquipment.AttachItem instantiate call changed; leaving the original method unmodified.");
                return code;
            }

            // The original stack already contains the selected source. Add the item prefab,
            // joint and back-attach flag and replace Object.Instantiate(source) with our
            // visual instantiation helper.
            var loadItemPrefab = new CodeInstruction(OpCodes.Ldloc_0);
            MoveLabelsAndBlocks(code[instantiateIndex], loadItemPrefab);
            code[instantiateIndex].opcode = OpCodes.Call;
            code[instantiateIndex].operand = InstantiateVisualMethod;
            code.Insert(instantiateIndex, loadItemPrefab);
            code.Insert(instantiateIndex + 1, new CodeInstruction(OpCodes.Ldarg_3));
            code.Insert(instantiateIndex + 2, new CodeInstruction(OpCodes.Ldarg_S, (byte)5));

            // Give UniversalGrasp a chance to replace an unsuitable vanilla attach
            // source before the original method performs its null check.
            var chooseStart = new CodeInstruction(OpCodes.Ldloc_0);
            MoveLabelsAndBlocks(code[selectionIndex], chooseStart);
            code.InsertRange(selectionIndex, new[]
            {
                chooseStart,
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Ldarg_3),
                new CodeInstruction(OpCodes.Ldarg_S, (byte)5),
                new CodeInstruction(OpCodes.Call, ChooseAttachSourceMethod),
                new CodeInstruction(OpCodes.Stloc_1)
            });

            int finalReturnIndex = code.FindLastIndex(instruction => instruction.opcode == OpCodes.Ret);
            if (finalReturnIndex >= 0)
            {
                code.InsertRange(finalReturnIndex, new[]
                {
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(
                        typeof(VisEquipmentAttachItemPatch), nameof(LogAttachResult)))
                });
            }

            UniversalGraspPlugin.Log?.LogDebug(
                "VisEquipment.AttachItem transpiler applied successfully (attach selection and visual instantiation hooks active).");

            return code;
        }

        [HarmonyPostfix]
        private static void Postfix(GameObject __result, int itemHash, Transform joint)
        {
            if (UniversalGraspPlugin.Log == null)
            {
                return;
            }

            string prefabName = ObjectDB.instance.GetItemPrefab(itemHash)?.name ?? "<null>";
            if (__result == null)
            {
                UniversalGraspPlugin.Log.LogDebug(
                    $"Attach result: prefab={prefabName}, joint={joint?.name ?? "<null>"}, result=<null>, valid=False.");
                return;
            }

            Renderer[] renderers = __result.GetComponentsInChildren<Renderer>(true);
            // A few consumable prefabs carry authoring offsets that are valid in their
            // world/pickup context but not when attached to a hand (Mead currently has
            // a Y offset close to -50). Do not disturb normal equip offsets; only repair
            // values that are clearly outside a plausible hand-local range.
            if (joint != null && IsHandJoint(joint) && __result.transform.localPosition.sqrMagnitude > 100f)
            {
                Vector3 invalidPosition = __result.transform.localPosition;
                __result.transform.localPosition = Vector3.zero;
                UniversalGraspPlugin.Log.LogDebug(
                    $"Corrected invalid hand visual offset: prefab={prefabName}, "
                    + $"joint={joint.name}, previousPosition={invalidPosition}, newPosition={Vector3.zero}.");
            }

            string rendererDetails = string.Join(", ", Array.ConvertAll(
                renderers,
                renderer => DescribeRenderer(renderer)));
            UniversalGraspPlugin.Log.LogDebug(
                $"Attach result: prefab={prefabName}, joint={joint?.name ?? "<null>"}, "
                + $"result={__result.name}, valid=True, parent={__result.transform.parent?.name ?? "<null>"}, "
                + $"localPosition={__result.transform.localPosition}, localScale={__result.transform.localScale}, "
                + $"renderers={renderers.Length}, details=[{rendererDetails}].");
        }

        private static void LogAttachResult(GameObject result)
        {
            if (UniversalGraspPlugin.Log == null)
            {
                return;
            }

            if (result == null)
            {
                UniversalGraspPlugin.Log.LogDebug("Attach result (injected): result=<null>, valid=False.");
                return;
            }

            Renderer[] renderers = result.GetComponentsInChildren<Renderer>(true);
            UniversalGraspPlugin.Log.LogDebug(
                $"Attach result (injected): result={result.name}, valid=True, "
                + $"parent={result.transform.parent?.name ?? "<null>"}, "
                + $"localPosition={result.transform.localPosition}, localScale={result.transform.localScale}, "
                + $"renderers={renderers.Length}.");
        }

        private static string DescribeRenderer(Renderer renderer)
        {
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            Material material = renderer.sharedMaterial;
            return $"{renderer.GetType().Name}@{renderer.gameObject.name}"
                + $"(active={renderer.gameObject.activeInHierarchy},enabled={renderer.enabled}"
                + $",mesh={mesh?.name ?? "<null>"},material={material?.name ?? "<null>"}"
                + $",bounds={renderer.bounds})";
        }

        private static int FindAttachSelectionNullCheck(IReadOnlyList<CodeInstruction> code)
        {
            for (int index = 0; index <= code.Count - 8; index++)
            {
                if (code[index].opcode == OpCodes.Ldloc_1
                    && code[index + 1].opcode == OpCodes.Ldnull
                    && code[index + 2].Calls(ObjectEqualityMethod)
                    && code[index + 3].opcode.FlowControl == FlowControl.Cond_Branch
                    && code[index + 4].opcode == OpCodes.Ldnull
                    && code[index + 5].opcode == OpCodes.Ret
                    && code[index + 6].opcode == OpCodes.Ldloc_1
                    && IsGameObjectInstantiate(code[index + 7]))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool IsGameObjectInstantiate(CodeInstruction instruction)
        {
            if (!(instruction.operand is MethodInfo method)
                || method.Name != nameof(Object.Instantiate)
                || !method.IsGenericMethod
                || method.GetParameters().Length != 1)
            {
                return false;
            }

            Type[] genericArguments = method.GetGenericArguments();
            return genericArguments.Length == 1 && genericArguments[0] == typeof(GameObject);
        }

        private static void MoveLabelsAndBlocks(CodeInstruction source, CodeInstruction destination)
        {
            destination.labels.AddRange(source.labels);
            source.labels.Clear();
            destination.blocks.AddRange(source.blocks);
            source.blocks.Clear();
        }

        private static GameObject ChooseAttachSource(
            GameObject itemPrefab,
            GameObject vanillaSource,
            Transform joint,
            bool backAttach)
        {
            if (itemPrefab == null
                || joint == null
                || backAttach
                || itemPrefab.name == "Lantern"
                || !IsHandJoint(joint))
            {
                return LogAttachSelection(itemPrefab, vanillaSource, joint, vanillaSource);
            }

            ItemDrop itemDrop = itemPrefab.GetComponent<ItemDrop>();
            bool isWeapon = itemDrop != null
                && itemDrop.m_itemData != null
                && itemDrop.m_itemData.IsWeapon();

            if (isWeapon && vanillaSource != null)
            {
                return LogAttachSelection(itemPrefab, vanillaSource, joint, vanillaSource);
            }

            GameObject selectedSource = UniversalGraspPlugin.GetAttachObject(itemPrefab) ?? vanillaSource;
            return LogAttachSelection(itemPrefab, vanillaSource, joint, selectedSource);
        }

        private static GameObject LogAttachSelection(
            GameObject itemPrefab,
            GameObject vanillaSource,
            Transform joint,
            GameObject selectedSource)
        {
            UniversalGraspPlugin.Log?.LogDebug(
                $"Attach selection: prefab={itemPrefab?.name ?? "<null>"}, joint={joint?.name ?? "<null>"}, "
                + $"vanillaAttach={vanillaSource?.name ?? "<null>"}, selectedAttach={selectedSource?.name ?? "<null>"}, "
                + $"valid={selectedSource != null}.");
            return selectedSource;
        }

        private static bool IsHandJoint(Transform joint)
        {
            return joint.name == "LeftHand_Attach" || joint.name == "RightHand_Attach";
        }

        private static GameObject InstantiateVisual(
            GameObject source,
            GameObject itemPrefab,
            Transform joint,
            bool backAttach)
        {
            bool isNonWeaponHandItem = !backAttach
                && IsHandJoint(joint)
                && !IsWeapon(itemPrefab);
            bool useVanillaInstantiation = !isNonWeaponHandItem
                && (IsVanillaAttachSource(source) || itemPrefab.name == "Lantern");
            UniversalGraspPlugin.Log?.LogDebug(
                $"Visual instantiation: prefab={itemPrefab.name}, source={source?.name ?? "<null>"}, "
                + $"joint={joint?.name ?? "<null>"}, validSource={source != null}, cleaner={!useVanillaInstantiation}.");

            if (useVanillaInstantiation)
            {
                GameObject vanillaClone = Object.Instantiate(source);
                UniversalGraspPlugin.Log?.LogDebug(
                    $"Visual instantiation result: prefab={itemPrefab.name}, clone={vanillaClone?.name ?? "<null>"}, valid={vanillaClone != null}.");
                return vanillaClone;
            }

            if (VisualCloneCleaner.TryCreate(source, out GameObject clone, out string error))
            {
                Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
                string rendererSummary = string.Join(", ", Array.ConvertAll(
                    renderers,
                    renderer => DescribeRenderer(renderer)));
                UniversalGraspPlugin.Log?.LogDebug(
                    $"Clean visual clone created: prefab={itemPrefab.name}, source={source.name}, clone={clone?.name ?? "<null>"}, valid={clone != null}.");
                UniversalGraspPlugin.Log?.LogDebug(
                    $"Clean visual clone renderers: prefab={itemPrefab.name}, count={renderers.Length}, [{rendererSummary}].");
                return clone;
            }

            Debug.LogWarning($"[UniversalGrasp] Could not create a safe visual clone of {itemPrefab.name}: {error}");
            return Object.Instantiate(source);
        }

        private static bool IsWeapon(GameObject itemPrefab)
        {
            ItemDrop itemDrop = itemPrefab.GetComponent<ItemDrop>();
            return itemDrop != null
                && itemDrop.m_itemData != null
                && itemDrop.m_itemData.IsWeapon();
        }

        private static bool IsVanillaAttachSource(GameObject source)
        {
            return source.name == "attach"
                || source.name == "attach_skin"
                || source.name == "attach_back";
        }
    }
}
