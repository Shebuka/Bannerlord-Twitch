﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BannerlordTwitch.Rewards;
using BannerlordTwitch.Testing;
using BannerlordTwitch.Util;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.ChannelPoints.GetCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;
using TwitchLib.PubSub;
using TwitchLib.PubSub.Events;

namespace BannerlordTwitch
{
    // https://twitchtokengenerator.com/
    // https://twitchtokengenerator.com/quick/AAYotwZPvU
    internal partial class TwitchService
    {
        private TwitchPubSub pubSub;
        private readonly TwitchAPI api;
        private string channelId;
        private readonly AuthSettings authSettings;

        private Settings Settings { get; set; }

        private readonly ConcurrentDictionary<Guid, OnRewardRedeemedArgs> redemptionCache = new();
        private Bot bot;

        public TwitchService()
        {
            if (!LoadSettings())
            {
                return;
            }

            try
            {
                authSettings = AuthSettings.Load();
            }
            catch(Exception e)
            {
                Log.Error(e.ToString());
            }
            if (authSettings == null)
            {
                InformationManager.ShowInquiry(
                    new InquiryData(
                        "Bannerlord Twitch MOD DISABLED",
                        $"Failed to load auth settings, enable the BLTConfigure module and authorize the mod via the window",
                        true, false, "Okay", null,
                        () => {}, () => {}), true);
                Log.ScreenCritical($"Failed to load auth settings, load the BLTConfigure module and authorize the mod via the window");
                return;
            }

            api = new TwitchAPI();
            //api.Settings.Secret = SECRET;
            api.Settings.ClientId = authSettings.ClientID;
            api.Settings.AccessToken = authSettings.AccessToken;

            api.Helix.Users.GetUsersAsync(accessToken: authSettings.AccessToken).ContinueWith(t =>
            {
                MainThreadSync.Run(() =>
                {
                    if (t.IsFaulted)
                    {
                        Log.ScreenFail($"Service init failed: {t.Exception?.Message}");
                        return;
                    }
                    
                    var user = t.Result.Users.First();

                    Log.Info($"Channel ID is {user.Id}");
                    channelId = user.Id;
                    
                    // Connect the chatbot
                    bot = new Bot(user.Login, authSettings, this);

                    if (string.IsNullOrEmpty(user.BroadcasterType))
                    {
                        Log.ScreenFail($"Service init failed: you must be a twitch partner or affiliate to use the channel points system. Chat bot and testing are still functioning.");
                        return;
                    }
                    
                    // Create new instance of PubSub Client
                    pubSub = new TwitchPubSub();

                    // Subscribe to Events
                    //_pubSub.OnWhisper += OnWhisper;
                    pubSub.OnPubSubServiceConnected += OnPubSubServiceConnected;
                    pubSub.OnRewardRedeemed += OnRewardRedeemed;
                    pubSub.OnLog += (sender, args) =>
                    {
                        if (args.Data.Contains("PONG")) return;
                        try
                        {
                            Log.Trace(args.Data);
                        }
                        catch
                        {
                            // ignored
                        }
                    };

                    // pubSub.OnPubSubServiceClosed += OnOnPubSubServiceClosed;

                    RegisterRewardsAsync();

                    // Connect
                    pubSub.Connect();
                });
            });
        }

        public bool LoadSettings()
        {
            try
            {
                Settings = Settings.Load();
                return true;
            }
            catch
            {
                // ignored
            }

            InformationManager.ShowInquiry(
                new InquiryData(
                    "Bannerlord Twitch MOD DISABLED",
                    $"Failed to load action/command settings, please enable the BLTConfigure module and use it to configure the mod",
                    true, false, "Okay", null,
                    () => {}, () => {}), true);
            Log.ScreenCritical($"MOD DISABLED: Failed to load settings from settings file, please enable the BLTConfigure module and use it to configure the mod");

            return false;
        }
        
        public void Exit()
        {
            if (Settings.DeleteRewardsOnExit)
            {
                RemoveRewards();
            }
            Log.Info($"Exiting");
        }
        
        private async void RegisterRewardsAsync()
        {
            var db = Db.Load();
            
            GetCustomRewardsResponse existingRewards = null;
            try
            {
                existingRewards = await api.Helix.ChannelPoints.GetCustomReward(channelId, accessToken: authSettings.AccessToken);
            }
            catch (Exception e)
            {
                Log.ScreenFail($"ERROR: Couldn't retrieve existing rewards: {e.Message}");
            }

            bool anyFailed = false;
            foreach (var rewardDef in Settings.Rewards.Where(r => existingRewards == null || existingRewards.Data.All(e => e.Title != r.RewardSpec?.Title)))
            {
                try
                {
                    var createdReward = (await api.Helix.ChannelPoints.CreateCustomRewards(channelId, rewardDef.RewardSpec.GetTwitchSpec(), authSettings.AccessToken)).Data.First();
                    Log.Info($"Created reward {createdReward.Title} ({createdReward.Id})");
                    db.RewardsCreated.Add(createdReward.Id);
                }
                catch (Exception e)
                {
                    Log.ScreenCritical($"Couldn't create reward {rewardDef.RewardSpec.Title}: {e.Message}");
                    anyFailed = true;
                }
            }

            if (anyFailed)
            {
                InformationManager.ShowInquiry(
                    new InquiryData(
                        "Bannerlord Twitch",
                        $"Failed to create some of the channel rewards, please check the logs for details!",
                        true, true, "View Log", "Ignore",
                        () =>
                        {
                            string logDir = Path.Combine(Common.PlatformFileHelper.DocumentsPath, "Mount and Blade II Bannerlord", "logs");
                            try
                            {
                                string logFile = Directory.GetFiles(logDir, "rgl_log_*.txt")
                                    .FirstOrDefault(f => !f.Contains("errors"));
                                if (logFile != null)
                                {
                                    // open with default editor
                                    Process.Start(logFile);
                                }
                                else
                                {
                                    Log.ScreenFail($"ERROR: Couldn't find the log file at {logDir}");
                                }
                            }
                            catch
                            {
                                // ignored
                            }
                        }, () => {}), true);
            }
            
            Db.Save(db);
        }

        private void RemoveRewards()
        {
            var db = Db.Load();
            foreach (string rewardId in db.RewardsCreated.ToList())
            {
                try
                {
                    api.Helix.ChannelPoints.DeleteCustomReward(channelId, rewardId, accessToken: authSettings.AccessToken).Wait();
                    Log.Info($"Removed reward {rewardId}");
                    db.RewardsCreated.Remove(rewardId);
                }
                catch (Exception e)
                {
                    Log.Info($"Couldn't remove reward {rewardId}: {e.Message}");
                }
            }
            Db.Save(db);
        }

        private void OnRewardRedeemed(object sender, OnRewardRedeemedArgs redeemedArgs)
        {
            MainThreadSync.Run(() =>
            {
                var reward = Settings.Rewards.FirstOrDefault(r => r.RewardSpec.Title == redeemedArgs.RewardTitle);
                if (reward == null)
                {
                    Log.Info($"Reward {redeemedArgs.RewardTitle} not owned by this extension, ignoring it");
                    // We don't cancel redemptions we don't know about!
                    // RedemptionCancelled(e.RedemptionId, $"Reward {e.RewardTitle} not found");
                    return;
                }

                if (redeemedArgs.Status != "UNFULFILLED")
                {
                    Log.Info($"Reward {redeemedArgs.RewardTitle} status {redeemedArgs.Status} is not interesting, ignoring it");
                    return;
                }

                try
                {
                    redemptionCache.TryAdd(redeemedArgs.RedemptionId, redeemedArgs);
                    if (!RewardManager.Enqueue(reward.Action, redeemedArgs.RedemptionId, redeemedArgs.Message, redeemedArgs.DisplayName, reward.ActionConfig))
                    {
                        Log.Error($"Couldn't enqueue redemption {redeemedArgs.RedemptionId}: RedemptionAction {reward.Action} not found, check you have its Reward extension installed!");
                        // We DO cancel redemptions we know about, where the implementation is missing
                        RedemptionCancelled(redeemedArgs.RedemptionId, $"Redemption action {reward.Action} wasn't found");
                    }
                    else
                    {
                        Log.Screen($"Redemption of {redeemedArgs.RewardTitle} from {redeemedArgs.DisplayName} received!");
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"Exception happened while trying to enqueue redemption {redeemedArgs.RedemptionId}: {e.Message}");
                    RedemptionCancelled(redeemedArgs.RedemptionId, $"Exception occurred: {e.Message}");
                }
            });
        }

        public void TestRedeem(string rewardName, string user, string message)
        {
            var reward = Settings?.Rewards.FirstOrDefault(r => r.RewardSpec.Title == rewardName);
            if (reward == null)
            {
                Log.Error($"Reward {rewardName} not found!");
                return;
            }

            var guid = Guid.NewGuid();
            redemptionCache.TryAdd(guid, new OnRewardRedeemedArgs
            {
                RedemptionId = guid,
                Message = message,
                DisplayName = user,
                Login = user,
                RewardTitle = rewardName,
                ChannelId = null,
            });

            RewardManager.Enqueue(reward.Action, guid, message, user, reward.ActionConfig);
        }

        private void ShowMessage(string screenMsg, string botMsg, string userToAt)
        {
            Log.Screen(screenMsg);
            SendChat($"@{userToAt}: {botMsg}");
        }

        private void ShowMessageFail(string screenMsg, string botMsg, string userToAt)
        {
            Log.ScreenFail(screenMsg);
            SendChat($"@{userToAt}: {botMsg}");
        }

        public void SendChat(params string[] message)
        {
            Log.Trace($"[chat] {string.Join(" - ", message)}");
            bot.SendChat(message);
        }

        public void SendReply(string replyId, params string[] message)
        {
            Log.Trace($"[chat] {replyId}->{string.Join(" - ", message)}");
            bot.SendReply(replyId, message);
        }

        private void ShowCommandHelp(string replyId)
        {
            bot.SendReply(replyId, "Commands: ".Yield()
                .Concat(Settings.Commands.Where(c 
                        => !c.HideHelp && !c.BroadcasterOnly && !c.ModOnly)
                    .Select(c => $"!{c.Name} - {c.Help}")
                ).ToArray());
        }
        
        public void RedemptionComplete(Guid redemptionId, string info = null)
        {
            if (!redemptionCache.TryRemove(redemptionId, out var redemption))
            {
                Log.Error($"RedemptionComplete failed: redemption {redemptionId} not known!");
                return;
            }
            ShowMessage($"Redemption of {redemption.RewardTitle} for {redemption.DisplayName} complete" + 
                        (!string.IsNullOrEmpty(info) ? $": {info}" : ""), info, redemption.Login);
            if (!string.IsNullOrEmpty(redemption.ChannelId))
            {
                SetRedemptionStatusAsync(redemption, CustomRewardRedemptionStatus.FULFILLED);
            }
            else
            {
                Log.Trace($"(skipped setting redemption status for test redemption)");
            }
        }

        public void RedemptionCancelled(Guid redemptionId, string reason = null)
        {
            if (!redemptionCache.TryRemove(redemptionId, out var redemption))
            {
                Log.Error($"RedemptionCancelled failed: redemption {redemptionId} not known!");
                return;
            }
            ShowMessageFail($"Redemption of {redemption.RewardTitle} for {redemption.DisplayName} cancelled" + 
                            (!string.IsNullOrEmpty(reason) ? $": {reason}" : ""), reason, redemption.Login);
            if (!string.IsNullOrEmpty(redemption.ChannelId))
            {
                SetRedemptionStatusAsync(redemption, CustomRewardRedemptionStatus.CANCELED);
            }
            else
            {
                Log.Trace($"(skipped setting redemption status for test redemption)");
            }
        }

        private async void SetRedemptionStatusAsync(OnRewardRedeemedArgs redemption, CustomRewardRedemptionStatus status)
        {
            try
            {
                await api.Helix.ChannelPoints.UpdateCustomRewardRedemptionStatus(
                    redemption.ChannelId,
                    redemption.RewardId.ToString(),
                    new List<string> {redemption.RedemptionId.ToString()},
                    new UpdateCustomRewardRedemptionStatusRequest {Status = status},
                    authSettings.AccessToken
                );
                Log.Info($"Set redemption status of {redemption.RedemptionId} ({redemption.RewardTitle} for {redemption.DisplayName}) to {status}");
            }
            catch (Exception e)
            {
                Log.Error($"Failed to set redemption status of {redemption.RedemptionId} ({redemption.RewardTitle} for {redemption.DisplayName}) to {status}: {e.Message}");
            }
        }

        private void OnPubSubServiceConnected(object sender, System.EventArgs e)
        {
            Log.Screen("PubSub Service connected, now listening for rewards");

#pragma warning disable 618
            // Obsolete warning disabled because no new version has yet been written!
            pubSub.ListenToRewards(channelId);
#pragma warning restore 618
            pubSub.SendTopics(authSettings.AccessToken);
        }
        
        // private void OnOnPubSubServiceClosed(object sender, EventArgs e)
        // {
        //     Log.ScreenFail("PubSub Service closed, attempting reconnect...");
        //     pubSub.Connect();
        // }
        
        public object FindGlobalConfig(string id) => Settings?.GlobalConfigs?.FirstOrDefault(c => c.Id == id)?.Config;

        private static SimulationTest simTest;
        
        public void StartSim()
        {
            StopSim();
            simTest = new SimulationTest(Settings);
        }
        
        public void StopSim()
        {
            simTest?.Stop();
            simTest = null;
        }
    }
}