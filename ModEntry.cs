using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

[ModInitializer("Initialize")]
public class ModEntry
{
    public static void Initialize()
    {
        var harmony = new Harmony("pathplanner.patch");
        harmony.PatchAll();
    }
}
