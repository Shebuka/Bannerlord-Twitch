﻿using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordTwitch.Util;
using BLTAdoptAHero.Annotations;
using BLTAdoptAHero.Util;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace BLTAdoptAHero.Achievements
{
    public class AchievementStatsData
    {
        public enum Statistic
        {
            None,
            TotalKills,
            TotalHeroKills,
            TotalViewerKills,
            TotalStreamerKills,
            TotalMountKills,
            TotalDeaths,
            TotalHeroDeaths,
            TotalViewerDeaths,
            TotalStreamerDeaths,
            TotalMountDeaths,
            Summons,
            Attacks,
            Battles,
            ConsecutiveSummons,
            ConsecutiveAttacks,
            TotalTournamentRoundWins,
            TotalTournamentRoundLosses,
            TotalTournamentFinalWins,
        }

        [SaveableProperty(0)]
        public Dictionary<Statistic, int> TotalStats { get; set; } = new();

        [SaveableProperty(1)]
        public Dictionary<(Guid, Statistic), int> ClassStats { get; set; } = new();

        [SaveableProperty(2)]
        public List<Guid> Achievements { get; set; } = new();

        // Update class and total stats together
        public void UpdateValue(Statistic type, Guid classId, int amount)
        {
            TotalStats.AddInt(type, amount);
            ClassStats.AddInt((classId, type), amount);

            if (type is Statistic.Summons)
            {
                TotalStats.AddInt(Statistic.ConsecutiveSummons, amount);
                ClassStats.AddInt((classId, Statistic.ConsecutiveSummons), amount);
                // Reset consecutive attacks, now that hero summoned
                TotalStats[Statistic.ConsecutiveAttacks] = 0;
                ClassStats[(classId, Statistic.ConsecutiveAttacks)] = 0;
            }
            else if (type is Statistic.Attacks)
            {
                TotalStats.AddInt(Statistic.ConsecutiveAttacks, amount);
                ClassStats.AddInt((classId, Statistic.ConsecutiveAttacks), amount);
                // Reset consecutive summons, now that hero attacked
                TotalStats[Statistic.ConsecutiveSummons] = 0;
                ClassStats[(classId, Statistic.ConsecutiveSummons)] = 0;
            }
            if (type is Statistic.Summons or Statistic.Attacks)
            {
                TotalStats.AddInt(Statistic.Battles, amount);
                ClassStats.AddInt((classId, Statistic.Battles), amount);
            }
        }

        public int GetTotalValue(Statistic type) => TotalStats.GetInt(type);

        public int GetClassValue(Statistic type, Guid classId) => ClassStats.GetInt((classId, type));
    }
}