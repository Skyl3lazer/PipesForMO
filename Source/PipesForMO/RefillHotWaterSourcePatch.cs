using System;
using System.Reflection;
using DubsBadHygiene;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PipesForMO
{
    // DBH for Medieval's refill job reads a source carrying a CompPipe as net water at 25 C. Our kettle
    // patch gives the boiler pot one, which hides the pot's own heated water from every haul.
    [StaticConstructorOnStartup]
    public static class RefillHotWaterSourcePatch
    {
        private const string DriverTypeName = "DBHforMedieval.JodDriver_RefillHotWater";
        // Compiler-generated name of the takeWater finish action, where the source temperature is decided.
        private const string TakeWaterFinishAction = "<MakeNewToils>b__12_0";
        private const float LimitPerOnce = 30f;

        // False once DBH for Medieval renames the compiler-generated step this patch targets.
        public static bool Applied { get; private set; }

        private static MethodInfo pullWater;
        private static MethodInfo getSpaceLeft;
        private static FieldInfo requestField;
        private static FieldInfo contamField;
        private static FieldInfo temperatureField;

        static RefillHotWaterSourcePatch()
        {
            Type driver = AccessTools.TypeByName(DriverTypeName);
            if (driver == null)
            {
                return;
            }
            Type storage = AccessTools.TypeByName(CompPipeHotWaterStorageFill.HotWaterStorageCompTypeName);
            MethodInfo target = AccessTools.Method(driver, TakeWaterFinishAction);
            if (storage == null || target == null)
            {
                Warn("could not find the refill job's water-source step");
                return;
            }
            pullWater = AccessTools.Method(storage, "PullWater", new[]
            {
                typeof(float),
                typeof(ContaminationLevel).MakeByRefType(),
                typeof(float).MakeByRefType(),
            });
            getSpaceLeft = AccessTools.PropertyGetter(storage, "SpaceLeft");
            requestField = AccessTools.Field(driver, "request");
            contamField = AccessTools.Field(driver, "contam");
            temperatureField = AccessTools.Field(driver, "temperature");
            if (pullWater == null || getSpaceLeft == null || requestField == null || contamField == null || temperatureField == null)
            {
                Warn("could not bind the refill job's water-source fields");
                return;
            }
            new Harmony("Skyl3lazer.PipesForMO").Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(RefillHotWaterSourcePatch), nameof(Prefix))));
            Applied = true;
        }

        private static void Warn(string what)
        {
            Log.Warning($"[PipesForMO] {what}. Hauling from a piped boiler pot will draw cold water. DBH for Medieval may have changed.");
        }

        // A source that holds its own hot water is read from directly, whether or not it is also plumbed.
        private static bool Prefix(JobDriver __instance)
        {
            Job job = __instance?.job;
            if (job == null)
            {
                return true;
            }
            ThingComp source = CompPipeHotWaterStorageFill.FindStorageComp(job.targetB.Thing as ThingWithComps);
            ThingComp destination = CompPipeHotWaterStorageFill.FindStorageComp(job.targetA.Thing as ThingWithComps);
            if (source == null || destination == null)
            {
                return true;
            }
            float request = Mathf.Min((float)getSpaceLeft.Invoke(destination, null), LimitPerOnce);
            object[] args = { request, null, null };
            requestField.SetValue(__instance, pullWater.Invoke(source, args));
            contamField.SetValue(__instance, args[1]);
            temperatureField.SetValue(__instance, args[2]);
            return false;
        }
    }
}
