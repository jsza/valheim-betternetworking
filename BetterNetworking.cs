﻿using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Steamworks;

using System.ComponentModel;
using System.Runtime.InteropServices;
using System;

namespace CW_Jesse.BetterNetworking {

    [BepInPlugin("CW_Jesse.BetterNetworking", "Better Networking", "0.6.2")]
    [BepInProcess("valheim.exe")]
    public class BetterNetworking : BaseUnityPlugin {

        private readonly Harmony harmony = new Harmony("CW_Jesse.BetterNetworking");

        public static ConfigEntry<bool> configLogMessages;
        public static ConfigEntry<BN_Patch_UpdateRate.Options_NetworkUpdateRates> configNetworkUpdateRate;

        private static ConfigEntry<Options_NetworkSendRate> configNetworkSendRateMin;
        private static ConfigEntry<Options_NetworkSendRate> configNetworkSendRateMax;
        private enum Options_NetworkSendRate {
            [Description("400% (600 KB/s | 4.8 Mbit/s)")]
            _400,
            [Description("200% (300 KB/s | 2.4 Mbit/s)")]
            _200,
            [Description("100% (150 KB/s | 1.2 Mbit/s)")]
            _100,
            [Description("50% (75 KB/s | 0.6 Mbit/s)")]
            _50
        }

        private static ConfigEntry<Options_NetworkQueueSize> configNetworkQueueSize;
        private enum Options_NetworkQueueSize {
            [Description("300% (30 KB)")]
            _300,
            [Description("200% (20 KB)")]
            _200,
            [Description("150% (15 KB)")]
            _150,
            [Description("100% (10 KB)")]
            _100,
            [Description("80% (8 KB)")]
            _80,
            [Description("60% (6 KB)")]
            _60,
        }

        void Awake() {
            BN_Logger.logger = base.Logger;
            configLogMessages = Config.Bind(
                "Logging",
                "Log Info Messages",
                false,
                "True: Verbose logs.\nFalse: Only log warnings and errors.");

            BN_Patch_UpdateRate.InitConfig(Config);

            configNetworkSendRateMin = Config.Bind(
                "Networking",
                "Minimum Send Rate",
                Options_NetworkSendRate._100,
                new ConfigDescription(
                    "Steam attempts to estimate your bandwidth. Valheim sets the MINIMUM estimation at 150 KB/s as of patch 0.203.11."
                ));

            configNetworkSendRateMax = Config.Bind(
                "Networking",
                "Maximum Send Rate",
                Options_NetworkSendRate._100,
                new ConfigDescription(
                    "Steam attempts to estimate your bandwidth. Valheim sets the MAXIMUM estimation at 150 KB/s as of patch 0.203.11."
                ));

            configNetworkQueueSize = Config.Bind(
                "Networking",
                "Queue Size",
                Options_NetworkQueueSize._100,
                new ConfigDescription(
                    "With low upload speeds, lowering your queue size allows Valheim to better prioritize outgoing data. Listed values are correct as of patch 0.203.11."
                ));

            harmony.PatchAll();

            configNetworkSendRateSettings_Listen();
            }

        public static void configNetworkSendRateSettings_Listen() {
            configNetworkSendRateMin.SettingChanged += ConfigNetworkSendRateMin_SettingChanged;
            configNetworkSendRateMax.SettingChanged += ConfigNetworkSendRateMax_SettingChanged;
            BN_Logger.LogInfo("Started listening for user changes to NetworkSendRates");
        }

        private static void ConfigNetworkSendRateMin_SettingChanged(object sender, EventArgs e) {
            if (configNetworkSendRateMin.Value < configNetworkSendRateMax.Value) {
                configNetworkSendRateMax.Value = configNetworkSendRateMin.Value;
                BN_Logger.logger.LogInfo("Maximum network send rate automatically increased");
            }
            NetworkSendRate_Patch.SetSendRateMinFromConfig();
        }
        private static void ConfigNetworkSendRateMax_SettingChanged(object sender, EventArgs e) {
            if (configNetworkSendRateMax.Value > configNetworkSendRateMin.Value) {
                configNetworkSendRateMin.Value = configNetworkSendRateMax.Value;
                BN_Logger.logger.LogInfo("Minimum network send rate automatically decreased");
            }
            NetworkSendRate_Patch.SetSendRateMaxFromConfig();
        }

        [HarmonyPatch(typeof(SteamNetworkingUtils))]
        class NetworkSendRate_Patch {
            static private int originalNetworkSendRateMin = 0;
            static private bool originalNetworkSendRateMin_set = false;
            static private int originalNetworkSendRateMax = 0;
            static private bool originalNetworkSendRateMax_set = false;


            public static int sendRateMin {
                get {
                    switch (configNetworkSendRateMin.Value) {
                        case Options_NetworkSendRate._400:
                            return originalNetworkSendRateMin * 4;
                        case Options_NetworkSendRate._200:
                            return originalNetworkSendRateMin * 2;
                        case Options_NetworkSendRate._50:
                            return originalNetworkSendRateMin / 2;
                    }
                    return originalNetworkSendRateMin;
                }
            }
            public static int sendRateMax {
                get {
                    switch (configNetworkSendRateMax.Value) {
                        case Options_NetworkSendRate._400:
                            return originalNetworkSendRateMax * 4;
                        case Options_NetworkSendRate._200:
                            return originalNetworkSendRateMax * 2;
                        case Options_NetworkSendRate._50:
                            return originalNetworkSendRateMax / 2;
                    }
                    return originalNetworkSendRateMax;
                }
            }

            public static void SetSendRateMinFromConfig() {
                if (!originalNetworkSendRateMin_set) {
                    BN_Logger.LogWarning("Attempted to set NetworkSendRateMin before Valheim did");
                    return;
                }

                BN_Logger.LogMessage($"Setting NetworkSendRateMin to {sendRateMin}");
                SetSteamNetworkConfig(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, sendRateMin);
            }
            public static void SetSendRateMaxFromConfig() {
                if (!originalNetworkSendRateMax_set) {
                    BN_Logger.LogWarning("Attempted to set NetworkSendRateMax before Valheim did");
                    return;
                }

                BN_Logger.LogMessage($"Setting NetworkSendRateMax to {sendRateMax}");
                SetSteamNetworkConfig(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, sendRateMax);
            }

            private static void SetSteamNetworkConfig(ESteamNetworkingConfigValue valueType, int value) {
                if (ZNet.instance == null) {
                    BN_Logger.LogWarning("Attempted to set Steam networking config value while disconnected");
                    return;
                }

                GCHandle pinned_SendRate = GCHandle.Alloc(value, GCHandleType.Pinned);

                try {
                    if (ZNet.instance.IsDedicated()) {
                        BN_Logger.LogInfo("(dedicated server)");

                        SteamGameServerNetworkingUtils.SetConfigValue(
                            valueType,
                            ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                            IntPtr.Zero,
                            ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                            pinned_SendRate.AddrOfPinnedObject()
                            );
                    } else {
                        BN_Logger.LogInfo("(non-dedicated server)");

                        SteamNetworkingUtils.SetConfigValue(
                            valueType,
                            ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                            IntPtr.Zero,
                            ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                            pinned_SendRate.AddrOfPinnedObject()
                            );
                    }
                } catch {
                    BN_Logger.LogError("Unable to set networking config; please notify the mod author");
                }

                pinned_SendRate.Free();
            }

            [HarmonyPatch(nameof(SteamNetworkingUtils.SetConfigValue))]
            static void Prefix(
                ESteamNetworkingConfigValue eValue,
                ESteamNetworkingConfigScope eScopeType,
                IntPtr scopeObj,
                ESteamNetworkingConfigDataType eDataType,
                ref IntPtr pArg) {

                if (eScopeType == ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global &&
                    scopeObj == IntPtr.Zero &&
                    eDataType == ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32) {

                    switch (eValue) {
                        case ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin:
                            if (!originalNetworkSendRateMin_set) {
                                originalNetworkSendRateMin_set = true;
                                originalNetworkSendRateMin = Marshal.ReadInt32(pArg);

                                BN_Logger.LogMessage($"Valheim's default NetworkSendRateMin is {originalNetworkSendRateMin}");
                            }
                            break;
                        case ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax:
                            if (!originalNetworkSendRateMax_set) {
                                originalNetworkSendRateMax_set = true;
                                originalNetworkSendRateMax = Marshal.ReadInt32(pArg);

                                BN_Logger.LogMessage($"Valheim's default NetworkSendRateMax is {originalNetworkSendRateMin}");
                            }
                            break;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ZSteamSocket), "RegisterGlobalCallbacks")]
        class PreventValheimControlOfNetworkRate_Patch {

            static void Postfix() {
                BN_Logger.LogInfo("Network settings overwritten by Valheim; setting them to Better Networking values");

                NetworkSendRate_Patch.SetSendRateMinFromConfig();
                NetworkSendRate_Patch.SetSendRateMaxFromConfig();
            }
        }

        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.GetSendQueueSize))]
        class NetworkQueueSize_Patch {
            static void Postfix(ref int __result) {
#if DEBUG
                int originalQueueSize = __result;
#endif

                switch (configNetworkQueueSize.Value) {
                    case Options_NetworkQueueSize._300:
                        __result /= 3;
                        break;
                    case Options_NetworkQueueSize._200:
                        __result /= 2;
                        break;
                    case Options_NetworkQueueSize._150:
                        __result = (int)(__result / 1.5);
                        break;
                    case Options_NetworkQueueSize._80:
                        __result = (int)(__result / 0.8);
                        break;
                    case Options_NetworkQueueSize._60:
                        __result = (int)(__result / 0.6);
                        break;
                }

#if DEBUG
                BN_Logger.LogInfo($"Queue size reported as {__result} instead of {originalQueueSize}");
#endif
            }
        }
    }
}
