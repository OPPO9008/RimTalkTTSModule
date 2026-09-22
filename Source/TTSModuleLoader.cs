using HarmonyLib;
using System;
using System.Reflection;
using Verse;

namespace RimTalk.TTS
{
    /// <summary>
    /// Entry point for TTS module - applies Harmony patches to hook into main RimTalk
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TTSModuleLoader
    {
        static TTSModuleLoader()
        {
            try
            {
                TTSLog.Message("[RimTalk.TTS] Initializing TTS Module...");
                
                var harmony = new Harmony("jlibrary.rimtalk.tts");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                
                TTSModule.Instance.Initialize();
                
                // Register application quit handler for proper cleanup
                UnityEngine.Application.quitting += OnApplicationQuitting;
                
                TTSLog.Message("[RimTalk.TTS] TTS Module initialized successfully");
            }
            catch (Exception ex)
            {
                TTSLog.Error($"[RimTalk.TTS] Failed to initialize: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void OnApplicationQuitting()
        {
            try
            {
                TTSLog.Message("[RimTalk.TTS] Application quitting, performing cleanup...");
                TTSModule.Instance.OnGameExit();
            }
            catch (Exception ex)
            {
                TTSLog.Error($"[RimTalk.TTS] Error during application quit: {ex.Message}");
            }
        }
    }
}