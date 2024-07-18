using System;
using GenericModConfigMenu;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Framework.ModLoading.Rewriters.StardewValley_1_6;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Audio;
using StardewValley.Locations;

namespace CustomElevatorSound
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {

        private ModConfig Config;

        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            //wiki said to include this from Patches...
            Patches.Initialize(this.Monitor);

            //this gets GMCM set up if it's installed
            this.Helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

            //read the config to get the name of the file the player has chosen (or use default)
            this.Config = this.Helper.ReadConfig<ModConfig>();
            string audioFileName = GetFileNameFromConfig(this.Config.ElevatorFileName);

            //define our custom cue
            CueDefinition customCueDefinition = new CueDefinition();
            customCueDefinition.name = "CustomElevator";

            //initialize the audio for the cue
            SoundEffect audio;

            //get the file for the audio
            string audioFilePath = Path.Combine(this.Helper.DirectoryPath, "assets", audioFileName);

            //set the audio to a stream of the file
            using (var stream = new System.IO.FileStream(audioFilePath, System.IO.FileMode.Open))
            {
                audio = SoundEffect.FromStream(stream);
            }

            if (audio == null)
                this.Monitor.Log("Invalid audio file type or location \"{audioFileName}\"", LogLevel.Error);

            //set the cue to the audio and add it to the game's soundbank
            customCueDefinition.SetSound(audio, Game1.audioEngine.GetCategoryIndex("Sound"), false);
            Game1.soundBank.AddCue(customCueDefinition);

            

            
            //patch PlayLocal with our prefix. Or put differently: dragons, but they are friendly dragons
            var harmony = new Harmony(this.ModManifest.UniqueID);

            harmony.Patch(
                original: AccessTools.Method(typeof(SoundsHelper), nameof(SoundsHelper.PlayLocal)),
                prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.PlayLocal_Prefix))
            );

        }

        private string GetFileNameFromConfig(string configChoice)
        {
            switch (configChoice)//TODO: these cases need to be keys of translations
            {
                case "GMCM Default":
                    {
                        return "DefaultModded.wav";
                    }
                case "GMCM Octave Lower":
                    {
                        return "OctaveLower.wav";
                    }
                case "GMCM Silent":
                    {
                        return "Silent.wav";
                    }
                case "GMCM Custom":
                    {
                        return "PlayerCreated.wav";
                    }
                default:
                    {
                        return "DefaultModded.wav";
                    }

            }
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // get Generic Mod Config Menu's API (if it's installed)
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
                return;

            // register mod
            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(this.Config)
            );

            // note that changing sounds take effect only after restarting
            configMenu.AddParagraph(
                mod: this.ModManifest,
                text: () => this.Helper.Translation.Get("GMCM Remark")
            );
            
            // add choice for sounds
            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("GMCM Elevator Sound"),
                getValue: () => this.Config.ElevatorFileName,
                setValue: value => this.Config.ElevatorFileName = value,
                allowedValues: new string[] { "GMCM Default", "GMCM Octave Lower", "GMCM Silent", "GMCM Custom" },
                formatAllowedValue: value => this.Helper.Translation.Get(value)
            );
        }

    }
}
