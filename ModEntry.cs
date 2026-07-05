using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using CustomSlimeCreator.Core;
using CustomSlimeCreator.UI;

[assembly: MelonInfo(typeof(CustomSlimeCreator.ModEntry), "Custom Slime Maker", "1.0.0", "AlkaPrime")]
[assembly: MelonGame("MonomiPark", "SlimeRancher2")]

namespace CustomSlimeCreator
{
    public class ModEntry : MelonMod
    {
        private static HarmonyLib.Harmony _harmony;

        public override void OnInitializeMelon()
        {
            _harmony = HarmonyInstance;
            LoggerInstance.Msg("Custom Slime Maker loaded — press F2 in a save to open the editor.");
            try
            {
                _harmony.Patch(
                    AccessTools.Method(typeof(SlimeEat), "CalculateAllEats"),
                    prefix: new HarmonyMethod(typeof(ModEntry), nameof(PrefixCalculateAllEats)),
                    finalizer: new HarmonyMethod(typeof(ModEntry), nameof(FinalizeCalculateAllEats)),
                    postfix: new HarmonyMethod(typeof(ModEntry), nameof(PostfixCalculateAllEats))
                );
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Warning("Patch failed: " + ex.Message);
            }

            // Patch the game's largo RESOLVERS so custom×custom and custom×vanilla fusions are created on demand.
            TryPatchFusion();
            // Patch the eat-gates so a custom-involved slime WILL eat the other's plort (and fuse) on contact.
            TryPatchEatGates();
        }

        private static void TryPatchFusion()
        {
            if (_harmony == null) return;
            // Patch the two largo RESOLVERS. When the game asks "is there a largo for these bases/plorts?" and the
            // answer is none but a CUSTOM slime is involved, we create + register the fusion on demand and return it.
            var targets = new (string name, string postfix)[]
            {
                ("GetLargoByBaseSlimes", nameof(PostfixLargoByBaseSlimes)),
                ("GetLargoByPlorts",     nameof(PostfixLargoByPlorts)),
            };
            foreach (var (name, postfix) in targets)
            {
                try
                {
                    var mi = AccessTools.Method(typeof(SlimeDefinitions), name);
                    if (mi == null) { MelonLogger.Warning($"[CustomSlimeCreator] SlimeDefinitions.{name} not found — fusion may not trigger."); continue; }
                    _harmony.Patch(mi, postfix: new HarmonyMethod(typeof(ModEntry), postfix));
                }
                catch (System.Exception ex) { MelonLogger.Warning($"[CustomSlimeMaker] patch {name}: {ex.Message}"); }
            }
        }

        /// <summary>If no largo exists for these two base slimes and one is custom, build the fusion on demand.</summary>
        private static void PostfixLargoByBaseSlimes(ref SlimeDefinition __result, SlimeDefinition __0, SlimeDefinition __1)
        {
            if (__result != null) return;
            var f = SlimeEngine.TryGetOrCreateFusion(__0, __1);
            if (f != null) __result = f;
        }

        /// <summary>Same, resolved by the two plorts being combined (maps each plort back to its slime).</summary>
        private static void PostfixLargoByPlorts(ref SlimeDefinition __result, IdentifiableType __0, IdentifiableType __1)
        {
            if (__result != null) return;
            var f = SlimeEngine.TryGetOrCreateFusionByPlorts(__0, __1);
            if (f != null) __result = f;
        }

        private static void TryPatchEatGates()
        {
            if (_harmony == null) return;
            var idType = new System.Type[] { typeof(IdentifiableType) };
            // (methodName, argTypes-or-null, postfix)
            var gates = new (string name, System.Type[] args, string postfix)[]
            {
                ("WillNowEat",     null,   nameof(PostfixEatGate)),
                ("IsHungryFor",    null,   nameof(PostfixEatGate)),
                ("DoesEat",        idType, nameof(PostfixEatGate)),  // the IdentifiableType overload
                ("GetEatMapById",  null,   nameof(PostfixGetEatMapById)),
            };
            foreach (var (name, args, postfix) in gates)
            {
                try
                {
                    var mi = args != null ? AccessTools.Method(typeof(SlimeEat), name, args)
                                          : AccessTools.Method(typeof(SlimeEat), name);
                    if (mi == null) { MelonLogger.Warning($"[CustomSlimeCreator] SlimeEat.{name} not found — fusion-eat may not trigger."); continue; }
                    _harmony.Patch(mi, postfix: new HarmonyMethod(typeof(ModEntry), postfix));
                }
                catch (System.Exception ex) { MelonLogger.Warning($"[CustomSlimeMaker] patch {name}: {ex.Message}"); }
            }

            // Intercept the transform: when a custom-involved slime is about to become one of OUR fusion largos, we
            // do the transform ourselves (next frame) via the proven spawn path — the native transform aborts on our
            // largos because they lack the vanilla largo appearance-set machinery.
            try
            {
                var mi = AccessTools.Method(typeof(SlimeEat), "EatAndTransform");
                if (mi != null) _harmony.Patch(mi, prefix: new HarmonyMethod(typeof(ModEntry), nameof(PrefixEatAndTransform)));
            }
            catch (System.Exception ex) { MelonLogger.Warning($"[CustomSlimeMaker] patch EatAndTransform: {ex.Message}"); }
        }

        /// <summary>
        /// Intercepts the largo transform. If a CUSTOM slime is involved, we transform the actor IN PLACE into our
        /// fusion largo (recomputed from the actual plort eaten) and skip the native path — the native transform
        /// turns a custom slime into the inherited vanilla largo (e.g. PinkBatty) or fails outright on our largos.
        /// Pure-vanilla transforms fall through to the game.
        /// __0 = the food GameObject being eaten.
        /// </summary>
        private static bool PrefixEatAndTransform(SlimeEat __instance, GameObject __0)
        {
            try { if (SlimeEngine.TryInPlaceFusion(__instance, __0)) return false; }
            catch { }
            return true;
        }

        /// <summary>
        /// Makes a custom-involved slime willing to eat a plort it can fuse with. On the first contact we create
        /// the fusion largo + wire the EatMap entry, then let the game's own transform logic take over.
        /// Shared by WillNowEat / IsHungryFor / DoesEat.
        /// </summary>
        private static void PostfixEatGate(SlimeEat __instance, IdentifiableType __0, ref bool __result)
        {
            if (__result) return;
            try { if (SlimeEngine.EnsureFusionEatable(__instance, __0)) __result = true; } catch { }
        }

        /// <summary>Supplies the fusion EatMap entry to the transform code if the native lookup came back empty.</summary>
        private static void PostfixGetEatMapById(SlimeEat __instance, IdentifiableType __0,
            ref Il2CppSystem.Collections.Generic.List<SlimeDiet.EatMapEntry> __result)
        {
            try
            {
                if (__result != null && __result.Count > 0) return;
                var entry = SlimeEngine.FusionEntryFor(__instance, __0);
                if (entry == null) return;
                var list = __result ?? new Il2CppSystem.Collections.Generic.List<SlimeDiet.EatMapEntry>();
                list.Add(entry);
                __result = list;
            }
            catch { }
        }

        private static int _eatWarned;
        private static int _eatLogged;
        private static float _nextEatLog;
        private static System.Exception FinalizeCalculateAllEats(System.Exception __exception)
        {
            if (__exception != null && _eatWarned < 5)
            {
                _eatWarned++;
                MelonLogger.Warning("[CustomSlimeCreator] CalculateAllEats error: " + __exception);
            }
            return null;
        }

        private static bool PrefixCalculateAllEats(SlimeEat __instance)
        {
            return true; // let CalculateAllEats run — it sets up internal state the slime needs
        }

        private static void PostfixCalculateAllEats(SlimeEat __instance)
        {
            // (No-op: the diet is set up correctly at build time; the old empty-EatMap recovery shared base-slime
            //  entries by reference, which risked corrupting vanilla slimes, so it's removed.)
        }

        public override void OnUpdate()
        {
            if (F2Down()) EditorUI.Toggle();
            EditorUI.Tick();
            SlimeEngine.Tick();
            SlimeEngine.AutoLoadSavedOnce();
            SlimeEngine.ProcessFusionQueue(); // run queued fusion transforms (next-frame, safe)
        }

        public override void OnGUI() => EditorUI.Draw();

        private static bool F2Down()
        {
            try { var kb = Keyboard.current; if (kb != null) return kb[Key.F2].wasPressedThisFrame; } catch { }
            try { return Input.GetKeyDown(KeyCode.F2); } catch { }
            return false;
        }
    }
}
