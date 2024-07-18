using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Reflection;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Audio;

namespace CustomElevatorSound
{
    /// <summary>The mod entry point.</summary>
    internal sealed class Patches
    {
        private static IMonitor Monitor;

        // call this method from your Entry class - so says the wiki
        internal static void Initialize(IMonitor monitor)
        {
            Monitor = monitor;
        }



        /*********
        ** Patches
        *********/

        /// <summary>Intercepts SoundsHelper.PlayLocal and checks if it's (almost certainly) an elevator. If so, it plays our custom sound instead.</summary>
        internal static bool PlayLocal_Prefix(string cueName, GameLocation location, Vector2? position, int? pitch, SoundContext context, out ICue cue)
        {

            cue = Game1.soundBank.GetCue(cueName);//never used, but assigning it anyway...

            try
            {
                //check that world is ready and player in the Mines (or similar) and cue is crystal and pitch is 0/null and then play assets/elevator.ogg instead
                if (Context.IsWorldReady)
                {



                    if (cueName.Equals("crystal") && (pitch == null || pitch == 0))
                    {

                        //the SkullCave check is in case the player has the Skull Cavern Elevator mod

                        string locationName = Game1.player.currentLocation.Name;
                        


                        if (locationName.Equals("Mine") || locationName.Contains("UndergroundMine") || locationName.Equals("SkullCave"))
                        {

                            //get the objects at the grab tile, the cursor location, and the tile below that (because of how singing stones can be activated)
                            Game1.currentLocation.objects.TryGetValue(Game1.player.GetGrabTile(), out var obj);
                            Game1.currentLocation.objects.TryGetValue(Game1.currentCursorTile, out var obj2);
                            Game1.currentLocation.objects.TryGetValue(Game1.currentCursorTile + new Vector2(0, 1), out var obj3);

                            bool grabbingSingingStone = false;

                            //check the grab tile
                            if (obj != null)
                            {
                                grabbingSingingStone = (obj.Name == "Singing Stone");
                            }

                            //check the cursor tile
                            if (obj2 != null && !grabbingSingingStone)
                            {
                                grabbingSingingStone = (obj2.Name == "Singing Stone");
                            }

                            //check the tile below the cursor tile (because activating singing stones is very weird)
                            if (obj3 != null && !grabbingSingingStone)
                            {
                                grabbingSingingStone = (obj3.Name == "Singing Stone");
                            }

                            if (grabbingSingingStone)
                            {
                                return true;//if it's a Singing Stone we want the original logic instead
                            }
                            else
                            {
                                //play new sound
                                Game1.sounds.PlayLocal("CustomElevator", location, position, 0, context, out ICue outCue);
                                

                                //return false to avoid original logic
                                return false;
                            }
                        }
                    }
                }

                return true;//these are not the sounds we're looking for so we let the original logic run by returning true
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed in {nameof(PlayLocal_Prefix)}:\n{ex}", LogLevel.Error);
                return true;//need this to run original logic for whatever situation got us here
            }



        }

    }
}