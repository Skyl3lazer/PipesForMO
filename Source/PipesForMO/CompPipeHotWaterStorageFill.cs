using System;
using System.Collections.Generic;
using System.Reflection;
using DubsBadHygiene;
using RimWorld;
using UnityEngine;
using Verse;

namespace PipesForMO
{
    public class CompProperties_PipeHotWaterStorageFill : CompProperties, IPipeIntegrationProps
    {
        public int ticksPerCheck = 60;
        public float maxPullPerCheck = 30f;
        public bool onlyWhenLow = true;
        public bool respectRefillMode = true;
        public float pushedWaterTemperature = 25f;

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
        NoHotWater,
        ResolveFailed,
    }

    public class CompPipeHotWaterStorageFill : ThingComp
    {
        internal const string HotWaterStorageCompTypeName = "DBHforMedieval.CompHotWaterStorage";

        private CompPipe pipeComp;
        private ThingComp hotWaterStorageComp;
        private MethodInfo pushWaterMethod;
        private MethodInfo pullWaterMethod;
        private MethodInfo getSpaceLeftMethod;
        private MethodInfo getIsLowMethod;
        private MethodInfo getIsColdMethod;
        private FieldInfo refillTimesField;
        private FieldInfo refillTempField;
        private List<string> ignoredSourceDefs;
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
            hotWaterStorageComp = FindStorageComp(parent);
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
            pullWaterMethod = type.GetMethod(
                "PullWater",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(float), typeof(ContaminationLevel).MakeByRefType(), typeof(float).MakeByRefType() },
                modifiers: null);
            getSpaceLeftMethod = type.GetProperty("SpaceLeft", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
            getIsLowMethod = type.GetProperty("IsLow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
            getIsColdMethod = type.GetProperty("IsCold", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
            refillTimesField = type.GetField("refillTimes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            refillTempField = type.GetField("refillTemp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            ignoredSourceDefs = ReadIgnoredSourceDefs(type);
            if (pushWaterMethod == null || getSpaceLeftMethod == null)
            {
                resolveFailed = true;
                Log.WarningOnce(
                    $"[PipesForMO] {nameof(CompPipeHotWaterStorageFill)} on {parent?.def?.defName} failed to bind hot water storage methods.",
                    GetHashCode() ^ 11777);
            }
        }

        internal static ThingComp FindStorageComp(ThingWithComps thing)
        {
            if (thing == null)
            {
                return null;
            }
            List<ThingComp> comps = thing.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i].GetType().FullName == HotWaterStorageCompTypeName)
                {
                    return comps[i];
                }
            }
            return null;
        }

        // The building's own list of vessels it refuses to be filled from. Stops one bath siphoning another.
        private List<string> ReadIgnoredSourceDefs(Type storageType)
        {
            object storageProps = storageType
                .GetProperty("Props", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(hotWaterStorageComp);
            return storageProps?.GetType()
                .GetField("ignoreDefs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(storageProps) as List<string>;
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
            if (Props.respectRefillMode && ReadModeName(refillTimesField) == "Never")
            {
                lastReason = HotWaterFillReason.RefillDisabled;
                return false;
            }
            if (Props.onlyWhenLow && getIsLowMethod != null)
            {
                if (getIsLowMethod.Invoke(hotWaterStorageComp, null) is bool isLow && !isLow)
                {
                    lastReason = HotWaterFillReason.NotLow;
                    return false;
                }
            }
            float spaceLeft = ReadSpaceLeft();
            if (spaceLeft <= 0f)
            {
                lastReason = HotWaterFillReason.StorageFull;
                return false;
            }
            float pull = Mathf.Min(spaceLeft, Mathf.Max(0.01f, Props.maxPullPerCheck));
            ThingComp hotSource = Props.useNetHeatedStatus ? FindHotSourceOnNet(net) : null;
            if (hotSource != null && TryFillFromVessel(hotSource, pull))
            {
                return true;
            }
            return TryFillFromNet(net, pull);
        }

        // The automated form of the hand-haul the game already models. The water carries whatever
        // temperature that vessel actually holds.
        private bool TryFillFromVessel(ThingComp source, float pull)
        {
            object[] args = { pull, null, null };
            if (!(pullWaterMethod.Invoke(source, args) is float moved) || moved <= 0f)
            {
                return false;
            }
            pushWaterMethod.Invoke(hotWaterStorageComp, new object[] { moved, args[1], args[2] });
            lastHeated = !StoredWaterIsCold();
            lastFillTick = Find.TickManager.TicksGame;
            lastFillAmount = moved;
            lastReason = HotWaterFillReason.Ok;
            return true;
        }

        private bool TryFillFromNet(PlumbingNet net, float pull)
        {
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
            float temperature;
            if (!Props.useNetHeatedStatus)
            {
                temperature = Props.pushedWaterTemperature;
            }
            else if (net.PullHotWater(Props.heatCostPerWaterUnit * pull))
            {
                temperature = Props.warmWaterTemperature;
                lastHeated = true;
            }
            else if (ReadModeName(refillTempField) != "Warm")
            {
                temperature = Props.coldWaterTemperature;
                lastHeated = false;
            }
            else
            {
                // A full vessel is one the game's own refill work giver skips. Leaving this one low
                // keeps a pawn free to haul hot water to it.
                lastReason = HotWaterFillReason.NoHotWater;
                return false;
            }
            if (!net.PullWater(pull, out ContaminationLevel contam))
            {
                lastReason = HotWaterFillReason.NoWater;
                return false;
            }
            pushWaterMethod.Invoke(hotWaterStorageComp, new object[] { pull, contam, temperature });
            lastFillTick = Find.TickManager.TicksGame;
            lastFillAmount = pull;
            lastReason = HotWaterFillReason.Ok;
            return true;
        }

        private ThingComp FindHotSourceOnNet(PlumbingNet net)
        {
            if (pullWaterMethod == null || getIsColdMethod == null)
            {
                return null;
            }
            foreach (ThingWithComps piped in net.PipedThings)
            {
                if (piped == parent || ignoredSourceDefs.NotNullAndContains(piped.def.defName))
                {
                    continue;
                }
                ThingComp source = FindStorageComp(piped);
                if (source == null)
                {
                    continue;
                }
                if (getIsColdMethod.Invoke(source, null) is bool cold && !cold)
                {
                    return source;
                }
            }
            return null;
        }

        private bool StoredWaterIsCold()
        {
            return getIsColdMethod?.Invoke(hotWaterStorageComp, null) is bool cold && cold;
        }

        private float ReadSpaceLeft()
        {
            object value = getSpaceLeftMethod.Invoke(hotWaterStorageComp, null);
            if (value is float f)
            {
                return f;
            }
            return value is double d ? (float)d : 0f;
        }

        private string ReadModeName(FieldInfo field)
        {
            return field?.GetValue(hotWaterStorageComp)?.ToString();
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
                case HotWaterFillReason.NoHotWater:
                    return (prefix + ".NoHotWater").Translate();
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
