using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using HarmonyLib;
using GenericModConfigMenu;
using System.Reflection;
using System.Reflection.Emit;
using StardewValley.TerrainFeatures;
using StardewValley.Mods;
using Microsoft.Xna.Framework.Graphics;
using StardewValley.Extensions;
using StardewValley.Objects;

namespace WhereTheSpreadingWeedsAre
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        private ModConfig Config;

        private static readonly string FARM_DAMAGE_TRANSLATION_KEY = "Farm Damage";
        private static string damageReportToDisplay = FARM_DAMAGE_TRANSLATION_KEY;
        private static string damageReportForConsole = FARM_DAMAGE_TRANSLATION_KEY;
        private static readonly string MODID = "pneuma163.WhereTheSpreadingWeedsAre";//could get this via this.ModManifest.UniqueID later but this is shorter and faster
        private static readonly string VERSION_STRING = "1.0.0";//this is the "earliest version our data works with" so if we change it, we need to remove older versions
        private static string currentDayOfSaveFile = "0";//only added to our data - not actually used in the current version
        
        private static float animationTransparency = 0f;
        private static string debrisIndicatorHelperForModData = "/HasSpreadingDebris";//special string to indicate whether a location has ever had damage
        private ModDataDictionary farmModDataDictionary = new ModDataDictionary();//used for the current player's current location

        //a dictionary populated at the start of the day to show today's destruction
        //key: location name followed by modData key that adds this dictionary's key
        //value: an internal class made of relevant data
        private Dictionary<string, SpreadingWeedsDestruction> DestructionForToday = new Dictionary<string, SpreadingWeedsDestruction>();
        private static bool doNotRender = false;
        private static Texture2D arrowPointer;



        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            //read the config
            this.Config = this.Helper.ReadConfig<ModConfig>();
            
            //event handlers
            helper.Events.GameLoop.GameLaunched += this.onGameLaunched;
            helper.Events.GameLoop.SaveLoaded += this.onSaveLoaded;
            helper.Events.Display.RenderedWorld += this.onRenderedWorld;
            helper.Events.GameLoop.DayEnding += this.onDayEnding;
            helper.Events.GameLoop.DayStarted += this.onDayStarted;
            helper.Events.Display.RenderedHud += this.onRenderedHud;
            helper.Events.Player.Warped += this.onWarped;

            helper.ConsoleCommands.Add("wtswa", "Prints mod data for farm damage to console.\n\nUsage:\nwtswa data - Prints data for mod.\nwtswa clear - Clears data for mod.\nwtswa report - Prints today's damage with coordinates.", this.HandleConsoleInput);

            //initiate translated heading for damage report
            damageReportToDisplay = this.Helper.Translation.Get(FARM_DAMAGE_TRANSLATION_KEY);

            arrowPointer = this.Helper.ModContent.Load<Texture2D>("assets/pointer.png");

            //harmony stuff to patch spawnWeedsAndStones with our transpiler
            var harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.Patch(
                original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.spawnWeedsAndStones)),
                transpiler: new HarmonyMethod(typeof(ModEntry), nameof(spawnWeedsAndStones_Transpiler))
            );
        }

        //each location where destruction happens will have some number of these each day
        internal class SpreadingWeedsDestruction
        {
            public int _x;
            public int _y;
            public StardewValley.Object _object;
            public bool _shouldDrawAsCrop = false;
            public int _cropPhase = 0;
            public Crop _cropToDraw;
            public bool _itIsJustDirt = false;
            public HoeDirt _tilledSoilToDraw;
        }


        private void onGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            // get Generic Mod Config Menu's API (if it's installed)
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
                return;//nothing else we need to do on launching the game for this mod

            // register mod
            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () => 
                {
                    this.Helper.WriteConfig(this.Config);
                    unloadDamageInfoForLocation(Game1.player.currentLocation);
                    loadDamageInfoForLocation(Game1.player.currentLocation, true);
                }
            );

            Translation CropPlant = this.Helper.Translation.Get("GMCM Crop Plant");
            Translation CropSeed = this.Helper.Translation.Get("GMCM Crop Seed");
            Translation CropHarvested = this.Helper.Translation.Get("GMCM Crop Harvested");

            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => this.Helper.Translation.Get("GMCM Visuals")
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("GMCM Crop Images"),
                getValue: () => this.Config.CropImages,
                setValue: value => this.Config.CropImages = value,
                allowedValues: new string[] { "Growing Plant", "Seed Packet", "Harvested Produce" },
                formatAllowedValue: value => this.Helper.Translation.Get(value)
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("GMCM Show In World Overlay"),
                tooltip: () => this.Helper.Translation.Get("GMCM Show In World Overlay Tooltip"),
                getValue: () => this.Config.ShowInWorldOverlay,
                setValue: value => this.Config.ShowInWorldOverlay = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("GMCM Show X"),
                tooltip: () => this.Helper.Translation.Get("GMCM Show X Tooltip"),
                getValue: () => this.Config.ShowX,
                setValue: value => this.Config.ShowX = value
            );

            configMenu.AddSectionTitle(
                mod: this.ModManifest,
                text: () => this.Helper.Translation.Get("GMCM Function")
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("GMCM New Object"),
                tooltip: () => this.Helper.Translation.Get("GMCM New Object Tooltip"),
                getValue: () => this.Config.NewObjectResets,
                setValue: value => this.Config.NewObjectResets = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.Helper.Translation.Get("GMCM Show HUD Damage Report"),
                tooltip: () => this.Helper.Translation.Get("GMCM Show HUD Damage Report Tooltip"),
                getValue: () => this.Config.ShowHUDDamageReport,
                setValue: value => this.Config.ShowHUDDamageReport = value
            );
        }

        //For handling console command "wtswa"
        private void HandleConsoleInput(string command, string[] args)
        {
            if (Context.IsWorldReady)
            {
                if (args.Length == 0)
                {
                    this.Monitor.Log($"Missing arguments for command {command}. Type \"help {command}\" for usage.", LogLevel.Warn);
                    return;
                }
                if (args[0] == "data")
                {
                    //prints data for this mod
                    this.Monitor.Log($"Printing data:", LogLevel.Debug);
                    this.Monitor.Log($"\n- ShowX: {this.Config.ShowX}", LogLevel.Debug);
                    this.Monitor.Log($"\n- NewObjectResets: {this.Config.NewObjectResets}", LogLevel.Debug);
                    this.Monitor.Log($"\n- CropImages: {this.Config.CropImages}", LogLevel.Debug);
                    this.Monitor.Log($"\n- ShowHUDDamageReport: {this.Config.ShowHUDDamageReport}", LogLevel.Debug);
                    this.Monitor.Log($"\n- ShowInWorldOverlay: {this.Config.ShowInWorldOverlay}", LogLevel.Debug);
                    this.Monitor.Log($"\n- zoom level: {Game1.options.zoomLevel}", LogLevel.Debug);
                    this.Monitor.Log($"\n- UI scale: {Game1.options.uiScale}", LogLevel.Debug);

                    foreach(GameLocation location in Game1.locations)
                    {
                        foreach(string s in location.modData.Keys)
                        {
                            if (s.StartsWith(MODID))
                            {
                                this.Monitor.Log($"\n{location.Name} | {s.Substring(MODID.Length + 1)} | {location.modData[s]}", LogLevel.Debug);
                            }
                        }
                    }
                }
                else if (args[0] == "clear")
                {
                    //clear data for this mod
                    foreach(GameLocation location in Game1.locations)
                    {
                        foreach(string s in location.modData.Keys)
                        {
                            if (s.StartsWith(MODID))
                            {
                                location.modData.Remove(s);
                            }
                        }
                    }
                    this.Monitor.Log($"Cleared all data for {MODID}", LogLevel.Debug);
                }
                else if (args[0] == "report")
                {
                    this.Monitor.Log($"{damageReportForConsole}", LogLevel.Debug);
                }
                else
                {
                    this.Monitor.Log($"Please supply an appropriate argument for the command {command}. Type \"help {command}\" for usage.", LogLevel.Warn);
                }
            }
            else
            {
                this.Monitor.Log("Please load a save to use this command.", LogLevel.Warn);
            }
        }

        private void onDayStarted(object? sender, DayStartedEventArgs e)
        {

            damageReportToDisplay = this.Helper.Translation.Get(FARM_DAMAGE_TRANSLATION_KEY);
            damageReportForConsole = damageReportToDisplay;

            foreach (GameLocation location in Game1.locations)
            {
                if (location.modData.ContainsKey(MODID + debrisIndicatorHelperForModData))
                {
                    loadDamageInfoForLocation(location);
                }
            }

            if (damageReportToDisplay != this.Helper.Translation.Get(FARM_DAMAGE_TRANSLATION_KEY) && this.Config.ShowHUDDamageReport)
            {
                Game1.addHUDMessage(HUDMessage.ForCornerTextbox(damageReportToDisplay));
            }

            return;
        }

        //covers case where a player changes crop config and then goes to another map with crops
        private void onWarped(object? sender, WarpedEventArgs e)
        {
            if (e.NewLocation.modData.ContainsKey(MODID + debrisIndicatorHelperForModData))
            {
                loadDamageInfoForLocation(e.NewLocation, true);
            }
        }


        //gets damage info from farm's modData and prepares text for HUD for Day Started
        private void loadDamageInfoForLocation(GameLocation farm, bool onlyLoadForDisplay = false)
        {
            //if save not loaded, return - i.e. we got here probably via GMCM save delegate but we're in the main menu
            if (!Context.IsWorldReady)
                return;

            ModDataDictionary farmModDataDictionary;
            farmModDataDictionary = farm.modData;

            string snippetOfDamageReport;
            int x_damage;
            int y_damage;
            string damaged_Object;//a Stardew Object or TerrainFeature as a string
            string damaged_Object_For_Report;//for the HUDMessage
            bool createdHeaderForHUD = false;

            foreach (string s in farmModDataDictionary.Keys)//example: key: "MODID/1.0.0/12/50/24", value: "Weeds/(BC)13" - weeds destroyed furnace on day 12 at (50, 24)
            {
                if (s.StartsWith(MODID))
                {
                    if (farmModDataDictionary.TryGetValue(s, out snippetOfDamageReport))
                    {

                        if (snippetOfDamageReport == "true")//just our indicator in the dictionary that this modData has data we care about - ignored in this method
                        {
                            continue;
                        }

                        

                        damaged_Object = snippetOfDamageReport.Substring(snippetOfDamageReport.Split('/')[0].Length + 1);//e.g. "(BC)13"
                        
                        if (!createdHeaderForHUD && !onlyLoadForDisplay)
                        {
                            damageReportToDisplay += "\n\n" + farm.DisplayName + this.Helper.Translation.Get("Colon_Punctuation") + "\n-----\n";
                            damageReportForConsole += "\n" + farm.DisplayName + this.Helper.Translation.Get("Colon_Punctuation");
                            createdHeaderForHUD = true;
                        }

                        //parse x, y from this key
                        int.TryParse(s.Split('/')[3], out x_damage);
                        int.TryParse(s.Split('/')[4], out y_damage);

                        SpreadingWeedsDestruction destruction = new SpreadingWeedsDestruction();
                        destruction._x = x_damage;
                        destruction._y = y_damage;

                        if (damaged_Object == "(D0)-1")
                        {
                            destruction._itIsJustDirt = true;
                            damaged_Object_For_Report = this.Helper.Translation.Get("Tilled Soil");
                            destruction._tilledSoilToDraw = new HoeDirt();
                        }
                        else
                        {
                            //the ? : check is because it's stored with our own item identifier thing (D#) to pass along more info
                            damaged_Object_For_Report = ItemRegistry.GetMetadata((damaged_Object[1] == 'D' ? "(O)" + damaged_Object.Substring(4) : damaged_Object)).GetParsedData().DisplayName;
                        }

                        if (damaged_Object[1] == 'O')
                        {
                            //it's an object
                            StardewValley.Object obj = (StardewValley.Object)ItemRegistry.Create(damaged_Object);
                            obj.Flipped = false;
                            destruction._object = obj;
                        }
                        else if (damaged_Object[1] == 'B' && damaged_Object[2] == 'C')
                        {
                            //it's a big craftable
                            StardewValley.Object obj = (StardewValley.Object)ItemRegistry.Create(damaged_Object);
                            obj.bigCraftable.Value = true;
                            obj.Flipped = false;
                            destruction._object = obj;
                        }
                        else if (damaged_Object[1] == 'D' && damaged_Object[4] != '-')//fake item data definition - game knows the object as an (O) - e.g. (D3)27 is (seed (O)27 at phase 3)
                        {
                            //it's a crop - "D" is for Dirt or seeD
                            int cropPhase = int.Parse(damaged_Object.Substring(2, 1));
                            damaged_Object = damaged_Object.Substring(4);//unqualify it (well, remove our fake item id) for actual game handling
                            if (Game1.cropData.ContainsKey(damaged_Object))
                            {
                                if (this.Config.CropImages == "Harvested Produce")//display harvested crop
                                {
                                    StardewValley.Object obj = (StardewValley.Object)ItemRegistry.Create(Game1.cropData[damaged_Object].HarvestItemId);
                                    destruction._object = obj;
                                }
                                if (this.Config.CropImages == "Seed Packet")//display seed packet
                                {
                                    StardewValley.Object obj = (StardewValley.Object)ItemRegistry.Create(damaged_Object);
                                    destruction._object = obj;
                                }
                                if (this.Config.CropImages == "Growing Plant")//display plant in current growth phase
                                {
                                    destruction._shouldDrawAsCrop = true;
                                    StardewValley.Object obj = (StardewValley.Object)ItemRegistry.Create(damaged_Object);
                                    destruction._object = obj;
                                    Crop cropToShow = new Crop(damaged_Object, x_damage, y_damage, farm);
                                    cropToShow.currentPhase.Set(cropPhase);
                                    destruction._cropToDraw = cropToShow;
                                }
                                
                            }
                            
                        }

                        DestructionForToday[farm.Name + "/" + s] = destruction;

                        if (!onlyLoadForDisplay)
                        {
                            damageReportToDisplay += damaged_Object_For_Report + ", ";
                            damageReportForConsole += damaged_Object_For_Report + " (" + x_damage.ToString() + ", " + y_damage.ToString() + "), ";
                        }
                    }
                    
                }
            }

            if (damageReportToDisplay != this.Helper.Translation.Get(FARM_DAMAGE_TRANSLATION_KEY) && !onlyLoadForDisplay)
            {
                if (damageReportToDisplay.Substring(damageReportToDisplay.Length - 2) == ", ")
                {
                    damageReportToDisplay = damageReportToDisplay.Substring(0, damageReportToDisplay.Length - 2);//cleans up the end of the string
                }
                if (damageReportForConsole.Substring(damageReportForConsole.Length - 2) == ", ")
                {
                    damageReportForConsole = damageReportForConsole.Substring(0, damageReportForConsole.Length - 2);//cleans up the end of the string
                }
            }
        }

        //for use in GMCM save delegate
        private void unloadDamageInfoForLocation(GameLocation farm)
        {
            //remove keys from dictionary of destruction
            foreach (string key in DestructionForToday.Keys)
            {
                if (key.StartsWith(farm.Name))
                {
                    DestructionForToday.Remove(key);
                }
            }
        }


        private void onRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            if (!this.Config.ShowInWorldOverlay && !this.Config.ShowX)
            {
                return;
            }

            if (doNotRender)
            {
                return;
            }

            //chue on SDV discord suggested the idea of some kind of overlay. I greatly expanded on this.
            farmModDataDictionary = Game1.player.currentLocation.modData;

            if (!farmModDataDictionary.ContainsKey(MODID + debrisIndicatorHelperForModData))
            {
                return;//we don't have anything to render at this location
            }

            int x_damage;
            int y_damage;

            animationTransparency += 0.02f;
            if (animationTransparency > 2)
                animationTransparency = 0f;

            int emoteIndex = 36 + (int)Math.Round(animationTransparency * 6)%4;

            float transparency = Math.Min(1.0f, 0.2f + 0.75f * ((animationTransparency > 1) ? 2f - animationTransparency : animationTransparency));

            try//just in case I haven't actually squashed all the data access bugs
            {
                foreach (string key in DestructionForToday.Keys)//example: key: "Farm/MODID/50/24", value: "class with x, y, Object, etc."
                {
                    if (!key.StartsWith(Game1.player.currentLocation.Name))
                    {
                        continue;//we ignore rendering keys for other locations
                    }

                    x_damage = DestructionForToday[key]._x;
                    y_damage = DestructionForToday[key]._y;

                    if (this.Config.ShowInWorldOverlay)
                    {
                        if (DestructionForToday[key]._shouldDrawAsCrop)
                        {
                            //draw as crop
                            if (DestructionForToday[key]._cropToDraw is not null && !DestructionForToday[key]._itIsJustDirt)
                            {
                                Rectangle rectangleOnTileSheet = DestructionForToday[key]._cropToDraw.getSourceRect(DestructionForToday[key]._cropPhase);
                                Texture2D cropTileSheetTexture = DestructionForToday[key]._cropToDraw.DrawnCropTexture;
                                Rectangle rectangleOfColoredPortion = rectangleOnTileSheet;
                                rectangleOfColoredPortion.X = (DestructionForToday[key]._cropToDraw.GetData().DaysInPhase.Count + 2) * 16;

                                //adapting color code from Crop.cs in game files
                                List<string> tintColors = DestructionForToday[key]._cropToDraw.GetData().TintColors;
                                Color? tryColor = Color.Transparent;
                                Color color = Color.Transparent;
                                if (tintColors != null && tintColors.Count > 0)
                                {
                                    tryColor = Utility.StringToColor(Utility.CreateRandom((double)x_damage * 1000.0, y_damage, Game1.dayOfMonth).ChooseFrom(tintColors));
                                }

                                if (tryColor != Color.Transparent)
                                {
                                    //draw colored part of crop
                                    color = tryColor.Value;
                                    e.SpriteBatch.Draw(cropTileSheetTexture, Game1.GlobalToLocal(Game1.viewport, new Vector2(x_damage * 64 + 32, (float)(y_damage * 64 - 64 + 32))), rectangleOfColoredPortion, color * transparency, 0f, new Vector2(8f, 8f), 4f, SpriteEffects.None, 1f);
                                }
                                
                                e.SpriteBatch.Draw(cropTileSheetTexture, Game1.GlobalToLocal(Game1.viewport, new Vector2(x_damage * 64 + 32 - 32, (float)(y_damage * 64 - 64 - 8 + 8))), rectangleOnTileSheet, Color.White * transparency, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
                                
                            }
                            else
                            {
                                this.Monitor.LogOnce("We shouldn't be here...", LogLevel.Warn);
                            }
                        }
                        else if (!DestructionForToday[key]._itIsJustDirt)//draw object, and if it's a crop draw it as a seed packet or harvested crop
                        {
                            DestructionForToday[key]._object.draw(e.SpriteBatch, x_damage, y_damage, transparency);
                        }
                        else if (DestructionForToday[key]._itIsJustDirt)
                        {
                            DestructionForToday[key]._tilledSoilToDraw.Tile = new Vector2(x_damage, y_damage);
                            DestructionForToday[key]._tilledSoilToDraw.loadSprite();
                            e.SpriteBatch.Draw(Game1.GetSeasonForLocation(Game1.player.currentLocation) == Season.Winter ? HoeDirt.snowTexture : HoeDirt.lightTexture, Game1.GlobalToLocal(Game1.viewport, new Vector2(x_damage * 64 + 32 - 32, (float)(y_damage * 64 - 64 - 8 + 8 + 64))), new Rectangle(0, 0, 16, 16), Color.White * transparency, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
                        }
                    }

                    double playerDistance = Math.Sqrt(Math.Pow(Game1.player.getStandingPosition().X - x_damage * 64 - 32, 2) + Math.Pow(Game1.player.getStandingPosition().Y - y_damage * 64 - 32, 2));

                    if (this.Config.ShowX && playerDistance > 144)
                    {
                        Vector2 emotePosition = Game1.GlobalToLocal(Game1.viewport, new Vector2(x_damage * 64 + 32 - 32, (float)(y_damage * 64 - 64 - 8 + 8)));
                        e.SpriteBatch.Draw(Game1.emoteSpriteSheet, emotePosition, new Rectangle(emoteIndex * 16 % Game1.emoteSpriteSheet.Width, emoteIndex * 16 / Game1.emoteSpriteSheet.Width * 16, 16, 16), Color.White * (Math.Min((float)playerDistance / 200f - 0.5f, 1f)), 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);

                    }

                    Vector2 tileLocation = new Vector2(x_damage, y_damage);

                    if (animationTransparency == 0f && this.Config.NewObjectResets)
                    {
                        if (Game1.currentLocation.objects.ContainsKey(tileLocation))
                        {
                            if (!Game1.currentLocation.objects[tileLocation].isDebrisOrForage())
                            {
                                //stop rendering overlay here, remove from DestructionForToday
                                DestructionForToday.Remove(key);
                            }
                        }
                        if (Game1.currentLocation.terrainFeatures.ContainsKey(tileLocation) || Game1.currentLocation.isTerrainFeatureAt(x_damage, y_damage))//the latter checks if it's passable so no good for checking paths or HoeDirt
                        {
                            DestructionForToday.Remove(key);
                        }
                    }
                }
            
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Failed in onRenderedWorld. Please report this error to the author of the mod.\n{ex}", LogLevel.Error);
                this.Monitor.Log($"Printing data:\n- ShowX: {this.Config.ShowX}\n- NewObjectResets: {this.Config.NewObjectResets}\n- CropImages: {this.Config.CropImages}\n- ShowHUDDamageReport: {this.Config.ShowHUDDamageReport}\n- ShowInWorldOverlay: {this.Config.ShowInWorldOverlay}", LogLevel.Trace);
                foreach(string s in farmModDataDictionary.Keys)
                {
                    if (s.StartsWith(MODID))
                    {
                        this.Monitor.Log($"\n{Game1.currentLocation.Name} : {s.Substring(MODID.Length + 1)} : {farmModDataDictionary[s]}", LogLevel.Trace);
                    }
                }
                doNotRender = true;
            }

        }


        private void onRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            //Button on the SDV discord suggested using the Tracker profession arrows. That code is buggy so I made my own.

            if (!this.Config.ShowX)
            {
                return;
            }

            if (doNotRender)
            {
                return;
            }

            farmModDataDictionary = Game1.player.currentLocation.modData;

            if (!farmModDataDictionary.ContainsKey(MODID + debrisIndicatorHelperForModData))
            {
                return;//we don't have anything to render at this location
            }

            int x_damage;
            int y_damage;

            try//just in case I haven't actually squashed all the data access bugs
            {
                foreach (string key in DestructionForToday.Keys)//example: key: "Farm/MODID/50/24", value: "class with x, y, Object, etc."
                {
                    if (!key.StartsWith(Game1.player.currentLocation.Name))
                    {
                        continue;//we ignore rendering keys for other locations
                    }

                    x_damage = DestructionForToday[key]._x;
                    y_damage = DestructionForToday[key]._y;

                    float arrowTransparency = 1f;
                    
                    Vector2 renderLocation = new Vector2(x_damage, y_damage) * 64f + new Vector2(32f, 32f);
                    Vector2 onScreenPosition2 = Vector2.Zero;

                    if (!Utility.isOnScreen(renderLocation, 32) && !Utility.isOnScreen(renderLocation - new Vector2(0f, 64f), 32))
                    {
                        Rectangle vpbounds = new Rectangle(Game1.uiViewport.X, Game1.uiViewport.Y, Game1.uiViewport.Width, Game1.uiViewport.Height);

                        if (renderLocation.X > vpbounds.Left + vpbounds.Width / Game1.options.zoomLevel * Game1.options.uiScale)
                        {
                            onScreenPosition2.X = vpbounds.Width - 2f * (float)arrowPointer.Height;
                        }
                        else if (renderLocation.X < vpbounds.Left)
                        {
                            onScreenPosition2.X = 2f * (float)arrowPointer.Height;
                        }
                        else
                        {
                            onScreenPosition2.X = (renderLocation.X - vpbounds.Left) * Game1.options.zoomLevel / Game1.options.uiScale;
                            if (onScreenPosition2.X > vpbounds.Width - 2f * (float)arrowPointer.Height)
                            {
                                onScreenPosition2.X = vpbounds.Width - 2f * (float)arrowPointer.Height;
                            }
                        }

                        if (renderLocation.Y > vpbounds.Top + vpbounds.Height / Game1.options.zoomLevel * Game1.options.uiScale)
                        {
                            onScreenPosition2.Y = vpbounds.Height - 2f * (float)arrowPointer.Height;
                        }
                        else if (renderLocation.Y < vpbounds.Top)
                        {
                            onScreenPosition2.Y = 2f * (float)arrowPointer.Height;
                        }
                        else
                        {
                            onScreenPosition2.Y = (renderLocation.Y - vpbounds.Top) * Game1.options.zoomLevel / Game1.options.uiScale;
                            if (onScreenPosition2.Y > vpbounds.Height - 2f * (float)arrowPointer.Height)
                            {
                                onScreenPosition2.Y = vpbounds.Height - 2f * (float)arrowPointer.Height;
                            }
                        }

                        float rotation2 = 0f;

                        if (onScreenPosition2.X == 2f * (float)arrowPointer.Height)
                        {
                            rotation2 = -(float)Math.PI / 2f;
                        }
                        else if (onScreenPosition2.X == vpbounds.Width - 2f * (float)arrowPointer.Height)
                        {
                            rotation2 = (float)Math.PI / 2f;
                        }
                        else if (onScreenPosition2.Y == vpbounds.Height - 2f * (float)arrowPointer.Height)
                        {
                            rotation2 = (float)Math.PI;
                        }

                        if (onScreenPosition2.X == 2f * (float)arrowPointer.Height && onScreenPosition2.Y == 2f * (float)arrowPointer.Height)
                        {
                            rotation2 = -(float)Math.PI / 4f;
                            arrowTransparency = 0.75f;
                        }
                        if (onScreenPosition2.X == 2f * (float)arrowPointer.Height && onScreenPosition2.Y == (vpbounds.Height - 2f * (float)arrowPointer.Height))
                        {
                            rotation2 = -3f * (float)Math.PI / 4f;
                            arrowTransparency = 0.75f;
                        }
                        if (onScreenPosition2.X == (vpbounds.Width - 2f * (float)arrowPointer.Height) && onScreenPosition2.Y == 2f * (float)arrowPointer.Height)
                        {
                            rotation2 = (float)Math.PI / 4f;
                            arrowTransparency = 0.75f;
                        }
                        if (onScreenPosition2.X == (vpbounds.Width - 2f * (float)arrowPointer.Height) && onScreenPosition2.Y == (float)(vpbounds.Height - 2f * (float)arrowPointer.Height))
                        {
                            rotation2 = 3f * (float)Math.PI / 4f;
                            arrowTransparency = 0.75f;
                        }
                        Rectangle srcRect = new Rectangle(0, 0, arrowPointer.Width, arrowPointer.Height);
                        float renderScale = 4f;

                        e.SpriteBatch.Draw(arrowPointer, onScreenPosition2, srcRect, Color.White * arrowTransparency, rotation2, new Vector2(arrowPointer.Width/2f, arrowPointer.Height/2f), renderScale, SpriteEffects.None, 1f);
                    }

                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Failed in onRenderedHud. Please report this error to the author of the mod.\n{ex}", LogLevel.Error);
                this.Monitor.Log($"Printing data:\n- ShowX: {this.Config.ShowX}\n- NewObjectResets: {this.Config.NewObjectResets}\n- CropImages: {this.Config.CropImages}\n- ShowHUDDamageReport: {this.Config.ShowHUDDamageReport}\n- ShowInWorldOverlay: {this.Config.ShowInWorldOverlay}", LogLevel.Trace);
                foreach(string s in farmModDataDictionary.Keys)
                {
                    if (s.StartsWith(MODID))
                    {
                        this.Monitor.Log($"\n{Game1.currentLocation.Name} : {s.Substring(MODID.Length + 1)} : {farmModDataDictionary[s]}", LogLevel.Trace);
                    }
                }
                doNotRender = true;
            }
        }


        private void resetDamageInfoForLocation(GameLocation location)
        {
            foreach (string s in location.modData.Keys)//example: key: "MODID/1.0.0/12/50/24", value: "Weeds/(BC)13"}
            {
                if (s.StartsWith(MODID) && location.modData[s] != "true")
                {
                    location.modData.Remove(s);
                }
            }
        }


        private void onDayEnding(object? sender, DayEndingEventArgs e)
        {
            doNotRender = false;
            
            if (damageReportToDisplay.Equals(this.Helper.Translation.Get(FARM_DAMAGE_TRANSLATION_KEY)))
            {
                return;
            }

            foreach (GameLocation location in Game1.locations)
            {
                resetDamageInfoForLocation(location);
            }

            DestructionForToday.Clear();

            return;
        }


        private void onSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
 
            DestructionForToday.Clear();

            return;
        }


        //this method is added to the spawn logic in the game via our tranpiler
        //it saves information about damage to the farms ("farm" includes any relevant GameLocation) into that farm's modData
        //location: the (x,y) tile location on the farm map where damage occurred
        //destroyed: if something got destroyed
        //obj: the object that got destroyed (if null, then a terrain feature got destroyed)
        //feat: the terrain feature that got destroyed (if null, then an object got destroyed)
        //deb: the type of debris (Weeds, Stone, Twig, etc.)
        //whichFarm: the GameLocation where this all happened
        public static void HandleNotifyingDestroyedObjectOrFeature(Vector2 location, string deb, GameLocation whichFarm)
        {

// The below code is heavily edited from spawnWeedsAndStones in GameLocations.cs
            bool destroyed = false;
            if (whichFarm.objects.TryGetValue(location, out var removedObj))
            {
                if (whichFarm.IsGreenRainingHere() || removedObj is Fence || removedObj is Chest || removedObj.QualifiedItemId == "(O)590" || removedObj.QualifiedItemId == "(BC)MushroomLog")
                {
                    return;
                }
                string text = removedObj.name;
                if (text != null && text.Length > 0 && removedObj.Category != -999)
                {
                    destroyed = true;
                }
            }
            if (whichFarm.terrainFeatures.TryGetValue(location, out var removedFeature))
            {
                try
                {
                    destroyed = removedFeature is HoeDirt || removedFeature is Flooring;
                }
                catch (Exception)
                {
                }
                if (!destroyed || whichFarm.IsGreenRainingHere())
                {
                    return;
                }
            }
// The above code is heavily edited from spawnWeedsAndStones in GameLocations.cs


            if (!destroyed)
            {
                return;
            }

            if (!whichFarm.modData.ContainsKey(MODID + debrisIndicatorHelperForModData))//if we found a new location where debris spreads
                whichFarm.modData[MODID + debrisIndicatorHelperForModData] = "true";//added to modData for this location

            string destroyedName = "";//for processing
            string storedDestroyedName = "";//for saved modData
            
            try
            {
                if (removedFeature is HoeDirt)
                {
                    if (((HoeDirt)removedFeature).crop is not null)
                    {
                        destroyedName = ((HoeDirt)removedFeature).crop.GetData().HarvestItemId;

                        bool isForageCrop = ItemRegistry.GetMetadata("(O)" + destroyedName).CreateItem().HasContextTag("forage_item");//we're in HoeDirt - other forage (or truffles) are covered in object case

                        string destroyedNameForLookUp = "(O)" + (isForageCrop ? ((HoeDirt)removedFeature).crop.whichForageCrop.Value : ((HoeDirt)removedFeature).crop.netSeedIndex.Value.ToString());
                        
                        destroyedName = ItemRegistry.GetMetadata(destroyedNameForLookUp).GetParsedData().DisplayName;

                        int cropPhase = ((HoeDirt)removedFeature).crop.currentPhase.Value;//int representing the current phase
                        cropPhase = (cropPhase > 9) ? 9 : cropPhase;//vanilla has 5-ish phases but this future proofs against fancy multiphase C# crop mods, sacrificing looks in that unlikely case

                        storedDestroyedName = "(D" + cropPhase.ToString() + ")" + destroyedNameForLookUp.Substring(3);//(D) is a fake item data definition to distinguish seeds from other objects
                    }
                    else
                    {
                        destroyedName = "Tilled Soil";
                        storedDestroyedName = "(D0)-1";//it's just tilled soil with no crop
                    }
                }
            }
            catch (Exception)
            {
            }

            try
            {
                if (removedFeature is Flooring)
                {
                    storedDestroyedName = "(O)" + ((Flooring)removedFeature).GetData().ItemId;
                    destroyedName = ItemRegistry.GetMetadata(storedDestroyedName).GetParsedData().DisplayName;
                }
            }
            catch (Exception)
            {
            }

            if (destroyedName == "" && removedObj is null)
            {
                return;
            }

            try
            {
                if (removedObj is not null)
                {
                    storedDestroyedName = removedObj.QualifiedItemId;
                    destroyedName = removedObj.DisplayName;
                }
            }
            catch (Exception)
            {
            }

            //convert from debris's qualified item id to debris name
            string storedDebName = "";
            try
            {
                storedDebName = ItemRegistry.GetMetadata(deb).GetParsedData().InternalName;
            }
            catch (Exception)
            {
            }

            currentDayOfSaveFile = Game1.Date.TotalDays.ToString();//morning of spring 10 means last night's destruction saved as day 9

            //save this damage instance to modData for this location
            //key: my mod ID plus location x and y
            //value: name of the debris, name of the destroyed item
            //Example: {"MODID/1.0.0/12/50/24", "Weeds/(BC)13"}, or something and that gets stored to Farm's modData
            whichFarm.modData[MODID + "/" + VERSION_STRING + "/" + currentDayOfSaveFile + "/" + location.X.ToString() + "/" + location.Y.ToString()] = storedDebName + "/" + storedDestroyedName;
        }


        //our transpiler - injects into near the end of GameLocations.spawnWeedsAndStones and passes along
        //useful information about overnight destruction to be saved for later use
        public static IEnumerable<CodeInstruction> spawnWeedsAndStones_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher matcher = new(instructions);

            MethodInfo getInfo = AccessTools.Method(typeof(ModEntry), nameof(HandleNotifyingDestroyedObjectOrFeature));

            matcher.MatchStartForward(
                new CodeMatch(OpCodes.Br),
                new CodeMatch(OpCodes.Ldloc_S),
                new CodeMatch(OpCodes.Brfalse),
                new CodeMatch(OpCodes.Ldc_I4_0),
                new CodeMatch(OpCodes.Stloc_S)
                )
                .ThrowIfNotMatch($"Could not find entry point for {nameof(spawnWeedsAndStones_Transpiler)}")
                .Advance(3)
                .Insert(
                    new CodeInstruction(OpCodes.Ldloc_S, 10),//location (a vector)
                    new CodeInstruction(OpCodes.Ldloc_S, 17),//debris added
                    new CodeInstruction(OpCodes.Ldarg_0),//game location
                    new CodeInstruction(OpCodes.Call, getInfo)
                );

            return matcher.InstructionEnumeration();
        }


    }
}