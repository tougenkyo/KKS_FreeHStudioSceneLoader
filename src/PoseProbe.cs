// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
//
// Diagnostics shared with the Studio-side probe (_dev/tools/StudioPoseProbe): writes the same numbers in Studio and
// in H, so the reproduction can be compared with the real plugins. Positions are relative to each character.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Studio;
using UnityEngine;

namespace KKS_FreeHStudioSceneLoader
{
    internal static class PoseProbe
    {
        private static readonly string[] Bones =
        {
            "cf_j_hips", "cf_j_spine01", "cf_j_spine02", "cf_j_spine03", "cf_j_neck", "cf_j_head",
            "cf_d_shoulder_L", "cf_j_shoulder_L", "cf_j_arm00_L", "cf_j_forearm01_L", "cf_j_hand_L",
            "cf_d_shoulder_R", "cf_j_shoulder_R", "cf_j_arm00_R", "cf_j_forearm01_R", "cf_j_hand_R",
            // Twist and helper bones of the arms (skinning)
            "cf_d_arm01_L", "cf_s_arm01_L", "cf_d_forearm02_L", "cf_s_forearm02_L", "cf_d_wrist_L", "cf_s_wrist_L", "cf_d_hand_L", "cf_s_hand_L",
            "cf_d_arm01_R", "cf_s_arm01_R", "cf_d_forearm02_R", "cf_s_forearm02_R", "cf_d_wrist_R", "cf_s_wrist_R", "cf_d_hand_R", "cf_s_hand_R",
        };

        // The IK input (after the animator, PVCopy and the KKPE edits), captured in the frame after each log and
        // written with the next one. PVCopy copies cf_pv_hand to cf_j_hand (world position) before IK.
        private static int _captureFrame = -1;
        private static readonly Dictionary<RootMotion.FinalIK.IKSolver, List<string>> PreIK = new Dictionary<RootMotion.FinalIK.IKSolver, List<string>>();

        internal static void InstallPatches(HarmonyLib.Harmony harmony)
        {
            var target = HarmonyLib.AccessTools.Method(typeof(RootMotion.FinalIK.IKSolverFullBody), "OnUpdate");
            harmony.Patch(target, prefix: new HarmonyLib.HarmonyMethod(typeof(PoseProbe), nameof(IKSolverFullBody_OnUpdate)));
        }

        private static void IKSolverFullBody_OnUpdate(RootMotion.FinalIK.IKSolverFullBody __instance)
        {
            if (Time.frameCount != _captureFrame || !(__instance is RootMotion.FinalIK.IKSolverFullBodyBiped solver)) return;
            try
            {
                var arm = solver.GetLimbMapping(RootMotion.FinalIK.FullBodyBipedChain.LeftArm);
                var chara = arm?.bone3 != null ? arm.bone3.GetComponentInParent<ChaControl>() : null;
                if (chara == null) return;
                var root = chara.transform;
                var lines = new List<string>();
                foreach (var side in new[] { "L", "R" })
                {
                    var hand = Find(root, "cf_j_hand_" + side);
                    var forearm = Find(root, "cf_j_forearm01_" + side);
                    var pv = Find(root, "cf_pv_hand_" + side);
                    if (hand == null || forearm == null) continue;
                    var line = $"preIK {side}: hand localPos {hand.localPosition.ToString("F4")} forearm local {forearm.localRotation.eulerAngles.ToString("F1")}" +
                               $" hand pos {root.InverseTransformPoint(hand.position).ToString("F3")}";
                    if (pv != null)
                        line += $" pv pos {root.InverseTransformPoint(pv.position).ToString("F3")} hand-pv {(hand.position - pv.position).magnitude * 1000f:F1}mm";
                    lines.Add(line);
                }
                var spine03 = Find(root, "cf_j_spine03");
                if (spine03 != null) lines.Add($"preIK spine03 local {spine03.localRotation.eulerAngles.ToString("F1")}");
                PreIK[__instance] = lines;
            }
            catch { }
        }

        internal static void Log(System.Action<string> logger, string where, float time, IEnumerable<OCIChar> characters)
        {
            // Also appended to a file of its own: Studio and the game overwrite the same output_log.txt
            var file = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "FreeHStudioProbe.log");
            void log(string line)
            {
                logger(line);
                try { System.IO.File.AppendAllText(file, line + System.Environment.NewLine); }
                catch { }
            }

            foreach (var chara in characters)
            {
                if (chara?.charInfo == null) continue;
                var root = chara.charInfo.transform;
                var name = chara.charInfo.chaFile?.parameter?.fullname?.Trim();
                var sb = new StringBuilder();
                sb.Append($"[probe {where} t={time:F2}] {name}");
                var ik = chara.finalIK;
                if (ik != null)
                {
                    var s = ik.solver;
                    sb.Append($" | IK on={ik.enabled} it={s.iterations} spineStiff={s.spineStiffness:F2} pullVert={s.pullBodyVertical:F2}" +
                              $" handL w={s.leftHandEffector.positionWeight:F2}/{s.leftHandEffector.rotationWeight:F2}" +
                              $" armL bend={s.leftArmChain.bendConstraint.weight:F2} FK={chara.oiCharInfo.enableFK}");
                }
                sb.Append($" fkCtrl={(chara.fkCtrl != null && chara.fkCtrl.enabled)} activeFK={string.Join("", chara.oiCharInfo.activeFK.Select(x => x ? "1" : "0").ToArray())}" +
                          $" activeIK={string.Join("", chara.oiCharInfo.activeIK.Select(x => x ? "1" : "0").ToArray())}");
                log(sb.ToString());
                // What the animator does, and PVCopy (Studio's copy of cf_pv_hand/leg to the hand/foot bones)
                try
                {
                    var anim = chara.charInfo.animBody;
                    if (anim != null)
                    {
                        var state = anim.layerCount > 0 ? anim.GetCurrentAnimatorStateInfo(0) : default(AnimatorStateInfo);
                        log($"[probe {where} t={time:F2}] {name} animator enabled={anim.enabled} active={anim.isActiveAndEnabled} init={anim.isInitialized} speed={anim.speed:F2}" +
                            $" ctrl={(anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "none")} cull={anim.cullingMode} update={anim.updateMode}" +
                            $" state={state.fullPathHash} t={state.normalizedTime:F3} anime={chara.oiCharInfo.animeInfo.group}/{chara.oiCharInfo.animeInfo.category}/{chara.oiCharInfo.animeInfo.no} animeSpeed={chara.oiCharInfo.animeSpeed:F2}");
                    }
                    var pvCopy = chara.preparation != null ? chara.preparation.pvCopy : null;
                    var enablePV = HarmonyLib.Traverse.Create(chara).Field("enablePV").GetValue<bool[]>();
                    log($"[probe {where} t={time:F2}] {name} pvCopy={(pvCopy != null ? string.Join("", Enumerable.Range(0, 4).Select(i => pvCopy[i] ? "1" : "0").ToArray()) : "none")}" +
                        $" component={(pvCopy != null ? pvCopy.enabled.ToString() : "-")} enablePV={(enablePV != null ? string.Join("", enablePV.Select(x => x ? "1" : "0").ToArray()) : "?")}");
                }
                catch (System.Exception ex)
                {
                    log($"[probe {where} t={time:F2}] {name} animator/pvCopy: {ex.Message}");
                }
                if (ik != null && PreIK.TryGetValue(ik.solver, out var preIK))
                    foreach (var line in preIK) log($"[probe {where} t={time:F2}] {name} {line}");
                foreach (var side in new[] { "L", "R" })
                {
                    var hand = Find(root, "cf_j_hand_" + side);
                    if (hand != null) log($"[probe {where} t={time:F2}] {name} hand {side} localPos {hand.localPosition.ToString("F4")}");
                }
                // Illusion's IK/frame correction components on the body (they move bones around IK)
                foreach (var correct in root.GetComponentsInChildren<Illusion.Component.Correct.BaseCorrect>(true))
                {
                    var on = correct.list.Where(i => i != null).Select(i => { try { return i.enabled ? "1" : "0"; } catch { return "?"; } });
                    log($"[probe {where} t={time:F2}] {name} correct {correct.GetType().Name} enabled={correct.enabled} items={string.Join("", on.ToArray())}");
                }
                foreach (var boneName in Bones)
                {
                    var bone = Find(root, boneName);
                    if (bone == null) continue;
                    log($"[probe {where} t={time:F2}] {name} bone {boneName} local {bone.localRotation.eulerAngles.ToString("F1")} pos {root.InverseTransformPoint(bone.position).ToString("F3")}");
                }
                foreach (var side in new[] { "L", "R" })
                {
                    var forearm = Find(root, "cf_j_forearm01_" + side);
                    var hand = Find(root, "cf_j_hand_" + side);
                    if (forearm != null && hand != null)
                        log($"[probe {where} t={time:F2}] {name} wrist {side} {Quaternion.Angle(forearm.rotation, hand.rotation):F1} deg local {(Quaternion.Inverse(forearm.rotation) * hand.rotation).eulerAngles.ToString("F1")}");
                }
                foreach (var target in chara.listIKTarget)
                {
                    var t = target.targetObject;
                    if (t == null) continue;
                    log($"[probe {where} t={time:F2}] {name} ikTarget {t.name} active={target.active} pos {root.InverseTransformPoint(t.position).ToString("F3")} rot {(Quaternion.Inverse(root.rotation) * t.rotation).eulerAngles.ToString("F1")}");
                }
            }
            PreIK.Clear();
            _captureFrame = Time.frameCount + 1;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
