﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using BannerlordTwitch;
using BannerlordTwitch.Rewards;
using BannerlordTwitch.Util;
using BLTAdoptAHero.Actions.Util;
using BLTAdoptAHero.Annotations;
using BLTAdoptAHero.Util;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;

namespace BLTAdoptAHero
{
    [UsedImplicitly]
    [Description("Improve adopted heroes equipment")]
    internal class EquipHero : ActionHandlerBase
    {
        private class Settings : IDocumentable
        {
            [Description("Allow improvement of adopted heroes who are also companions of the player."), 
             PropertyOrder(0), UsedImplicitly]
            public bool AllowCompanionUpgrade { get; set; } = true;

            [Description("Gold cost for Tier 1 equipment"), PropertyOrder(1), UsedImplicitly]
            public int CostTier1 { get; set; } = 25000;

            [Description("Gold cost for Tier 2 equipment"), PropertyOrder(2), UsedImplicitly]
            public int CostTier2 { get; set; } = 50000;

            [Description("Gold cost for Tier 3 equipment"), PropertyOrder(3), UsedImplicitly]
            public int CostTier3 { get; set; } = 100000;

            [Description("Gold cost for Tier 4 equipment"), PropertyOrder(4), UsedImplicitly]
            public int CostTier4 { get; set; } = 175000;

            [Description("Gold cost for Tier 5 equipment"), PropertyOrder(5), UsedImplicitly]
            public int CostTier5 { get; set; } = 275000;

            [Description("Gold cost for Tier 6 equipment"), PropertyOrder(6), UsedImplicitly]
            public int CostTier6 { get; set; } = 400000;
            
            // etc..
            
            public int GetTierCost(int tier)
            {
                return tier switch
                {
                    0 => CostTier1,
                    1 => CostTier2,
                    2 => CostTier3,
                    3 => CostTier4,
                    4 => CostTier5,
                    5 => CostTier6,
                    _ => CostTier6
                };
            }
            
            [Description("Whether to re-equip the equipment INSTEAD of upgrading it (you should make TWO commands, " +
                         "one to upgrade and one to reequip)"), PropertyOrder(11), UsedImplicitly]
            public bool ReequipInsteadOfUpgrade { get; set; }

            public void GenerateDocumentation(IDocumentationGenerator generator)
            {
                if (ReequipInsteadOfUpgrade)
                {
                    generator.P("Re-rolls your equipment at your current tier");
                }
                
                
                generator.PropertyValuePair("Tier costs", $"1={CostTier1}{Naming.Gold}, 2={CostTier2}{Naming.Gold}, 3={CostTier3}{Naming.Gold}, 4={CostTier4}{Naming.Gold}, 5={CostTier5}{Naming.Gold}, 5={CostTier5}{Naming.Gold}, 6={CostTier6}{Naming.Gold}");
                
                if (!AllowCompanionUpgrade)
                {
                    generator.P($"Disallowed for player companions");
                }
            }
        }

        protected override Type ConfigType => typeof(Settings);

        protected override void ExecuteInternal(ReplyContext context, object config,
            Action<string> onSuccess,
            Action<string> onFailure)
        {
            var settings = (Settings)config;
            var adoptedHero = BLTAdoptAHeroCampaignBehavior.Current.GetAdoptedHero(context.UserName);
            if (adoptedHero == null)
            {
                onFailure(AdoptAHero.NoHeroMessage);
                return;
            }
            if (!settings.AllowCompanionUpgrade && adoptedHero.IsPlayerCompanion)
            {
                onFailure($"You are a player companion, you cannot change your own equipment!");
                return;
            }
            if (Mission.Current != null)
            {
                onFailure($"You cannot upgrade equipment, as a mission is active!");
                return;
            }

            int targetTier = Math.Max(0, BLTAdoptAHeroCampaignBehavior.Current.GetEquipmentTier(adoptedHero) +
                             (settings.ReequipInsteadOfUpgrade ? 0 : 1));
            
            if (targetTier > 5)
            {
                onFailure($"You cannot upgrade any further!");
                return;
            }
            
            int cost = settings.GetTierCost(targetTier);

            int availableGold = BLTAdoptAHeroCampaignBehavior.Current.GetHeroGold(adoptedHero);
            if (availableGold < cost)
            {
                onFailure(Naming.NotEnoughGold(cost, availableGold));
                return;
            }

            var charClass = BLTAdoptAHeroCampaignBehavior.Current.GetClass(adoptedHero);

            UpgradeEquipment(adoptedHero, targetTier, charClass, !settings.ReequipInsteadOfUpgrade);

            BLTAdoptAHeroCampaignBehavior.Current.SetEquipmentTier(adoptedHero, targetTier);
            BLTAdoptAHeroCampaignBehavior.Current.SetEquipmentClass(adoptedHero, charClass);
            BLTAdoptAHeroCampaignBehavior.Current.ChangeHeroGold(adoptedHero, -cost, isSpending: true);

            // Need to ensure this will not reset the players interactions
            // GameStateManager.Current?.UpdateInventoryUI(adoptedHero);
            
            onSuccess(settings.ReequipInsteadOfUpgrade
                ? $"Re-equipped Tier {targetTier + 1} ({charClass?.Name ?? "No Class"})"
                : $"Equipped Tier {targetTier + 1} ({charClass?.Name ?? "No Class"})");
        }

        internal static void RemoveAllEquipment(Hero adoptedHero)
        {
            foreach (var slot in adoptedHero.BattleEquipment.YieldEquipmentSlots())
            {
                adoptedHero.BattleEquipment[slot.index] = EquipmentElement.Invalid;
            }
            foreach (var slot in adoptedHero.CivilianEquipment.YieldEquipmentSlots())
            {
                adoptedHero.CivilianEquipment[slot.index] = EquipmentElement.Invalid;
            }
        }

        public static int CalculateHeroEquipmentTier(Hero hero) =>
            // The Mode of the tiers of the equipment
            hero.BattleEquipment.YieldEquipmentSlots()
                .Where(s => s.index is >= EquipmentIndex.ArmorItemBeginSlot and < EquipmentIndex.ArmorItemEndSlot || s.element.Item != null)
                .Select(s => s.element.Item)
                .Select(i => i == null ? -1 : (int)i.Tier)
                .GroupBy(v => v)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?
                .Key ?? -1;

        public static bool IsWeaponUsableByHeroAndClass(Hero hero, ItemObject o, HeroClassDef heroClass) =>
            heroClass?.Mounted != true
            || o.PrimaryWeapon == null
            || !MBItem.GetItemUsageSetFlags(o.PrimaryWeapon.ItemUsage).HasFlag(ItemObject.ItemUsageSetFlags.RequiresNoMount)
            || o.Type == ItemObject.ItemTypeEnum.Bow && hero?.CharacterObject?.GetPerkValue(DefaultPerks.Bow.HorseMaster) == true
            || o.Type == ItemObject.ItemTypeEnum.Crossbow && hero?.CharacterObject?.GetPerkValue(DefaultPerks.Crossbow.MountedCrossbowman) == true
            ;

        public static void UpgradeEquipment(Hero adoptedHero, int targetTier, HeroClassDef classDef, bool keepBetter)
        {
            // Take existing equipment and the heroes custom items, so we can (re)use them if appropriate
            var availableItems = adoptedHero.BattleEquipment.YieldEquipmentSlots()
                .Select(e => e.element).Where(i => !i.IsEmpty)
                .Concat(BLTAdoptAHeroCampaignBehavior.Current.GetCustomItems(adoptedHero))
                .Where(e => IsWeaponUsableByHeroAndClass(adoptedHero, e.Item, classDef))
                .ToList();

            // Clear the weapon slots
            foreach (var x in adoptedHero.BattleEquipment.YieldEquipmentSlots())
            {
                adoptedHero.BattleEquipment[x.index] = EquipmentElement.Invalid;
            }

            EquipmentElement FindNewEquipment(Func<ItemObject, bool> filter = null, FindFlags flags = FindFlags.None)
            {
                var oldEquipment = availableItems.FirstOrDefault(i =>
                    (keepBetter && i.Item.Tier >= (ItemObject.ItemTiers) targetTier 
                        || BLTCustomItemsCampaignBehavior.Current.IsRegistered(i.ItemModifier))
                    && filter?.Invoke(i.Item) != false
                    );
                if (!oldEquipment.IsEmpty)
                    return oldEquipment;
                var foundItem = FindRandomTieredEquipment(targetTier, adoptedHero, flags, 
                    o => filter?.Invoke(o) != false && IsWeaponUsableByHeroAndClass(adoptedHero, o, classDef)); 
                if (foundItem == null)
                    return default;
                return new(foundItem);
            }
            EquipmentElement FindNewEquipmentBySkill(SkillObject skill, Func<ItemObject, bool> filter = null, FindFlags flags = FindFlags.None)
                => FindNewEquipment(o => o.RelevantSkill == skill && filter?.Invoke(o) != false, flags);
            EquipmentElement FindNewEquipmentByType(ItemObject.ItemTypeEnum itemType, Func<ItemObject, bool> filter = null, FindFlags flags = FindFlags.None)
                => FindNewEquipment(o => o.ItemType == itemType && filter?.Invoke(o) != false, flags);

            if (classDef != null)
            {
                // Select class specific weapons if we can
                foreach (var (equipmentType, slot) in classDef.SlotItems
                            .Zip(adoptedHero.BattleEquipment.YieldWeaponSlots(), (equipmentType, slot) => (equipmentType, slot)))
                {
                    var weapon = FindNewEquipment(e => e.IsEquipmentType(equipmentType));
                    if(!weapon.IsEmpty)
                    {
                        adoptedHero.BattleEquipment[slot.index] = weapon;
                    }
                }
            }
            else
            {
                static bool IsPrimaryWeaponItemType(ItemObject o) =>
                    o.Type is ItemObject.ItemTypeEnum.OneHandedWeapon 
                        or ItemObject.ItemTypeEnum.TwoHandedWeapon 
                        or ItemObject.ItemTypeEnum.Polearm 
                        or ItemObject.ItemTypeEnum.Bow
                        or ItemObject.ItemTypeEnum.Crossbow 
                        or ItemObject.ItemTypeEnum.Thrown;
                
                // Without class specified, we instead use some heuristics based on the heroes top weapon skills
                var addedWeapons = new List<EquipmentElement>();
                var currSlot = EquipmentIndex.WeaponItemBeginSlot;
                
                var weaponSkills = SkillGroup.SkillItemPairs
                    .OrderByDescending(s => adoptedHero.GetSkillValue(s.skill))
                    .Take(1)
                    .ToList();

                EquipmentElement primaryAmmo = default;
                foreach (var weapon in weaponSkills
                    .Select(s => FindNewEquipmentBySkill(s.skill, IsPrimaryWeaponItemType))
                    .Where(e => !e.IsEmpty)
                )
                {
                    var ammoType = ItemObject.GetAmmoTypeForItemType(weapon.Item.Type);

                    // We need at least 2 slots if the weapon requires ammo, so just skip if we don't have 2 left
                    if (ammoType != weapon.Item.Type && ammoType != ItemObject.ItemTypeEnum.Invalid && currSlot >= EquipmentIndex.Weapon3)
                        continue;

                    adoptedHero.BattleEquipment[currSlot++] = weapon;
                    addedWeapons.Add(weapon);

                    // Exit once we run out of weapon slots
                    if (currSlot > EquipmentIndex.Weapon3)
                        break;

                    // Add one ammo if we need it
                    if (ammoType != weapon.Item.Type && ammoType != ItemObject.ItemTypeEnum.Invalid)
                    {
                        primaryAmmo = FindNewEquipmentByType(ammoType);
                        if (!primaryAmmo.IsEmpty)
                        {
                            adoptedHero.BattleEquipment[currSlot++] = primaryAmmo;
                            if (currSlot > EquipmentIndex.Weapon3)
                                break;
                        }
                    }
                    else if (ammoType == weapon.Item.Type)
                    {
                        primaryAmmo = weapon;
                    }
                }

                // If we have space left and existing weapons don't support swinging, then add a weapon that does,
                // appropriate to our skills
                if (currSlot <= EquipmentIndex.Weapon3 && !addedWeapons.Any(e => WeaponIsSwingable(e.Item)))
                {
                    var weapon = SkillGroup.MeleeSkillItemPairs
                        .OrderByDescending(s => adoptedHero.GetSkillValue(s.skill))
                        .Select(s => FindNewEquipmentBySkill(
                            s.skill, 
                            o => IsPrimaryWeaponItemType(o) && WeaponIsSwingable(o)))
                        .FirstOrDefault(w => !w.IsEmpty);
                    
                    if (!weapon.IsEmpty)
                    {
                        adoptedHero.BattleEquipment[currSlot++] = weapon;
                        addedWeapons.Add(weapon);
                    }
                }

                // Add one more primary ammo
                if (currSlot <= EquipmentIndex.Weapon3 && !primaryAmmo.IsEmpty)
                {
                    adoptedHero.BattleEquipment[currSlot++] = primaryAmmo;
                }

                // If we have space left then add a shield if we have a 1H weapon that allows shield
                if (currSlot <= EquipmentIndex.Weapon3
                    && addedWeapons.Any(w 
                        => !WeaponRequires(w.Item, ItemObject.ItemUsageSetFlags.RequiresNoShield)))
                {
                    var shield = FindNewEquipmentByType(ItemObject.ItemTypeEnum.Shield);
                    if (!shield.IsEmpty)
                    {
                        adoptedHero.BattleEquipment[currSlot] = new EquipmentElement(shield);
                    }
                }
            }

            // Always want armor obviously
            foreach (var (index, itemType) in SkillGroup.ArmorIndexType)
            {
                adoptedHero.BattleEquipment[index] = FindNewEquipmentByType(itemType);
            }
            
            // We should assign a horse if using a class definition that specifies riding, OR 
            // if not using class definition and the riding skill is better than athletics, or polearm
            // is the top combat skill
            if (HeroShouldUseHorse(adoptedHero, classDef))
            {
                var horse = FindNewEquipmentByType(
                    ItemObject.ItemTypeEnum.Horse,
                    h => 
                        h.HorseComponent?.IsMount == true
                        && (classDef == null
                            || classDef.UseHorse && h.HorseComponent.Monster.FamilyType == (int) MountFamilyType.horse
                            || classDef.UseCamel && h.HorseComponent.Monster.FamilyType == (int) MountFamilyType.camel
                        ),
                    // allow non-merchandise mounts, to include the tournament prize ones, and ignore ability to allow camel riders to ride something
                    FindFlags.IgnoreAbility | FindFlags.AllowNonMerchandise
                );
                if (!horse.IsEmpty)
                {
                    adoptedHero.BattleEquipment[EquipmentIndex.Horse] = horse;

                    int horseType = horse.Item.HorseComponent.Monster.FamilyType;
                    adoptedHero.BattleEquipment[EquipmentIndex.HorseHarness] = FindNewEquipmentByType(
                        ItemObject.ItemTypeEnum.HorseHarness, h => horseType == h.ArmorComponent?.FamilyType
                        );
                }
            }

            UpgradeCivilian(adoptedHero, targetTier, keepBetter);
        }

        public static bool HeroShouldUseHorse(Hero adoptedHero, HeroClassDef classDef)
        {
            var heroWeapons = adoptedHero.BattleEquipment.YieldFilledWeaponSlots().Select(e => e.element.Item).ToList();
            return classDef is {Mounted: true} 
                   || classDef == null
                   && (
                       // One of our weapons requires a mount (not sure this is actually a thing)
                       heroWeapons.Any(s => WeaponRequires(s, ItemObject.ItemUsageSetFlags.RequiresMount))
                       ||
                       // Any of our weapons *allows* a mount
                       !heroWeapons.All(s => WeaponRequires(s, ItemObject.ItemUsageSetFlags.RequiresNoMount))
                       // Either our riding skill is better than athletics or we have a thrust only polearm
                       && (adoptedHero.GetSkillValue(DefaultSkills.Riding) > adoptedHero.GetSkillValue(DefaultSkills.Athletics)
                           || heroWeapons.Any(s => s.Type == ItemObject.ItemTypeEnum.Polearm && !WeaponIsSwingable(s)))
                   );
        }

        public static bool WeaponIsSwingable(ItemObject w) 
            => w.PrimaryWeapon?.IsMeleeWeapon == true && w.PrimaryWeapon?.SwingDamageType != DamageTypes.Invalid;

        public static bool WeaponRequires(ItemObject w, ItemObject.ItemUsageSetFlags flag) 
            => w.PrimaryWeapon?.ItemUsage != null && MBItem.GetItemUsageSetFlags(w.PrimaryWeapon.ItemUsage).HasFlag(flag);

        private static void UpgradeCivilian(Hero adoptedHero, int targetTier, bool keepBetter)
        {
            foreach (var (index, itemType) in SkillGroup.ArmorIndexType)
            {
                UpgradeItemInSlot(index, itemType, targetTier, keepBetter, 
                    adoptedHero.CivilianEquipment, adoptedHero, o => o.IsCivilian);
            }

            // Clear weapon slots beyond 0
            foreach (var x in adoptedHero.CivilianEquipment.YieldWeaponSlots().Skip(1))
            {
                adoptedHero.CivilianEquipment[x.index] = EquipmentElement.Invalid;
            }
            
            UpgradeItemInSlot(EquipmentIndex.Weapon0, ItemObject.ItemTypeEnum.OneHandedWeapon, 
                targetTier, keepBetter, adoptedHero.CivilianEquipment, adoptedHero);
        }
        
        public enum MountFamilyType
        {
            human,
            horse,
            camel,
            cow,
            goose,
            hog,
            sheep,
            hare,
        }

        private static ItemObject UpgradeItemInSlot(EquipmentIndex equipmentIndex, ItemObject.ItemTypeEnum itemType, 
            int tier, bool keepBetter, Equipment equipment, Hero hero, Func<ItemObject, bool> filter = null)
        {
            var slot = equipment[equipmentIndex];
            if ((slot.ItemModifier == null || !BLTCustomItemsCampaignBehavior.Current.IsRegistered(slot.ItemModifier))
                && (!keepBetter || slot.Item == null || slot.Item.Tier < (ItemObject.ItemTiers) tier 
                    || filter?.Invoke(slot.Item) == false))
            {
                var item = FindRandomTieredEquipment(tier, hero, FindFlags.None, o 
                    => o.ItemType == itemType && filter?.Invoke(o) != false);
                if (item != null 
                    && (!keepBetter || slot.Item == null || slot.Item.Tier < item.Tier || filter?.Invoke(slot.Item) == false))
                {
                    equipment[equipmentIndex] = new (item);
                    return item;
                }
            }

            return null;
        }

        [Flags]
        public enum FindFlags
        {
            None = 0,
            IgnoreAbility = 1 << 0,
            AllowNonMerchandise = 1 << 1,
            RequireExactTier = 1 << 2,
        }
        
        public static ItemObject FindRandomTieredEquipment(int tier, Hero hero, FindFlags flags = FindFlags.None, 
            Func<ItemObject, bool> filter = null)
        {
            var items =
                HeroHelpers.AllItems.Where(item =>
                    // Non-merchandise includes some weird items like testing ones in some cases
                    (!item.NotMerchandise || flags.HasFlag(FindFlags.AllowNonMerchandise))
                    // Usable
                    && CanUseItem(item, hero, flags.HasFlag(FindFlags.IgnoreAbility))
                    // Custom filter
                    && filter?.Invoke(item) != false
                    )
                .ToList();

            if (flags.HasFlag(FindFlags.RequireExactTier))
            {
                return items
                    .Where(item => (int) item.Tier == tier)
                    .SelectRandom();
            }
            else
            {
                return FindRandomItemNearestTier(items, tier);
            }
        }

        public static ItemObject FindRandomItemNearestTier(IEnumerable<ItemObject> items, int tier)
        {
            // This should order the tier groups to be
            // (closest tier below the desired one), (closest tier above the desired one), etc...
            var tieredItems = items.GroupBy(item => (int) item.Tier)
                .OrderBy(t => 100 * Math.Abs(tier - t.Key) + t.Key)
                .ToList();

            return tieredItems
                .FirstOrDefault()?
                .SelectRandom();
        }

        public static bool CanUseItem(ItemObject item, Hero hero, bool overrideAbility)
        {
            var relevantSkill = item.RelevantSkill;
            return    (overrideAbility || relevantSkill == null || hero.GetSkillValue(relevantSkill) >= item.Difficulty) 
                   && (!hero.CharacterObject.IsFemale || !item.ItemFlags.HasAnyFlag(ItemFlags.NotUsableByFemale)) 
                   && (hero.CharacterObject.IsFemale || !item.ItemFlags.HasAnyFlag(ItemFlags.NotUsableByMale));
        }
    }
}