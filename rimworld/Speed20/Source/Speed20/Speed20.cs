using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Speed20;

public sealed class SpeedState : GameComponent
{
    public bool Selected;
    public SpeedState(Game game) { }
    public override void ExposeData() => Scribe_Values.Look(ref Selected, "speed20Selected");
    public static SpeedState Current => Verse.Current.Game?.GetComponent<SpeedState>();
}

[StaticConstructorOnStartup]
public static class Startup
{
    static Startup() => new Harmony("gojohnlee.speed20").PatchAll();
}

[HarmonyPatch(typeof(TickManager), nameof(TickManager.CurTimeSpeed), MethodType.Setter)]
public static class SelectOrdinarySpeed
{
    // Explicit speed selection restores the original Smart Speed tier.
    // TogglePaused changes the backing field, preserving this selection on resume.
    static void Postfix()
    {
        if (SpeedState.Current is { } state) state.Selected = false;
    }
}

[HarmonyPatch(typeof(TickManager), nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
public static class ExtraRate
{
    private static readonly FieldInfo EventSetting = AccessTools.Field(
        AccessTools.TypeByName("SmartSpeed.SmartSpeed_Settings"), "currSetting");

    [HarmonyAfter("com.smartspeed")]
    static void Postfix(TickManager __instance, ref float __result)
    {
        if (SpeedState.Current?.Selected != true) return;
        if (__instance.CurTimeSpeed == TimeSpeed.Paused)
        {
            __result = 0f;
            return;
        }
        if (__instance.CurTimeSpeed != TimeSpeed.Ultrafast) return;
        if (!__instance.slower.ForcedNormalSpeed)
            __result = 20f;
        else
        {
            // Respect Smart Speed's event slowdown policy, including Half/Ignore.
            string policy = EventSetting?.GetValue(null)?.ToString();
            if (policy == "Half") __result = 10f;
            else if (policy == "Ignore") __result = 20f;
        }
    }
}

[HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
public static class ExtraButton
{
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("com.smartspeed")]
    static void Prefix(Rect timerRect, out Rect __state)
        => __state = new Rect(timerRect.x - 78f, timerRect.y, 60f, 24f);

    static void Postfix(Rect __state)
    {
        var state = SpeedState.Current;
        var manager = Find.TickManager;
        if (state == null || manager == null) return;
        bool selected = state.Selected && manager.CurTimeSpeed == TimeSpeed.Ultrafast;
        var oldColor = GUI.color;
        if (selected && !manager.ForcePaused) GUI.color = new Color(0.5f, 1f, 0.65f);
        bool clicked = Widgets.ButtonText(__state, "20x");
        GUI.color = oldColor;
        TooltipHandler.TipRegion(__state, "20x = 1200 ticks/sec (target). Smart Speed event slowdown still applies.");
        if (clicked && !manager.ForcePaused)
        {
            manager.CurTimeSpeed = TimeSpeed.Ultrafast;
            if (manager.CurTimeSpeed == TimeSpeed.Ultrafast)
            {
                state.Selected = true;
                SoundDefOf.Clock_Superfast.PlayOneShotOnCamera();
            }
        }
        GenUI.AbsorbClicksInRect(__state);
    }
}
