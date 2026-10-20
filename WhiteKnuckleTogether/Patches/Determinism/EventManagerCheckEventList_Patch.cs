using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using Random = UnityEngine.Random;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Session events (random fog, toxic fog that makes players cough, enemy drops, greyscale whispers) roll whether they
    /// start when the local player enters a level, subregion or region, and every second while there. In a multiplayer run:
    /// <list type="bullet">
    /// <item>Rolls on entering a place come from that level's own stream (<see cref="WKTContentSeeding.EnterLevelStream"/>), so
    /// every player who enters the same place gets the same outcome there, fog and coughing included, each on their own
    /// screen while they are in it, as in singleplayer. Enemies such an event spawns are keyed from the same stream.</item>
    /// <item>Rolls every second are each player's own scares (whispers, flashes, knocks) and use the plain random, so one
    /// player's checks never shift anyone else's.</item>
    /// </list>
    /// </summary>
    [WKTCriticalPatch(WKTPatchCategory.RunSync), HarmonyPatch(typeof(CL_EventManager), nameof(CL_EventManager.CheckEventList))]
    internal sealed class EventManagerCheckEventList_Patch {
        private static readonly ulong s_salt = WKTStableHash.Of("CL_EventManager.CheckEventList");

        [UsedImplicitly]
        private static void Prefix(List<SessionEvent>? eventList, out WKTRandomStreams<Random.State>.Token __state) {
            __state = default;
            if (eventList == null || eventList.Count == 0 || !WKTContentSeeding.IsActive) return;
            foreach (var sessionEvent in eventList) {
                if (sessionEvent == null || sessionEvent.startCheck == SessionEvent.EventStart.checkEverySecond) continue;
                __state = WKTContentSeeding.EnterLevelStream(CL_EventManager.currentLevel, s_salt);
                return;
            }
        }

        [UsedImplicitly]
        private static void Finalizer(WKTRandomStreams<Random.State>.Token __state) => WKTContentSeeding.Exit(__state);
    }
}
