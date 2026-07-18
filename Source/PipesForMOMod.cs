using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PipesForMO
{
    public class PipesForMOSettings : ModSettings
    {
        // Keyed by defName; an absent entry means enabled, so the default is all-on
        // and existing configs stay untouched.
        private Dictionary<string, bool> enabled = new Dictionary<string, bool>();

        public bool IsEnabled(string defName)
        {
            return !enabled.TryGetValue(defName, out bool value) || value;
        }

        public void SetEnabled(string defName, bool value)
        {
            enabled[defName] = value;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref enabled, "enabled", LookMode.Value, LookMode.Value);
            if (enabled == null)
            {
                enabled = new Dictionary<string, bool>();
            }
        }
    }

    public class PipesForMOMod : Mod
    {
        public static PipesForMOSettings Settings;

        public PipesForMOMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PipesForMOSettings>();
        }

        public override string SettingsCategory()
        {
            return "PipesForMO.Settings.Category".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("PipesForMO.Settings.RestartNote".Translate());
            listing.GapLine();
            List<PipeIntegration> integrations = PipeIntegrationSetup.Integrations;
            if (integrations.NullOrEmpty())
            {
                listing.Label("PipesForMO.Settings.NoIntegrations".Translate());
            }
            else
            {
                foreach (PipeIntegration integration in integrations)
                {
                    bool value = Settings.IsEnabled(integration.defName);
                    listing.CheckboxLabeled(integration.label, ref value);
                    Settings.SetEnabled(integration.defName, value);
                }
            }
            listing.End();
        }
    }
}
