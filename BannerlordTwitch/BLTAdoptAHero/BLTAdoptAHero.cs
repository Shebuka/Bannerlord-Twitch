﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using BannerlordTwitch.Rewards;
using BannerlordTwitch.Util;
using BLTAdoptAHero.Actions.Util;
using HarmonyLib;
using JetBrains.Annotations;
using SandBox;
using SandBox.View;
using SandBox.View.Missions;
using SandBox.ViewModelCollection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.TwoDimension;
using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;
using YamlDotNet.Serialization;

#pragma warning disable 649

namespace BLTAdoptAHero
{
    [UsedImplicitly]
    [HarmonyPatch]
    public class BLTAdoptAHeroModule : MBSubModuleBase
    {
        private Harmony harmony;
        public const string Name = "BLTAdoptAHero";
        public const string Ver = "1.4.6";

        internal static GlobalCommonConfig CommonConfig { get; private set; }
        internal static GlobalTournamentConfig TournamentConfig { get; private set; }
        internal static GlobalHeroClassConfig HeroClassConfig { get; private set; }
        public BLTAdoptAHeroModule()
        {
            ActionManager.RegisterAll(typeof(BLTAdoptAHeroModule).Assembly);
            GlobalCommonConfig.Register();
            GlobalTournamentConfig.Register();
            GlobalHeroClassConfig.Register();
        }

        public override void OnMissionBehaviourInitialize(Mission mission)
        {
            // Add the marker overlay for appropriate mission types
            if(mission.GetMissionBehaviour<MissionNameMarkerUIHandler>() == null 
               && (MissionHelpers.InSiegeMission() 
                   || MissionHelpers.InFieldBattleMission() 
                   || Mission.Current?.GetMissionBehaviour<TournamentFightMissionController>() != null))
            {
                mission.AddMissionBehaviour(SandBoxViewCreator.CreateMissionNameMarkerUIHandler(mission));
            }
            mission.AddMissionBehaviour(new BLTAdoptAHeroCommonMissionBehavior());
            mission.AddMissionBehaviour(new BLTAdoptAHeroCustomMissionBehavior());
            mission.AddMissionBehaviour(new BLTSummonBehavior());
            mission.AddMissionBehaviour(new BLTRemoveAgentsBehavior());
        }
        
        [UsedImplicitly, HarmonyPostfix, HarmonyPatch(typeof(MissionNameMarkerTargetVM), MethodType.Constructor, typeof(Agent))]
        public static void MissionNameMarkerTargetVMConstructorPostfix(MissionNameMarkerTargetVM __instance, Agent agent)
        {
            if (MissionHelpers.InSiegeMission() || MissionHelpers.InFieldBattleMission() || MissionHelpers.InHideOutMission())
            {
                if (Agent.Main != null && agent.IsEnemyOf(Agent.Main) || agent.Team.IsEnemyOf(Mission.Current.PlayerTeam))
                {
                    __instance.MarkerType = 2;
                }
                else if (Agent.Main != null && agent.IsFriendOf(Agent.Main) || agent.Team.IsFriendOf(Mission.Current.PlayerTeam))
                {
                    __instance.MarkerType = 0;
                }
            }
        }

        [UsedImplicitly, HarmonyPostfix, HarmonyPatch(typeof(MissionNameMarkerVM), MethodType.Constructor, typeof(Mission), typeof(Camera))]
        // ReSharper disable once RedundantAssignment
        public static void MissionNameMarkerVMConstructorPostfix(MissionNameMarkerVM __instance, Mission mission, ref Vec3 ____heightOffset)
        {
            ____heightOffset = new Vec3(0, 0, 4, -1);
        }

        [UsedImplicitly, HarmonyPatch(typeof(DefaultClanTierModel), nameof(DefaultClanTierModel.GetCompanionLimit))]
        public static void Postfix(ref int __result)
        {
            if (CommonConfig != null && CommonConfig.BreakCompanionLimit)
            {
                __result = Clan.PlayerClan.Companions.Count + 1;
            }
            return;
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            if (harmony == null)
            {
                harmony = new Harmony("mod.bannerlord.bltadoptahero");
                harmony.PatchAll();
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            if(game.GameType is Campaign) 
            {
                // Reload settings here so they are fresh
                CommonConfig = GlobalCommonConfig.Get();
                TournamentConfig = GlobalTournamentConfig.Get();
                HeroClassConfig = GlobalHeroClassConfig.Get();

                var campaignStarter = (CampaignGameStarter) gameStarterObject;
                campaignStarter.AddBehavior(new BLTAdoptAHeroCampaignBehavior());
                JoinTournament.AddBehaviors(campaignStarter);
                
                campaignStarter.AddBehavior(new BLTCustomItemsCampaignBehavior());
            }
        }
        
        public override void BeginGameStart(Game game)
        {
        }
        
        // public override void OnCampaignStart(Game game, object starterObject)
        // {
        //     base.OnCampaignStart(game, starterObject);
        //     // JoinTournament.SetupGameMenus(starterObject as CampaignGameStarter);
        // }
        
        public override void OnGameEnd(Game game)
        {
            base.OnGameEnd(game);
            if(game.GameType is Campaign campaign) 
            {
                JoinTournament.OnGameEnd(campaign);
            }
        }

        internal const string Tag = "[BLT]";
    }

    [CategoryOrder("General", 1)]
    [CategoryOrder("Kill Rewards", 2)]
    [CategoryOrder("Battle End Rewards", 3)]
    [CategoryOrder("Shouts", 4)]
    [CategoryOrder("Kill Streaks", 5)]
    internal class GlobalCommonConfig : IConfig
    {

        private const string ID = "Adopt A Hero - General Config";

        internal static void Register() => ActionManager.RegisterGlobalConfigType(ID, typeof(GlobalCommonConfig));
        internal static GlobalCommonConfig Get() => ActionManager.GetGlobalConfig<GlobalCommonConfig>(ID);

        [Category("General"), Description("Whether the hero is allowed to die"), PropertyOrder(3)]
        public bool AllowDeath { get; [UsedImplicitly] set; }
        
        [Category("General"), Description("Chance (from 0 to 1) of killing blow not being reduced to a knock out blow (when Allow Death is enabled above). Remember the death chance for non companion heroes is 10% in vanilla, and this chance is applied as well as that. So if you set death chance to 0.5 (50%), then final death chance is 50% of 10%, which is 5%. Setting this to > 1 will NOT increase final death chance beyond 10%."), PropertyOrder(4)]
        public float DeathChance { get; [UsedImplicitly] set; } = 0.2f;

        [Category("General"), Description("Whether the hero will always start with full health"), PropertyOrder(5)]
        public bool StartWithFullHealth { get; set; } = true;

        [Category("General"),
         Description("Amount to multiply normal starting health by, to give heroes better staying power vs others"),
         PropertyOrder(6)]
        public float StartHealthMultiplier { get; set; } = 2;

        [Category("General"),
         Description("Amount to multiply normal retinue starting health by, to give retinue better staying power vs others"),
         PropertyOrder(7)]
        public float StartRetinueHealthMultiplier { get; set; } = 2;

        [Category("General"),
         Description("Reduces morale loss when summoned heroes die"),
         PropertyOrder(8)]
        public float MoraleLossFactor { get; set; } = 0.5f;

        [Category("General"),
         Description("Multiplier applied to all rewards for subscribers (less or equal to 1 means no boost)"),
         PropertyOrder(10)]
        public float SubBoost { get; set; } = 1;

        [Category("General"),
         Description("Use raw XP values instead of adjusting by focus and attributes, also ignoring skill cap. This avoids characters getting stuck when focus and attributes are not well distributed. You should consider hiding "),
         PropertyOrder(11)]
        public bool UseRawXP { get; set; } = true;

        [Category("General"), Description("Whether an adopted heroes retinue should spawn in the same formation as the hero (otherwise they will go into default formations)"), PropertyOrder(12)]
        public bool RetinueUseHeroesFormation { get; [UsedImplicitly] set; }

        [Category("General"), Description("Minimum time between summons for a specific hero"), PropertyOrder(13)]
        public int SummonCooldownInSeconds { get; [UsedImplicitly] set; } = 20;
        [Browsable(false), YamlIgnore]
        public bool CooldownEnabled => SummonCooldownInSeconds > 0;

        [Category("General"), Description("How much to multiply the cooldown by each time summon is used. e.g. if Summon Cooldown is 20 seconds, and UseMultiplier is 1.1 (the default), then the first summon has a cooldown of 20 seconds, and the next 24 seconds, the 10th 52 seconds, and the 20th 135 seconds. See https://www.desmos.com/calculator/muej1o5eg5 for a visualization of this."), PropertyOrder(14)]
        public float SummonCooldownUseMultiplier { get; [UsedImplicitly] set; } = 1.1f;

        public float GetCooldownTime(int summoned) => (float) (Math.Pow(SummonCooldownUseMultiplier, Mathf.Max(1, summoned)) * SummonCooldownInSeconds);

        [Category("General"), Description("Will disable companion limit. You will be able to have infinite number of companion"), PropertyOrder(13)]
        public bool BreakCompanionLimit { get; set; } = false;

        [Category("Kill Rewards"), Description("Gold the hero gets for every kill"), PropertyOrder(1)]
        public int GoldPerKill { get; set; } = 5000;

        [Category("Kill Rewards"), Description("XP the hero gets for every kill"), PropertyOrder(2)]
        public int XPPerKill { get; set; } = 5000;

        [Category("Kill Rewards"), Description("XP the hero gets for being killed"), PropertyOrder(3)]
        public int XPPerKilled { get; set; } = 2000;

        [Category("Kill Rewards"), Description("HP the hero gets for every kill"), PropertyOrder(4)]
        public int HealPerKill { get; set; } = 20;

        [Category("Kill Rewards"), Description("Gold the hero gets for every kill their retinue gets"),
         PropertyOrder(5)]
        public int RetinueGoldPerKill { get; set; } = 2500;

        [Category("Kill Rewards"), Description("HP the hero's retinue gets for every kill"), PropertyOrder(6)]
        public int RetinueHealPerKill { get; set; } = 50;

        [Category("Kill Rewards"),
         Description("How much to scale the kill rewards by, based on relative level of the two characters. " +
                     "If this is 0 (or not set) then the rewards are always as specified, if this is higher than 0 " +
                     "then the rewards increase if the killed unit is higher level than the hero, and decrease if it " +
                     "is lower. At a value of 0.5 (recommended) at level difference of 10 would give about 2.5 times " +
                     "the normal rewards for gold, xp and health."),
         PropertyOrder(7)]
        public float RelativeLevelScaling { get; set; } = 0.5f;

        [Category("Kill Rewards"),
         Description("Caps the maximum multiplier for the level difference, defaults to 5 if not specified"),
         PropertyOrder(8)]
        public float LevelScalingCap { get; set; } = 5;

        [Category("Battle End Rewards"), Description("Gold won if the heroes side wins"), PropertyOrder(1)]
        public int WinGold { get; set; } = 10000;

        [Category("Battle End Rewards"), Description("XP the hero gets if the heroes side wins"), PropertyOrder(2)]
        public int WinXP { get; set; } = 10000;

        [Category("Battle End Rewards"), Description("Gold lost if the heroes side loses"), PropertyOrder(3)]
        public int LoseGold { get; set; } = 5000;

        [Category("Battle End Rewards"), Description("XP the hero gets if the heroes side loses"), PropertyOrder(4)]
        public int LoseXP { get; set; } = 5000;
        
        [Category("Battle End Rewards"), Description("Apply difficulty scaling to players side"), PropertyOrder(5)]
        public bool DifficultyScalingOnPlayersSide { get; set; } = true;
        
        [Category("Battle End Rewards"), Description("Apply difficulty scaling to enemy side"), PropertyOrder(6)]
        public bool DifficultyScalingOnEnemySide { get; set; } = true;
        
        [Category("Battle End Rewards"), Description("End reward difficulty scaling: determines the extent to which higher difficulty battles increase the above rewards"), PropertyOrder(7)]
        public float DifficultyScaling { get; set; } = 1;

        [YamlIgnore, Browsable(false)]
        public float DifficultyScalingClamped => MathF.Clamp(DifficultyScaling, 0, 5);
        
        [Category("Battle End Rewards"), Description("Min difficulty scaling multiplier"), PropertyOrder(8)]
        public float DifficultyScalingMin { get; set; } = 0.2f;
        [YamlIgnore, Browsable(false)]
        public float DifficultyScalingMinClamped => MathF.Clamp(DifficultyScalingMin, 0, 1);

        [Category("Battle End Rewards"), Description("Max difficulty scaling multiplier"), PropertyOrder(9), UsedImplicitly]
        public float DifficultyScalingMax { get; set; } = 3f;
        [YamlIgnore, Browsable(false)]
        public float DifficultyScalingMaxClamped => Math.Max(DifficultyScalingMax, 1f);
        
        [Category("Shouts"), Description("Custom shouts"), PropertyOrder(1)]
        public List<SummonHero.Shout> Shouts { get; set; } = new();

        [Category("Shouts"), Description("Whether to include default shouts"), PropertyOrder(2)]
        public bool IncludeDefaultShouts { get; set; } = true;

        [Category("Kill Streak Rewards"), Description("Kill Streaks"), PropertyOrder(1), UsedImplicitly]
        public List<KillStreakRewards> KillStreaks { get; set; } = new();

        [Category("Kill Streak Rewards"), Description("Whether to use the popup banner to announce kill streaks. Will only print in the overlay instead if disabled."), PropertyOrder(2)]
        public bool ShowKillStreakPopup { get; set; } = true;

        [Category("Kill Streak Rewards"), Description("Sound to play when killstreak popup is disabled."),
         PropertyOrder(3)]
        public Log.Sound KillStreakPopupAlertSound { get; [UsedImplicitly] set; } = Log.Sound.Horns2;
        
        [Category("Kill Streak Rewards"), Description("The level at which the rewards normalize and start to reduce (if relative level scaling is enabled)."), PropertyOrder(4)]
        public int ReferenceLevelReward { get; set; } = 15;

        [Category("General"), Description("Achievements"), PropertyOrder(15), UsedImplicitly]
        public List<AchievementSystem> Achievements { get; set; } = new();

        // This is just a copy of the achievements that existed on loading, so we can assign unique IDs to any new ones when
        // we save
        private List<AchievementSystem> loadedAchievements;
        public void OnLoaded()
        {
            foreach (var a in Achievements
                .GroupBy(a => a.ID)
                .SelectMany(g => g.Skip(1)))
            {
                a.ID = Guid.NewGuid();
            }
            loadedAchievements = Achievements.ToList();
        }

        public void OnSaving()
        {
            foreach (var achievement in Achievements.Except(loadedAchievements))
            {
                achievement.ID = Guid.NewGuid();
            }
        }

        public void OnEditing() { }
    }

    [CategoryOrder("General", 1)]
    [CategoryOrder("Match Rewards", 2)]
    [CategoryOrder("Prize", 3)]
    [CategoryOrder("Prize Tier", 4)]
    [CategoryOrder("Custom Prize", 5)]
    internal partial class GlobalTournamentConfig
    {
        private const string ID = "Adopt A Hero - Tournament Config";
        internal static void Register() => ActionManager.RegisterGlobalConfigType(ID, typeof(GlobalTournamentConfig));
        internal static GlobalTournamentConfig Get() => ActionManager.GetGlobalConfig<GlobalTournamentConfig>(ID);

        [Category("General"), Description("Amount to multiply normal starting health by"), PropertyOrder(1)]
        public float StartHealthMultiplier { get; set; } = 2;

        [Category("General"), Description("Remove horses completely from the BLT tournaments (the horse AI is terrible)"), PropertyOrder(2)]
        public bool NoHorses { get; set; } = true;
        
        [Category("General"), Description("Replaces all lances and spears with swords, because lance and spear combat is terrible"), PropertyOrder(3)]
        public bool NoSpears { get; set; } = true;

        [Category("General"), Description("Replaces all armor with Tier 3 Armor (Based on Culture if possible)"), PropertyOrder(4)]
        public bool NormalizeArmor { get; set; } = true;

        [Category("Rewards"), Description("Gold won if the hero wins the tournaments"), PropertyOrder(1)]
        public int WinGold { get; set; } = 50000;

        [Category("Rewards"), Description("XP given if the hero wins the tournaments"), PropertyOrder(2)]
        public int WinXP { get; set; } = 50000;

        [Category("Rewards"), Description("XP given if the hero participates in a tournament"), PropertyOrder(3)]
        public int ParticipateXP { get; set; } = 10000;

        [Category("Match Rewards"), Description("Gold won if the hero wins their match"), PropertyOrder(1)]
        public int WinMatchGold { get; set; } = 10000;

        [Category("Match Rewards"), Description("XP given if the hero wins their match"), PropertyOrder(2)]
        public int WinMatchXP { get; set; } = 10000;

        [Category("Match Rewards"), Description("XP given if the hero participates in a match"), PropertyOrder(3)]
        public int ParticipateMatchXP { get; set; } = 2500;

        [Category("Prize"), Description("Relative proportion of prizes that will be weapons. This includes all one handed, two handed, ranged and ammo."), PropertyOrder(1)]
        public float PrizeWeaponWeight { get; set; } = 1f;

        [Category("Prize"), Description("Relative proportion of prizes that will be armor"), PropertyOrder(2)]
        public float PrizeArmorWeight { get; set; } = 1f;

        [Category("Prize"), Description("Relative proportion of prizes that will be mounts"), PropertyOrder(3)]
        public float PrizeMountWeight { get; set; } = 0.1f;
        
        // Prizes:
        // Random vanilla equipment, chance for each tier
        // Generated vanilla equip,ent

        [Category("Prize Tier"), Description("Relative proportion of prizes that will be Tier 1"), PropertyOrder(1)]
        public float PrizeTier1Weight { get; set; } = 0f;

        [Category("Prize Tier"), Description("Relative proportion of prizes that will be Tier 2"), PropertyOrder(2)]
        public float PrizeTier2Weight { get; set; } = 0f;

        [Category("Prize Tier"), Description("Relative proportion of prizes that will be Tier 3"), PropertyOrder(3)]
        public float PrizeTier3Weight { get; set; } = 0f;

        [Category("Prize Tier"), Description("Relative proportion of prizes that will be Tier 4"), PropertyOrder(4)]
        public float PrizeTier4Weight { get; set; } = 0f;

        [Category("Prize Tier"), Description("Relative proportion of prizes that will be Tier 5"), PropertyOrder(5)]
        public float PrizeTier5Weight { get; set; } = 3f;

        [Category("Prize Tier"), Description("Relative proportion of prizes that will be Tier 6"), PropertyOrder(6)]
        public float PrizeTier6Weight { get; set; } = 2f;

        [Category("Prize Tier"), Description("Relative proportion of prizes that will be Custom (Tier 6 with modifiers as per the Custom Prize settings below)"), PropertyOrder(7)]
        public float PrizeCustomWeight { get; set; } = 1f;

        [Browsable(false), YamlIgnore]
        public IEnumerable<(int tier, float weight)> PrizeTierWeights
        {
            get
            {
                yield return (tier: 0, weight: PrizeTier1Weight);
                yield return (tier: 1, weight: PrizeTier2Weight);
                yield return (tier: 2, weight: PrizeTier3Weight);
                yield return (tier: 3, weight: PrizeTier4Weight);
                yield return (tier: 4, weight: PrizeTier5Weight);
                yield return (tier: 5, weight: PrizeTier6Weight);
                yield return (tier: 6, weight: PrizeCustomWeight);
            }
        }

        public class CustomPrizeConfig
        {
            [Description("Custom prize power, a global multiplier for the values below"), PropertyOrder(1)]
            public float Power { get; set; } = 1f;

            [Description("Weapon damage modifier for custom weapon prize"), PropertyOrder(2), UsedImplicitly, ExpandableObject]
            public RangeInt WeaponDamage { get; set; } = new(25, 50);
            
            [Description("Speed modifier for custom weapon prize"), PropertyOrder(3), UsedImplicitly, ExpandableObject]
            public RangeInt WeaponSpeed { get; set; } = new(25, 50);
            
            [Description("Missile speed modifier for custom weapon prize"), PropertyOrder(4), UsedImplicitly, ExpandableObject]
            public RangeInt WeaponMissileSpeed { get; set; } = new(25, 50);
            
            [Description("Ammo damage modifier for custom ammo prize"), PropertyOrder(5), UsedImplicitly, ExpandableObject]
            public RangeInt AmmoDamage { get; set; } = new (10, 30);
              
            [Description("Arrow stack size modifier for custom arrow prize"), PropertyOrder(6), UsedImplicitly, ExpandableObject]
            public RangeInt ArrowStack { get; set; } = new(25, 50);
              
            [Description("Throwing stack size modifier for custom throwing prize"), PropertyOrder(7), UsedImplicitly, ExpandableObject]
            public RangeInt ThrowingStack { get; set; } = new(2, 6);
            
            [Description("Armor modifier for custom armor prize"), PropertyOrder(8), UsedImplicitly, ExpandableObject]
            public RangeInt Armor { get; set; } = new(10, 20);
            
            [Description("Maneuver multiplier for custom mount prize"), PropertyOrder(9), UsedImplicitly, ExpandableObject]
            public RangeFloat MountManeuver { get; set; } = new(1.25f, 2f);
            
            [Description("Speed multiplier for custom mount prize"), PropertyOrder(10), UsedImplicitly, ExpandableObject]
            public RangeFloat MountSpeed { get; set; } = new(1.25f, 2f);
              
            [Description("Charge damage multiplier for custom mount prize"), PropertyOrder(11), UsedImplicitly, ExpandableObject]
            public RangeFloat MountChargeDamage { get; set; } = new(1.25f, 2f);

            [Description("Hitpoints multiplier for custom mount prize"), PropertyOrder(12), UsedImplicitly, ExpandableObject]
            public RangeFloat MountHitPoints { get; set; } = new(1.25f, 2f);
        }

        [Category("Custom Prize"), Description("Custom prize configuration"), PropertyOrder(1), ExpandableObject, UsedImplicitly]
        public CustomPrizeConfig CustomPrize { get; set; } = new();

        public enum PrizeType
        {
            Weapon,
            Armor,
            Mount
        }

        [Browsable(false), YamlIgnore]
        public IEnumerable<(PrizeType type, float weight)> PrizeTypeWeights {
            get
            {
                yield return (type: PrizeType.Weapon, weight: PrizeWeaponWeight);
                yield return (type: PrizeType.Armor, weight: PrizeArmorWeight);
                yield return (type: PrizeType.Mount, weight: PrizeMountWeight);
            }
        }
    }

    internal class GlobalHeroClassConfig : IConfig
    {
        private const string ID = "Adopt A Hero - Class Config";
        internal static void Register() => ActionManager.RegisterGlobalConfigType(ID, typeof(GlobalHeroClassConfig));
        internal static GlobalHeroClassConfig Get() => ActionManager.GetGlobalConfig<GlobalHeroClassConfig>(ID);
        
        [Description("Defined classes"), UsedImplicitly] 
        public List<HeroClassDef> ClassDefs { get; set; } = new();

        [Browsable(false), YamlIgnore]
        public IEnumerable<string> ClassNames => ClassDefs?.Select(c => c.Name?.ToLower()) ?? Enumerable.Empty<string>();

        public HeroClassDef GetClass(Guid id) 
            => ClassDefs?.FirstOrDefault(c => c.ID == id);

        public HeroClassDef FindClass(string search) 
            => ClassDefs?.FirstOrDefault(c => c.Name.Equals(search, StringComparison.InvariantCultureIgnoreCase));

        // public HeroClassDef FindClosestClass(Hero hero) =>
        //     // Find class that has maximum sum of matched skills
        //     ClassDefs.OrderByDescending(c
        //             // Sum the heroes skill values for all the class skills
        //             => c.Skills.Sum(s => hero.GetSkillValue(SkillGroup.GetSkill(s.Skill))))
        //         .FirstOrDefault();

        // This is just a copy of the classes that existed on loading, so we can assign unique IDs to any new ones when
        // we save
        private List<HeroClassDef> classesOnLoad;
        public void OnLoaded()
        {
            foreach (var c in ClassDefs
                .GroupBy(c => c.ID)
                .SelectMany(g => g.Skip(1)))
            {
                c.ID = Guid.NewGuid();
            }
            classesOnLoad = ClassDefs.ToList();
        }

        public void OnSaving()
        {
            // Assign unique IDs to new class definitions
            foreach (var classDef in ClassDefs.Except(classesOnLoad))
            {
                classDef.ID = Guid.NewGuid();
            }
        }

        public void OnEditing()
        {
            HeroClassDef.ItemSource.ActiveList = this.ClassDefs;
        }
    }
    
    // We could do this, but they could also gain money so...
    // public static class Patches
    // {
    //     [HarmonyPrefix]
    //     [HarmonyPatch(typeof(Hero), nameof(Hero.Gold), MethodType.Setter)]
    //     public static bool set_GoldPrefix(Hero __instance, int value)
    //     {
    //         // Don't allow changing gold of our adopted heroes, as we use it ourselves
    //         return !__instance.GetName().Contains(AdoptAHero.Tag);
    //     }
    // }
}