using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Objects;

namespace AutoCollectorMod
{
    public class ModEntry : Mod
    {
        private static ModConfig Config;
        private static IMonitor ModMonitor;
        private Harmony harmony;

        public override void Entry(IModHelper helper)
        {
            Config = Helper.ReadConfig<ModConfig>();
            ModMonitor = Monitor;

            harmony = new Harmony(ModManifest.UniqueID);
            // Patch AnimalHouse instead of Building
            harmony.Patch(
                original: AccessTools.Method(typeof(AnimalHouse), nameof(AnimalHouse.DayUpdate)),
                postfix: new HarmonyMethod(typeof(ModEntry), nameof(After_AnimalHouseDayUpdate))
            );
        }

        private static void After_AnimalHouseDayUpdate(AnimalHouse __instance, int dayOfMonth)
        {
            try
            {
                if (!Config.Enabled)
                    return;

                Building building = GetParentBuilding(__instance);
                if (building == null)
                    return;

                string buildingType = building.buildingType.Value;
                bool isBarn = buildingType.Contains("Barn");
                bool isCoop = buildingType.Contains("Coop");

                if (!(isBarn || (Config.EnableForCoops && isCoop)))
                    return;

                List<Chest> chests = FindChestsInBuilding(__instance);
                if (chests.Count == 0)
                    return;

                List<Vector2> toRemove = new List<Vector2>();
                foreach (var pair in __instance.objects.Pairs)
                {
                    if (IsCollectibleItem(pair.Value))
                    {
                        StardewValley.Object item = (StardewValley.Object)pair.Value;
                        foreach (Chest chest in chests)
                        { 
                            if (TryTransferToChest(pair.Value, chest))
                            {
                                // Generate HUD message with "Collected" prefix
                                string messageText = $"Collected {item.DisplayName}";
                                if (item.Stack > 1)
                                    messageText += $" x{item.Stack}";

                                HUDMessage msg = HUDMessage.ForItemGained(item, item.Stack);
                                msg.message = $"Collected {item.DisplayName}";
                                if (item.Stack > 1)
                                    msg.message += $" x{item.Stack}";

                                Game1.addHUDMessage(msg);

                                toRemove.Add(pair.Key);
                                break;
                            }
                        }
                    }
                }

                foreach (Vector2 tile in toRemove)
                {
                    __instance.objects.Remove(tile);
                }

                if (toRemove.Count > 0)
                {
                    ModMonitor.Log($"Auto-collected {toRemove.Count} items in {buildingType}", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                ModMonitor.Log($"Error in auto-collection: {ex}", LogLevel.Error);
            }
        }

        private static Building GetParentBuilding(AnimalHouse animalHouse)
        {
            foreach (Building building in Game1.getFarm().buildings)
            {
                if (building.GetIndoors() == animalHouse)
                {
                    return building;
                }
            }
            return null;
        }

        private static List<Chest> FindChestsInBuilding(GameLocation location)
        {
            List<Chest> foundChests = new List<Chest>();
            foreach (var pair in location.objects.Pairs)
            {
                if (pair.Value is Chest chest)
                {
                    foundChests.Add(chest);
                }
            }
            return foundChests;
        }

        private static bool IsCollectibleItem(Item item)
        {
            if (item is not StardewValley.Object obj) 
                return false;

            // Expanded list of animal products
            return obj.Type == "Animal" || 
                   obj.Category == -5 ||
                   obj.ParentSheetIndex is 176 or 174 or 182 or 180 or 305 or 928 or 442 or 444 or 107 or 446 or 430;
            /*
            COOP
            Chicken
            176 = White Egg
            174 = Large White Egg
            182 = Large Brown Egg
            180 = Brown Egg
            305 = Void Egg
            928 = Golden Egg
            Duck
            442 = Duck Egg
            444 = Duck Feather
            Dino
            107 = Dinosaur Egg
            Rabbit
            446 = Rabbits Foot

            BARN
            438 = Large Goat Milk
            440 = Wool
            430 = Truffles
            */
        }

        private static bool TryTransferToChest(Item item, Chest chest)
        {
            try
            {
                // Handle full chest
                if (chest.Items.Count >= 36) 
                    return false;

                // Handle stackable items
                foreach (Item chestItem in chest.Items)
                {
                    if (chestItem?.canStackWith(item) == true)
                    {
                        chestItem.Stack += item.Stack;
                        return true;
                    }
                }

                // Add new item
                chest.Items.Add(item);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public class ModConfig
    {
        public bool Enabled { get; set; } = true;
        public bool EnableForCoops { get; set; } = true;
    }
}