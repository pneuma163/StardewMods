using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace WhereTheSpreadingWeedsAre
{
    /// <summary>Define the config.</summary>
    public sealed class ModConfig
    {
        public bool ShowX { get; set; } = true;
        public bool NewObjectResets { get; set; } = true;
        public string CropImages { get; set; } = "Growing Plant";
        public bool ShowHUDDamageReport { get; set; } = true;
        public bool ShowInWorldOverlay { get; set; } = true;
    }
}