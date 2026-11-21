using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

[assembly: AssemblyMetadata(ModSettings.Glyphs.NameKey, "modify variant")]

namespace ModSettings
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "sparr.messagefromaliens.modsettings";
        public const string Name = "Mod Settings";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private bool failed;

        // Plugin objects must not add components or start coroutines from Awake, which runs
        // during Application's static constructor; Unity crashes. LateUpdate is safe.
        private void LateUpdate()
        {
            try
            {
                SettingsPages.Tick();
                UnlockMessages.Tick();
            }
            catch (Exception e)
            {
                if (!failed)
                {
                    failed = true;
                    Log.LogError($"LateUpdate failed: {e}");
                }
            }
        }
    }
}
