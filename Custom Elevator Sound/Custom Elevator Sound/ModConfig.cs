using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace CustomElevatorSound
{
    /// <summary>Define the config.</summary>
    public sealed class ModConfig
    {
        public string ElevatorFileName { get; set; } = "Default Modded";
    }
}