using HarmonyLib;

namespace ModSettings
{
    // Global.GetWin checks every gate token the game draws, and Mod Settings and other mods call it
    // too, so a token that combines gates works wherever a gate does: menu and explanation lines,
    // content packs' text, settings, and unlock messages. Each term goes through Global.GetWin
    // again, with every mod's patches. Without Mod Settings, the game reads such a token as a page
    // name no page has, so the line stays hidden.
    internal static class CompoundGates
    {
        [HarmonyPatch(typeof(Global), nameof(Global.GetWin))]
        private static class GetWinPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(string s, ref bool __result)
            {
                if (!GateExpr.IsCompound(s))
                {
                    return true;
                }
                __result = GateExpr.Evaluate(s, Global.GetWin);
                return false;
            }
        }
    }
}
