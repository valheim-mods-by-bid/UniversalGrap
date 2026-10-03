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

        [HarmonyTranspiler]
        // Run early so the semantic matcher normally sees vanilla-like IL. The
        // injection is deliberately small and leaves Object.Instantiate untouched.
        [HarmonyPriority(Priority.First)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int selectionIndex = FindAttachSelectionNullCheck(code);
            if (selectionIndex < 0)
            {
#if DEBUG
                Debug.LogWarning(
                    "[UniversalGrasp] VisEquipment.AttachItem no longer has the expected selection null-check; "
                    + "leaving the original method unmodified.");
#endif
                return code;
            }

#if DEBUG
            // This is a confidence check only. Another mod may legitimately replace
            // the Instantiate call while leaving the selection hook usable.
            if (!HasUniqueAttachInstantiationFromSelectedSource(code, selectionIndex))
            {
                Debug.LogWarning(
                    "[UniversalGrasp] VisEquipment.AttachItem instantiation differs from vanilla; "
                    + "applying the independent selection hook anyway.");
            }
#endif

            InjectAttachSelection(code, selectionIndex);
            return code;
        }

        [HarmonyPostfix]
        private static void CorrectInvalidHandOffset(
            GameObject __result,
            Transform joint,
            VisualCloneCleaner.PreparedSourceScope __state)
        {
            try
            {
                if (__result != null
                    && joint != null
                    && IsHandJoint(joint)
                    && __result.transform.localPosition.sqrMagnitude > 100f)
                {
                    __result.transform.localPosition = Vector3.zero;
                }
            }
            finally
            {
                VisualCloneCleaner.ReleaseScope(__state);
            }
        }

        [HarmonyPrefix]
        private static void BeginPreparedSourceScope(
            out VisualCloneCleaner.PreparedSourceScope __state)
        {
            __state = VisualCloneCleaner.BeginScope();
        }

        [HarmonyFinalizer]
        private static Exception RestoreInstantiationState(
            Exception __exception,
            VisualCloneCleaner.PreparedSourceScope __state)
        {
            VisualCloneCleaner.ReleaseScope(__state);
            return __exception;
        }

        private static int FindAttachSelectionNullCheck(IReadOnlyList<CodeInstruction> code)
        {
            int match = -1;
            for (int index = 0; index < code.Count; index++)
            {
                if (!IsLoadLocal(code[index], 1))
                {
                    continue;
                }

                int loadNullIndex = NextMeaningfulInstruction(code, index + 1);
                int equalityIndex = NextMeaningfulInstruction(code, loadNullIndex + 1);
                int branchIndex = NextMeaningfulInstruction(code, equalityIndex + 1);
                int nullResultIndex = NextMeaningfulInstruction(code, branchIndex + 1);
                int returnIndex = NextMeaningfulInstruction(code, nullResultIndex + 1);

                if (loadNullIndex < 0
                    || equalityIndex < 0
                    || branchIndex < 0
                    || nullResultIndex < 0
                    || returnIndex < 0
                    || code[loadNullIndex].opcode != OpCodes.Ldnull
                    || !code[equalityIndex].Calls(ObjectEqualityMethod)
                    || code[branchIndex].opcode.FlowControl != FlowControl.Cond_Branch
                    || code[nullResultIndex].opcode != OpCodes.Ldnull
                    || code[returnIndex].opcode != OpCodes.Ret)
                {
                    continue;
                }

                if (match >= 0)
                {
                    return -1;
                }

                match = index;
            }

            return match;
        }

#if DEBUG
        private static bool HasUniqueAttachInstantiationFromSelectedSource(
            IReadOnlyList<CodeInstruction> code,
            int searchAfterIndex)
        {
            int matches = 0;

            for (int index = searchAfterIndex + 1; index < code.Count; index++)
            {
                if (!IsGameObjectInstantiate(code[index]))
                {
                    continue;
                }

                int candidateSourceIndex = PreviousMeaningfulInstruction(code, index - 1);
                int resultStoreIndex = NextMeaningfulInstruction(code, index + 1);
                if (candidateSourceIndex < 0
                    || resultStoreIndex < 0
                    || !IsLoadLocal(code[candidateSourceIndex], 1)
                    || !IsStoreLocal(code[resultStoreIndex]))
                {
                    continue;
                }

                matches++;
                if (matches > 1)
                {
                    return false;
                }
            }

            return matches == 1;
        }
#endif

        private static void InjectAttachSelection(List<CodeInstruction> code, int selectionIndex)
        {
            var firstInjectedInstruction = new CodeInstruction(OpCodes.Ldloc_0);
            MoveLabelsAndBlocks(code[selectionIndex], firstInjectedInstruction);
            code.InsertRange(selectionIndex, new[]
            {
                firstInjectedInstruction,
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Ldarg_3),
                new CodeInstruction(OpCodes.Ldarg_S, (byte)5),
                new CodeInstruction(OpCodes.Call, ChooseAttachSourceMethod),
                new CodeInstruction(OpCodes.Stloc_1)
            });
        }

        private static int NextMeaningfulInstruction(
            IReadOnlyList<CodeInstruction> code,
            int startIndex)
        {
            for (int index = Math.Max(0, startIndex); index < code.Count; index++)
            {
                if (code[index].opcode != OpCodes.Nop)
                {
                    return index;
                }
            }

            return -1;
        }

#if DEBUG
        private static int PreviousMeaningfulInstruction(
            IReadOnlyList<CodeInstruction> code,
            int startIndex)
        {
            for (int index = Math.Min(startIndex, code.Count - 1); index >= 0; index--)
            {
                if (code[index].opcode != OpCodes.Nop)
                {
                    return index;
                }
            }

            return -1;
        }
#endif

        private static bool IsLoadLocal(CodeInstruction instruction, int localIndex)
        {
            if (localIndex == 0 && instruction.opcode == OpCodes.Ldloc_0) return true;
            if (localIndex == 1 && instruction.opcode == OpCodes.Ldloc_1) return true;
            if (localIndex == 2 && instruction.opcode == OpCodes.Ldloc_2) return true;
            if (localIndex == 3 && instruction.opcode == OpCodes.Ldloc_3) return true;

            if (instruction.opcode != OpCodes.Ldloc
                && instruction.opcode != OpCodes.Ldloc_S)
            {
                return false;
            }

            if (instruction.operand is LocalBuilder localBuilder)
            {
                return localBuilder.LocalIndex == localIndex;
            }

            if (instruction.operand is LocalVariableInfo localVariable)
            {
                return localVariable.LocalIndex == localIndex;
            }

            return instruction.operand is int integerIndex && integerIndex == localIndex
                || instruction.operand is byte byteIndex && byteIndex == localIndex;
        }

#if DEBUG
        private static bool IsStoreLocal(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Stloc
                || instruction.opcode == OpCodes.Stloc_S
                || instruction.opcode == OpCodes.Stloc_0
                || instruction.opcode == OpCodes.Stloc_1
                || instruction.opcode == OpCodes.Stloc_2
                || instruction.opcode == OpCodes.Stloc_3;
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
#endif

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
                return vanillaSource;
            }

            if (IsWeapon(itemPrefab) && vanillaSource != null)
            {
                return vanillaSource;
            }

            GameObject selectedSource = AttachSourceSelector.Find(itemPrefab) ?? vanillaSource;
            if (selectedSource == null)
            {
                return null;
            }

            if (VisualCloneCleaner.TryPrepareSource(
                selectedSource,
                out GameObject preparedSource,
                out string error))
            {
                return preparedSource;
            }

#if DEBUG
            Debug.LogWarning(
                $"[UniversalGrasp] Could not prepare a safe visual source for "
                + $"{itemPrefab.name}: {error}");
#endif
            return selectedSource;
        }

        private static bool IsWeapon(GameObject itemPrefab)
        {
            ItemDrop itemDrop = itemPrefab.GetComponent<ItemDrop>();
            return itemDrop != null
                && itemDrop.m_itemData != null
                && itemDrop.m_itemData.IsWeapon();
        }

        private static bool IsHandJoint(Transform joint)
        {
            return joint.name == "LeftHand_Attach" || joint.name == "RightHand_Attach";
        }

    }
}
