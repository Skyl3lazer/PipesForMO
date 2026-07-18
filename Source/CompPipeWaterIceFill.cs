using System.Collections.Generic;
using System.Text;
using DubsBadHygiene;
using ProcessorFramework;
using RimWorld;
using UnityEngine;
using Verse;

namespace PipesForMO
{
    public class CompProperties_PipeWaterIceFill : CompProperties, IPipeIntegrationProps
    {
        public ThingDef ingredientDef;
        public ProcessDef processDef;
        public float waterPerIngredient = 5f;
        public int ticksPerCheck = 250;
        public int maxFillPerCheck = 5;
        public bool requireFreezing = true;
        public float freezeThreshold = 0f;

        public CompProperties_PipeWaterIceFill()
        {
            compClass = typeof(CompPipeWaterIceFill);
        }
    }

    public enum FillSkipReason
    {
        Pending,
        Ok,
        TooWarm,
        NotConnected,
        NoTowers,
        NoWater,
        ProcessorFull,
        ResolveFailed,
    }

    public class CompPipeWaterIceFill : ThingComp
    {
        private CompPipe pipeComp;
        private CompProcessor processorComp;
        private bool resolveFailed;

        private FillSkipReason lastReason = FillSkipReason.Pending;
        private int lastFillTick = -1;
        private int lastFillCount;

        public CompProperties_PipeWaterIceFill Props => (CompProperties_PipeWaterIceFill)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            pipeComp = parent.GetComp<CompPipe>();
            processorComp = parent.GetComp<CompProcessor>();
            if (Props.ingredientDef == null || Props.processDef == null)
            {
                resolveFailed = true;
                Log.WarningOnce(
                    $"[PipesForMO] {nameof(CompPipeWaterIceFill)} on {parent?.def?.defName} is missing ingredientDef or processDef.",
                    GetHashCode());
            }
        }

        private int ticksSinceCheck;

        public override void CompTick()
        {
            base.CompTick();
            AccumulateAndMaybeFill(1);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            AccumulateAndMaybeFill(250);
        }

        public override void CompTickLong()
        {
            base.CompTickLong();
            AccumulateAndMaybeFill(2000);
        }

        private void AccumulateAndMaybeFill(int ticksDelta)
        {
            if (pipeComp == null || processorComp == null)
            {
                return;
            }
            ticksSinceCheck += ticksDelta;
            if (ticksSinceCheck < Props.ticksPerCheck)
            {
                return;
            }
            ticksSinceCheck = 0;
            TryFillFromPipe(force: false);
        }

        private bool TryFillFromPipe(bool force)
        {
            if (resolveFailed)
            {
                lastReason = FillSkipReason.ResolveFailed;
                return false;
            }
            if (!force && Props.requireFreezing && parent.AmbientTemperature >= Props.freezeThreshold)
            {
                lastReason = FillSkipReason.TooWarm;
                return false;
            }
            PlumbingNet net = pipeComp.pipeNet;
            if (net == null)
            {
                lastReason = FillSkipReason.NotConnected;
                return false;
            }
            if (net.WaterTowers.NullOrEmpty())
            {
                lastReason = FillSkipReason.NoTowers;
                return false;
            }
            if (net.WaterStorage <= 0f)
            {
                lastReason = FillSkipReason.NoWater;
                return false;
            }
            int spaceLeft = processorComp.SpaceLeftFor(Props.processDef);
            if (spaceLeft <= 0)
            {
                lastReason = FillSkipReason.ProcessorFull;
                return false;
            }
            int unitsToMake = Mathf.Min(spaceLeft, Mathf.Max(1, Props.maxFillPerCheck));
            float waterPer = Mathf.Max(0.0001f, Props.waterPerIngredient);
            float waterNeeded = unitsToMake * waterPer;
            while (unitsToMake > 0 && waterNeeded > net.WaterStorage)
            {
                unitsToMake--;
                waterNeeded = unitsToMake * waterPer;
            }
            if (unitsToMake <= 0)
            {
                lastReason = FillSkipReason.NoWater;
                return false;
            }
            if (!net.PullWater(waterNeeded, out _))
            {
                lastReason = FillSkipReason.NoWater;
                return false;
            }
            Thing ingredient = ThingMaker.MakeThing(Props.ingredientDef);
            ingredient.stackCount = unitsToMake;
            processorComp.AddIngredient(ingredient, Props.processDef);
            lastReason = FillSkipReason.Ok;
            lastFillTick = Find.TickManager.TicksGame;
            lastFillCount = unitsToMake;
            return true;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "PipesForMO.Dev.ForceFillLabel".Translate(),
                    defaultDesc = "PipesForMO.Dev.ForceFillDesc".Translate(),
                    icon = TexCommand.DesirePower,
                    action = () => TryFillFromPipe(force: true),
                };
            }
        }

        public override string CompInspectStringExtra()
        {
            if (resolveFailed || pipeComp == null || processorComp == null)
            {
                return null;
            }
            PlumbingNet net = pipeComp.pipeNet;
            StringBuilder sb = new StringBuilder();
            sb.Append("PipesForMO.PipeWaterFill".Translate());
            sb.Append(": ");
            switch (lastReason)
            {
                case FillSkipReason.NotConnected:
                    sb.Append("PipesForMO.NotConnected".Translate());
                    break;
                case FillSkipReason.NoTowers:
                    sb.Append("PipesForMO.NoTowers".Translate());
                    break;
                case FillSkipReason.NoWater:
                    sb.Append("PipesForMO.NoWater".Translate());
                    break;
                case FillSkipReason.ProcessorFull:
                    sb.Append("PipesForMO.ProcessorFull".Translate());
                    break;
                case FillSkipReason.TooWarm:
                    sb.Append("PipesForMO.TooWarm".Translate(parent.AmbientTemperature.ToStringTemperature("F0")));
                    break;
                case FillSkipReason.Pending:
                    sb.Append("PipesForMO.Pending".Translate());
                    if (net != null && !net.WaterTowers.NullOrEmpty())
                    {
                        sb.Append(" - ");
                        sb.Append("PipesForMO.WaterAvailable".Translate(net.WaterStorage.ToString("F0")));
                    }
                    break;
                default:
                    if (net == null)
                    {
                        sb.Append("PipesForMO.NotConnected".Translate());
                    }
                    else
                    {
                        sb.Append("PipesForMO.WaterAvailable".Translate(net.WaterStorage.ToString("F0")));
                    }
                    break;
            }
            if (Prefs.DevMode && lastFillTick >= 0)
            {
                int ago = Find.TickManager.TicksGame - lastFillTick;
                sb.Append(" (");
                sb.Append("PipesForMO.LastFill".Translate(lastFillCount, ago.ToStringTicksToPeriod()));
                sb.Append(")");
            }
            return sb.ToString();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastFillTick, "PipesForMO_lastFillTick", -1);
            Scribe_Values.Look(ref lastFillCount, "PipesForMO_lastFillCount", 0);
        }
    }
}
