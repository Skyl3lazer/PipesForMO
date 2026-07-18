using System;
using System.Collections.Generic;
using System.Reflection;
using DubsBadHygiene;
using RimWorld;
using UnityEngine;
using Verse;

namespace PipesForMO
{
    public class CompProperties_PipeHotWaterStorageFill : CompProperties
    {
        public int ticksPerCheck = 60;
        public float maxPullPerCheck = 30f;
        public bool onlyWhenLow = true;
        public bool respectRefillMode = true;
        public float pushedWaterTemperature = 25f;

        // When set, the pushed temperature tracks whether the net can supply hot water
        // (a boiler-fed hot water tank) instead of the fixed pushedWaterTemperature.
        public bool useNetHeatedStatus = false;
        public float warmWaterTemperature = 60f;
        public float coldWaterTemperature = 25f;
        public float heatCostPerWaterUnit = 0.00013f;
        public string inspectKeyPrefix = "PipesForMO.Kettle";

        public CompProperties_PipeHotWaterStorageFill()
        {
            compClass = typeof(CompPipeHotWaterStorageFill);
        }
    }

    public enum HotWaterFillReason
    {
        Pending,
        Ok,
        NotConnected,
        NoTowers,
        NoWater,
        StorageFull,
        NotLow,
        RefillDisabled,
        ResolveFailed,
    }

    public class CompPipeHotWaterStorageFill : ThingComp
    {
        private const string HotWaterStorageCompTypeName = "DBHforMedieval.CompHotWaterStorage";

        private CompPipe pipeComp;
        private ThingComp hotWaterStorageComp;
        private MethodInfo pushWaterMethod;
        private MethodInfo getSpaceLeftMethod;
        private MethodInfo getIsLowMethod;
        private FieldInfo refillTimesField;
        private bool resolveFailed;

        private int ticksSinceCheck;
        private int lastFillTick = -1;
        private float lastFillAmount;
        private bool lastHeated;
        private HotWaterFillReason lastReason = HotWaterFillReason.Pending;

        public CompProperties_PipeHotWaterStorageFill Props => (CompProperties_PipeHotWaterStorageFill)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            pipeComp = parent.GetComp<CompPipe>();
            ResolveStorageComp();
        }

        private void ResolveStorageComp()
        {
            if (resolveFailed || hotWaterStorageComp != null)
            {
                return;
            }
            List<ThingComp> comps = parent.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                ThingComp comp = comps[i];
                if (comp.GetType().FullName == HotWaterStorageCompTypeName)
                {
                    hotWaterStorageComp = comp;
                    break;
                }
            }
            if (hotWaterStorageComp == null)
            {
                resolveFailed = true;
                Log.WarningOnce(
                    $"[PipesForMO] {nameof(CompPipeHotWaterStorageFill)} on {parent?.def?.defName} could not find {HotWaterStorageCompTypeName}.",
                    GetHashCode());
                return;
            }
            Type type = hotWaterStorageComp.GetType();
            pushWaterMethod = type.GetMethod(
                "PushWater",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(float), typeof(ContaminationLevel), typeof(float) },
                modifiers: null);
            getSpaceLeftMethod = type.GetProperty("SpaceLeft", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
            getIsLowMethod = type.GetProperty("IsLow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
            refillTimesField = type.GetField("refillTimes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pushWaterMethod == null || getSpaceLeftMethod == null)
            {
                resolveFailed = true;
                Log.WarningOnce(
                    $"[PipesForMO] {nameof(CompPipeHotWaterStorageFill)} on {parent?.def?.defName} failed to bind hot water storage methods.",
                    GetHashCode() ^ 11777);
            }
        }

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

        private void AccumulateAndMaybeFill(int tickDelta)
        {
            if (pipeComp == null)
            {
                return;
            }
            ticksSinceCheck += tickDelta;
            if (ticksSinceCheck < Props.ticksPerCheck)
            {
                return;
            }
            ticksSinceCheck = 0;
            TryFillFromPipe();
        }

        private bool TryFillFromPipe()
        {
            ResolveStorageComp();
            if (resolveFailed || hotWaterStorageComp == null)
            {
                lastReason = HotWaterFillReason.ResolveFailed;
                return false;
            }
            PlumbingNet net = pipeComp.pipeNet;
            if (net == null)
            {
                lastReason = HotWaterFillReason.NotConnected;
                return false;
            }
            if (net.WaterTowers.NullOrEmpty())
            {
                lastReason = HotWaterFillReason.NoTowers;
                return false;
            }
            if (net.WaterStorage <= 0f)
            {
                lastReason = HotWaterFillReason.NoWater;
                return false;
            }
            if (Props.respectRefillMode && refillTimesField != null)
            {
                object refillMode = refillTimesField.GetValue(hotWaterStorageComp);
                if (refillMode != null && refillMode.ToString() == "Never")
                {
                    lastReason = HotWaterFillReason.RefillDisabled;
                    return false;
                }
            }
            if (Props.onlyWhenLow && getIsLowMethod != null)
            {
                object isLowValue = getIsLowMethod.Invoke(hotWaterStorageComp, null);
                if (isLowValue is bool isLow && !isLow)
                {
                    lastReason = HotWaterFillReason.NotLow;
                    return false;
                }
            }
            float spaceLeft = 0f;
            object spaceObj = getSpaceLeftMethod.Invoke(hotWaterStorageComp, null);
            if (spaceObj is float f)
            {
                spaceLeft = f;
            }
            else if (spaceObj is double d)
            {
                spaceLeft = (float)d;
            }
            if (spaceLeft <= 0f)
            {
                lastReason = HotWaterFillReason.StorageFull;
                return false;
            }
            float pull = Mathf.Min(spaceLeft, Mathf.Max(0.01f, Props.maxPullPerCheck));
            if (pull <= 0f)
            {
                lastReason = HotWaterFillReason.StorageFull;
                return false;
            }
            if (!net.PullWater(pull, out ContaminationLevel contam))
            {
                lastReason = HotWaterFillReason.NoWater;
                return false;
            }
            float temperature = ResolvePushTemperature(net, pull);
            pushWaterMethod.Invoke(hotWaterStorageComp, new object[] { pull, contam, temperature });
            lastFillTick = Find.TickManager.TicksGame;
            lastFillAmount = pull;
            lastReason = HotWaterFillReason.Ok;
            return true;
        }

        private float ResolvePushTemperature(PlumbingNet net, float pull)
        {
            if (!Props.useNetHeatedStatus)
            {
                return Props.pushedWaterTemperature;
            }
            lastHeated = net.PullHotWater(Props.heatCostPerWaterUnit * pull);
            return lastHeated ? Props.warmWaterTemperature : Props.coldWaterTemperature;
        }

        public override string CompInspectStringExtra()
        {
            if (resolveFailed || hotWaterStorageComp == null)
            {
                return null;
            }
            string prefix = Props.inspectKeyPrefix;
            switch (lastReason)
            {
                case HotWaterFillReason.NotConnected:
                    return (prefix + ".NotConnected").Translate();
                case HotWaterFillReason.NoTowers:
                    return (prefix + ".NoTowers").Translate();
                case HotWaterFillReason.NoWater:
                    return (prefix + ".NoWater").Translate();
                case HotWaterFillReason.StorageFull:
                    return (prefix + ".Full").Translate();
                case HotWaterFillReason.NotLow:
                    return (prefix + ".NotLow").Translate();
                case HotWaterFillReason.RefillDisabled:
                    return (prefix + ".RefillDisabled").Translate();
                case HotWaterFillReason.Pending:
                    return (prefix + ".Pending").Translate();
                default:
                {
                    if (Props.useNetHeatedStatus && lastFillTick >= 0)
                    {
                        return (prefix + (lastHeated ? ".FillingWarm" : ".FillingCold")).Translate();
                    }
                    if (!Prefs.DevMode || lastFillTick < 0)
                    {
                        return null;
                    }
                    int ago = Find.TickManager.TicksGame - lastFillTick;
                    return (prefix + ".LastFill").Translate(lastFillAmount.ToString("0.#"), ago.ToStringTicksToPeriod());
                }
            }
        }
    }
}
