using HarmonyLib;
using Verse;

namespace RimWorldAgent
{
    [StaticConstructorOnStartup]
    internal static class Hook_Bootstrap
    {
        static Hook_Bootstrap()
        {
            new Harmony("RimWorldAgent").PatchAll(typeof(Hook_Bootstrap).Assembly);
        }
    }
}
