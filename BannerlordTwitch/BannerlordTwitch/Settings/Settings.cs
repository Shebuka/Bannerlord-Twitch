﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BannerlordTwitch.Rewards;
using BannerlordTwitch.Util;
using TaleWorlds.Library;
using YamlDotNet.Serialization;

// ReSharper disable MemberCanBePrivate.Global
#pragma warning disable 649

namespace BannerlordTwitch
{
    // Docs here https://dev.twitch.tv/docs/api/reference#create-custom-rewards

    public class Settings : IDocumentable, IUpdateFromDefault
    {
        public List<Reward> Rewards { get; set; } = new ();
        [YamlIgnore]
        public IEnumerable<Reward> EnabledRewards => Rewards.Where(r => r.Enabled);
        public List<Command> Commands { get; set; } = new ();
        [YamlIgnore]
        public IEnumerable<Command> EnabledCommands => Commands.Where(r => r.Enabled);
        public List<GlobalConfig> GlobalConfigs { get; set; } = new ();
        public SimTestingConfig SimTesting { get; set; }
        [YamlIgnore, Browsable(false)]
        public IEnumerable<ActionBase> AllActions => Rewards.Cast<ActionBase>().Concat(Commands);

        public bool DisableAutomaticFulfillment { get; set; }
        
        public Command GetCommand(string id) => EnabledCommands.FirstOrDefault(c =>
            string.Equals(c.Name, id, StringComparison.CurrentCultureIgnoreCase));

        [YamlIgnore]
        public IEnumerable<ILoaded> ConfigInterfaces => AllActions
            .Select(c => c.HandlerConfig)
            .Concat(GlobalConfigs.Select(g => g.Config))
            .OfType<ILoaded>();

        public T GetGlobalConfig<T>(string id) => (T)GlobalConfigs.First(c => c.Id == id).Config;

        private static string DefaultSettingsFileName 
            => Path.Combine(Path.GetDirectoryName(typeof(Settings).Assembly.Location) ?? ".", 
                "..", "..", "Bannerlord-Twitch-v2.yaml");
        
        public static Settings DefaultSettings { get; private set; }
        
        #if DEBUG
        private static string ProjectRootDir([CallerFilePath]string file = "") => Path.Combine(Path.GetDirectoryName(file) ?? ".", "..");
        private static string SaveFilePath => Path.Combine(ProjectRootDir(), "Bannerlord-Twitch-v2.yaml");
        public static Settings Load()
        {
            LoadDefaultSettings();
            
            var settings = YamlHelpers.Deserialize<Settings>(File.ReadAllText(SaveFilePath));
            if (settings == null)
                throw new Exception($"Couldn't load the mod settings from {SaveFilePath}");

            SettingsPostLoad(settings);

            return settings;
        }

        public static void Save(Settings settings)
        {
            SettingsPreSave(settings);
            File.WriteAllText(SaveFilePath, YamlHelpers.Serialize(settings));
        }

        #else
        private static PlatformFilePath SaveFilePath => FileSystem.GetConfigPath("Bannerlord-Twitch-v2.yaml");

        public static Settings Load()
        {
            LoadDefaultSettings();

            var settings = FileSystem.FileExists(SaveFilePath)
                ? YamlHelpers.Deserialize<Settings>(FileSystem.GetFileContentString(SaveFilePath))
                : YamlHelpers.Deserialize<Settings>(File.ReadAllText(DefaultSettingsFileName))
                ;

            if (settings == null)
                throw new Exception($"Couldn't load the mod settings from {SaveFilePath}");

            SettingsPostLoad(settings);

            SettingsHelpers.CallInDepth<IUpdateFromDefault>(settings, 
                config => config.OnUpdateFromDefault(DefaultSettings));

            return settings;
        }

        public static void Save(Settings settings)
        {
            SettingsPreSave(settings);
            FileSystem.SaveFileString(SaveFilePath, YamlHelpers.Serialize(settings));
        }
        #endif

        private static void LoadDefaultSettings()
        {
            if (DefaultSettings == null)
            {
                DefaultSettings = YamlHelpers.Deserialize<Settings>(File.ReadAllText(DefaultSettingsFileName));
                if (DefaultSettings == null)
                {
                    throw new Exception($"Couldn't load the mod default settings from {DefaultSettingsFileName}");
                }
                SettingsPostLoad(DefaultSettings);
            }
        }
        
        private static void SettingsPostLoad(Settings settings)
        {
            settings.Commands ??= new();
            settings.Rewards ??= new();
            settings.GlobalConfigs ??= new();
            settings.SimTesting ??= new();
            
            ActionManager.ConvertSettings(settings.Commands);
            ActionManager.ConvertSettings(settings.Rewards);
            ActionManager.EnsureGlobalSettings(settings.GlobalConfigs);

            SettingsHelpers.CallInDepth<ILoaded>(settings, config => config.OnLoaded(settings));
        }

        private static void SettingsPreSave(Settings settings)
        {
            SettingsHelpers.CallInDepth<ISaving>(settings, config => config.OnSaving());
        }

        public void GenerateDocumentation(IDocumentationGenerator generator)
        {
            generator.Div("commands", () =>
            {
                generator.H1("Commands");
                generator.Table(() => {
                    generator.TR(() => generator.TH("Command").TH("Description").TH("Settings"));
                    foreach (var d in Commands.Where(c => c.Enabled))
                    {
                        generator.TR(() =>
                        {
                            generator.TD(d.Name);
                            generator.TD(string.IsNullOrEmpty(d.Documentation) ? d.Help : d.Documentation);
                            generator.TD(() =>
                            {
                                if (d.HandlerConfig is IDocumentable doc)
                                {
                                    doc.GenerateDocumentation(generator);
                                }
                                else if (d.HandlerConfig != null)
                                {
                                    DocumentationHelpers.AutoDocument(generator, d.HandlerConfig);
                                }
                            });
                        });
                    }
                });
            });
            generator.Br();
            generator.Div("rewards", () =>
            {
                generator.H1("Channel Point Rewards");
                generator.Table(() => {
                    generator.TR(() => generator.TH("Command").TH("Description").TH("Settings"));
                    foreach (var r in Rewards.Where(r => r.Enabled))
                    {
                        generator.TR(() =>
                        {
                            generator.TD(r.RewardSpec.Title);
                            generator.TD(string.IsNullOrEmpty(r.Documentation) ? r.RewardSpec.Prompt : r.Documentation);
                            generator.TD(() =>
                            {
                                if (r.HandlerConfig is IDocumentable doc)
                                {
                                    doc.GenerateDocumentation(generator);
                                }
                                else if (r.HandlerConfig != null)
                                {
                                    DocumentationHelpers.AutoDocument(generator, r.HandlerConfig);
                                }
                            });
                        });
                    }
                });
            });
            generator.Br();
            generator.Div("global-configs", () =>
            {
                foreach (var g in GlobalConfigs.Select(c => c.Config).OfType<IDocumentable>())
                {
                    g.GenerateDocumentation(generator);
                }
            });
        }

        #region IUpdateFromDefault
        public void OnUpdateFromDefault(Settings defaultSettings)
        {
            // merge missing actions / rewards / global configs from template
            SettingsHelpers.MergeCollectionsSorted(
                Commands,
                defaultSettings.Commands,
                (s, s2) => s.ID == s2.ID || s.ToString() == s2.ToString(),
                (a, b) => string.Compare(a.ToString(), b.ToString(), StringComparison.CurrentCulture)
            );
            SettingsHelpers.MergeCollectionsSorted(
                Rewards,
                defaultSettings.Rewards,
                (s, s2) => s.ID == s2.ID || s.ToString() == s2.ToString(),
                (a, b) => string.Compare(a.ToString(), b.ToString(), StringComparison.CurrentCulture)
            );
            SettingsHelpers.MergeCollectionsSorted(
                GlobalConfigs,
                defaultSettings.GlobalConfigs,
                (s, s2) => s.Id == s2.Id,
                (a, b) => string.Compare(a.ToString(), b.ToString(), StringComparison.CurrentCulture)
            );
        }
        #endregion
    }
}
