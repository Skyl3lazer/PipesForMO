using System.Collections.Generic;
using System.Text;
using DubsBadHygiene;
using ProcessorFramework;
using RimWorld;
using UnityEngine;
using Verse;

namespace PipesForMO
{
    public class CompProperties_PipeWaterIceFill : CompProperties
    {
        public string ingredientDef = "DankPyon_Waterskin";
        public string processDef = "DankPyon_IceBlockProcess";
        public float waterPerIngredient = 5f;
        public int ticksPerCheck = 250;
        public int maxFillPerCheck = 5;
        public bool requireFreezing = true;

        public CompProperties_PipeWaterIceFill()
        {
            compClass = typeof(CompPipeWaterIceFill);
        }
    }

    public class CompPipeWaterIceFill : ThingComp
    {
        private CompPipe pipeComp;
        private CompProcessor processorComp;
        private ThingDef cachedIngredientDef;
        private ProcessDef cachedProcessDef;
        private bool resolveFailed;

        public CompProperties_PipeWaterIceFill Props => (CompProperties_PipeWaterIceFill)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            pipeComp = parent.GetComp<CompPipe>();
            processorComp = parent.GetComp<CompProcessor>();
            ResolveDefs();
        }

        private void ResolveDefs()
        {
            if (resolveFailed || (cachedIngredientDef != null && cachedProcessDef != null))
            {
                return;
            }
            cachedIngredientDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props.ingredientDef);
            cachedProcessDef = DefDatabase<ProcessDef>.GetNamedSilentFail(Props.processDef);
            if (cachedIngredientDef == null || cachedProcessDef == null)
            {
                resolveFailed = true;
                Log.WarningOnce(
                    $"[PipesForMO] CompPipeWaterIceFill on {parent?.def?.defName} could not resolve "
                    + $"ingredientDef='{Props.ingredientDef}' or processDef='{Props.processDef}'. Disabling.",
                    GetHashCode());
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (resolveFailed || pipeComp == null || processorComp == null)
            {
                return;
            }
            if (!parent.IsHashIntervalTick(Props.ticksPerCheck))
            {
                return;
            }
            TryFillFromPipe();
        }

        private void TryFillFromPipe()
        {
            if (Props.requireFreezing && parent.AmbientTemperature >= 0f)
            {
                return;
            }
            ResolveDefs();
            if (resolveFailed)
            {
                return;
            }
            PlumbingNet net = pipeComp.pipeNet;
            if (net == null || net.WaterStorage <= 0f)
            {
                return;
            }
            int spaceLeft = processorComp.SpaceLeftFor(cachedProcessDef);
            if (spaceLeft <= 0)
            {
                return;
            }
            int unitsToMake = Mathf.Min(spaceLeft, Mathf.Max(1, Props.maxFillPerCheck));
            float waterNeeded = unitsToMake * Mathf.Max(0.0001f, Props.waterPerIngredient);
            while (unitsToMake > 0 && waterNeeded > net.WaterStorage)
            {
                unitsToMake--;
                waterNeeded = unitsToMake * Props.waterPerIngredient;
            }
            if (unitsToMake <= 0)
            {
                return;
            }
            if (!net.PullWater(waterNeeded, out _))
            {
                return;
            }
            Thing ingredient = ThingMaker.MakeThing(cachedIngredientDef);
            ingredient.stackCount = unitsToMake;
            processorComp.AddIngredient(ingredient, cachedProcessDef);
        }

        public override string CompInspectStringExtra()
        {
            if (resolveFailed || pipeComp == null)
            {
                return null;
            }
            PlumbingNet net = pipeComp.pipeNet;
            StringBuilder sb = new StringBuilder();
            sb.Append("PipesForMO.PipeWaterFill".Translate());
            sb.Append(": ");
            if (net == null)
            {
                sb.Append("PipesForMO.NotConnected".Translate());
            }
            else if (net.WaterTowers.NullOrEmpty())
            {
                sb.Append("PipesForMO.NoTowers".Translate());
            }
            else
            {
                sb.Append("PipesForMO.WaterAvailable".Translate(net.WaterStorage.ToString("F0")));
            }
            if (Props.requireFreezing && parent.Spawned && parent.AmbientTemperature >= 0f)
            {
                sb.Append(" (");
                sb.Append("PipesForMO.TooWarm".Translate());
                sb.Append(")");
            }
            return sb.ToString();
        }
    }
}
