using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Objects;
using StardewValley.GameData.FarmAnimals;

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
                bool isCoop = buildingType.Contains("Coop");
                bool isBarn = buildingType.Contains("Barn");

                if (!isCoop && !isBarn)
                    return;

                if ((isCoop && !Config.EnableForCoops) || (isBarn && !Config.EnableForBarns))
                    return;

                List<Chest> chests = FindChestsInBuilding(__instance);
                if (chests.Count == 0)
                    return;

                // Handle animal products differently for barns
                if (isBarn)
                {
                    ProcessBarnAnimals(__instance, chests);
                }
                else
                {
                    ProcessCoopItems(__instance, chests);
                }
            }
            catch (Exception ex)
            {
                ModMonitor.Log($"Error in auto-collection: {ex}", LogLevel.Error);
            }
        }

        private static void ProcessBarnAnimals(AnimalHouse animalHouse, List<Chest> chests)
        {
            int collectedCount = 0;
            
            foreach (FarmAnimal animal in animalHouse.animals.Values)
            {
                try
                {
                    FarmAnimalData animalData = animal.GetAnimalData();
                    if (animal.currentProduce.Value == null || animal.age.Value <= animalData.DaysToMature)
                        continue;

                    // Get the actual product item
                    Item item = GetAnimalProduct(animal);
                    if (item == null)
                        continue;

                    // Try to transfer to chests
                    foreach (Chest chest in chests)
                    {
                        if (TryTransferToChest(item, chest))
                        {
                            // Show HUD message
                            HUDMessage msg = HUDMessage.ForItemGained(item, item.Stack);
                            msg.message = $"Collected {item.DisplayName}";
                            if (item.Stack > 1)
                                msg.message += $" x{item.Stack}";
                            Game1.addHUDMessage(msg);

                            // Clear animal's produce
                            animal.currentProduce.Value = null;
                            animal.daysSinceLastLay.Value = 0;
                            collectedCount++;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModMonitor.Log($"Error processing {animal.displayName}: {ex}", LogLevel.Error);
                }
            }

            if (collectedCount > 0)
            {
                ModMonitor.Log($"Auto-collected {collectedCount} animal products from barn", LogLevel.Info);
            }
        }

        private static void ProcessCoopItems(AnimalHouse animalHouse, List<Chest> chests)
        {
            List<Vector2> toRemove = new List<Vector2>();
            int collectedCount = 0;

            foreach (var pair in animalHouse.objects.Pairs)
            {
                if (IsCollectibleItem(pair.Value, isCoop: true, isBarn: false))
                {
                    StardewValley.Object item = (StardewValley.Object)pair.Value;
                    foreach (Chest chest in chests)
                    {
                        if (TryTransferToChest(item, chest))
                        {
                            // Show HUD message
                            HUDMessage msg = HUDMessage.ForItemGained(item, item.Stack);
                            msg.message = $"Collected {item.DisplayName}";
                            if (item.Stack > 1)
                                msg.message += $" x{item.Stack}";
                            Game1.addHUDMessage(msg);

                            toRemove.Add(pair.Key);
                            collectedCount++;
                            break;
                        }
                    }
                }
            }

            // Remove collected items from the ground
            foreach (Vector2 tile in toRemove)
            {
                animalHouse.objects.Remove(tile);
            }

            if (collectedCount > 0)
            {
                ModMonitor.Log($"Auto-collected {collectedCount} items from coop", LogLevel.Info);
            }
        }

        private static Item GetAnimalProduct(FarmAnimal animal)
        {
            if (animal.currentProduce.Value == null)
                return null;

            // Get base product
            Item item = ItemRegistry.Create(animal.currentProduce.Value);
            
            // Handle quality
            if (item is StardewValley.Object obj)
            {
                obj.Quality = animal.produceQuality.Value switch
                {
                    1 => StardewValley.Object.medQuality,
                    2 => StardewValley.Object.highQuality,
                    4 => StardewValley.Object.bestQuality,
                    _ => StardewValley.Object.lowQuality
                };
            }

            // Handle special cases
            if (animal.hasEatenAnimalCracker.Value)
            {
                item.Stack *= 2;
                animal.hasEatenAnimalCracker.Value = false;
            }

            return item;
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

        private static bool IsCollectibleItem(Item item, bool isCoop, bool isBarn)
        {
            if (item is not StardewValley.Object obj)
                return false;

            // Coop products
            if (isCoop)
            {
                return obj.ParentSheetIndex is 
                    176 or 174 or  // Chicken eggs
                    182 or 180 or  // Brown eggs
                    305 or         // Void Egg
                    928 or         // Golden Egg
                    442 or         // Duck Egg
                    444 or         // Duck Feather
                    107 or         // Dinosaur Egg
                    446;           // Rabbit's Foot
            }

            // Barn products
            if (isBarn)
            {
                return obj.ParentSheetIndex is 
                    184 or 186 or  // Cow Milk
                    436 or 438 or  // Goat Milk
                    440 or         // Wool
                    289;           // Ostrich Egg
            }

            return false;
        }

        private static bool TryTransferToChest(Item item, Chest chest)
        {
            try
            {
                if (chest.Items.Count >= Chest.capacity)
                    return false;

                // Clone the item to prevent reference issues
                Item clonedItem = item.getOne();
                clonedItem.Stack = item.Stack;

                // Try stacking first
                foreach (Item chestItem in chest.Items)
                {
                    if (chestItem?.canStackWith(clonedItem) == true)
                    {
                        chestItem.Stack += clonedItem.Stack;
                        return true;
                    }
                }

                // Add as new item
                return chest.addItem(clonedItem) == null;
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
        public bool EnableForBarns { get; set; } = true;
        public bool IncludePigs { get; set; } = false;  // For future implementation
    }

}