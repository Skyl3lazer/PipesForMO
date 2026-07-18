using System.Collections.Generic;
using System.Linq;
using DubsBadHygiene;
using Verse;

namespace PipesForMO
{
    // Marker: a building this mod plumbs. Drives settings discovery and the strip pass.
    public interface IPipeIntegrationProps
    {
    }

    public class PipeIntegration
    {
        public string defName;
        public string label;
    }

    [StaticConstructorOnStartup]
    public static class PipeIntegrationSetup
    {
        public static readonly List<PipeIntegration> Integrations = new List<PipeIntegration>();

        static PipeIntegrationSetup()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!IsIntegration(def))
                {
                    continue;
                }
                Integrations.Add(new PipeIntegration
                {
                    defName = def.defName,
                    label = def.label.NullOrEmpty() ? def.defName : def.label.CapitalizeFirst(),
                });
                if (PipesForMOMod.Settings != null && !PipesForMOMod.Settings.IsEnabled(def.defName))
                {
                    StripPipeComps(def);
                }
            }
            Integrations.SortBy(i => i.label);
        }

        public static bool IsIntegration(ThingDef def)
        {
            return def != null && !def.comps.NullOrEmpty() && def.comps.Any(c => c is IPipeIntegrationProps);
        }

        // Leaves native comps (e.g. the bath's own hot water storage) so off is exactly vanilla.
        public static void StripPipeComps(ThingDef def)
        {
            def.comps.RemoveAll(c =>
                c is IPipeIntegrationProps ||
                c is CompProperties_Pipe ||
                c is CompProperties_WaterTrader);
        }
    }
}
