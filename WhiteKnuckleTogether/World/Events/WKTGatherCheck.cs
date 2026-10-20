using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Whether every living player is together somewhere, for things the group must do together in a synced co-op run: taking
    /// a shortcut, calling the Hadal Diver, going through a door that shuts behind them for good.
    /// </summary>
    internal static class WKTGatherCheck {
        /// <summary>How close to whoever sets it off every living player must be to start something together.</summary>
        public const float RADIUS = 5f;
        /// <summary>How far the one who set something off may walk from it before it is called off.</summary>
        public const float LEAVE_RADIUS = 10f;
        /// <summary>How far outside a gathering zone's box a player still counts as in it, for capsules that straddle its edge.</summary>
        public const float ZONE_MARGIN = 0.5f;
        /// <summary>How far below the Teeth's shaft box still counts as in the shaft: down past its floor.</summary>
        public const float SHAFT_ZONE_BELOW = 13f;
        /// <summary>
        /// How far below the Expulsion Chute's seal box still counts as past its floor hatch: the box floats about 10 m above
        /// the hatch, so this (with <see cref="ZONE_MARGIN"/>) reaches down to just above it.
        /// </summary>
        public const float CHUTE_SEAL_ZONE_BELOW = 9.5f;
        /// <summary>How far from whoever started a shortcut the others are put.</summary>
        public const float PULL_SPOT_RADIUS = 0.6f;
        /// <summary>Players farther than this from whoever started a shortcut are not pulled: they were never part of it.</summary>
        public const float PULL_LIMIT = 2f * RADIUS;
        /// <summary>What the wait panel says while a room's door waits for everyone to be inside (see <see cref="WKTWaitHud"/>).</summary>
        public const string WAIT_IN_ROOM = "Waiting for everyone to get in here";
        /// <summary>What the wait panel says while something waits for everyone to come close.</summary>
        public const string WAIT_NEAR = "Waiting for everyone to come close";
        /// <summary>How close to a listed button (see <see cref="s_gatheredButtons"/>) every living player must be for it to press.</summary>
        public const float BUTTON_RADIUS = 15f;
        private const float c_capsule_radius = 0.3f;
        private const float c_capsule_height = 1.6f;
        private const string c_generate = nameof(UT_BranchGenerator.Generate);
        /// <summary>
        /// Level scripting that changes which levels the route has: loads the levels ahead, drops those behind, deletes other
        /// branches or switches branch (see <see cref="IsLoading"/>). Each machine keeps the levels its living players are in
        /// (see <see cref="WKTHostStreaming"/>), so only what takes levels away for everyone brings anyone over (see
        /// <see cref="GetLoadPull(Type, string)"/>).
        /// </summary>
        private static readonly (Type Target, string Method)[] s_loaders = {
            (typeof(UT_WorldLoaderController), nameof(UT_WorldLoaderController.UnloadPreviousLevels)),
            (typeof(UT_WorldLoaderController), nameof(UT_WorldLoaderController.DeleteOtherBranches)),
            (typeof(UT_WorldLoaderController), nameof(UT_WorldLoaderController.SendToWorldLoader)),
            (typeof(M_Level), nameof(M_Level.ContinueGeneration)),
            (typeof(M_Level), nameof(M_Level.MovePlayerIntoLevelBranch))
        };
        /// <summary>
        /// Level inputs that start something every player must be there for, by level and a part of the input's path. Those
        /// marked <c>InZone</c> wait for every living player to be inside the trigger's own box, the room whose door shuts
        /// behind them; the rest for everyone around whoever set them off.
        /// <list type="bullet">
        /// <item>The Hadal Diver's call, which takes everyone down.</item>
        /// <item>The airlock before the Teeth's shaft, which shuts its first door behind whoever walks in.</item>
        /// <item>The computer room past the airlock, which shuts the airlock's second door behind whoever walks in.</item>
        /// <item>The Teeth's shaft, whose door shuts behind whoever enters and only opens once, from a one-press button.</item>
        /// <item>The service elevator past the Teeth's vent, which falls (and walls off its shaft) once someone steps out of it.</item>
        /// <item>The first bay of the pier, whose door opens as someone walks up to it, shuts once they walk on, and stays shut
        /// once they reach the bay.</item>
        /// <item>The way out of the Tangled Sink's exit room, whose hatch shuts behind whoever reaches the corridor past it.</item>
        /// <item>The Expulsion Chute's way out: its floor hatch seals below whoever climbs well above it, and its shaft's door
        /// shuts behind whoever climbs into the shaft, as before the Teeth.</item>
        /// <item>The Rotten Duct's way out, whose prison doors lock behind whoever walks on past them.</item>
        /// <item>The Hadal Diver's safe area, whose door shuts for good behind whoever walks on past it.</item>
        /// </list>
        /// <c>Event</c>, when set, is the only event of the input that waits. <c>Below</c> is how many meters under a zone's box
        /// still count as in it. The shaft's box floats about 11 m above its floor (the game shuts the door once someone climbs
        /// into it), so players standing in the shaft were never counted. <c>Zone</c>, when set, is the path of another trigger
        /// whose box everyone must be in: the bay door shuts when someone leaves its trigger, once everyone is in the bay.
        /// </summary>
        private static readonly (string Level, string Path, string? Event, bool InZone, float Below, string? Zone)[] s_groupStarts = {
            ("Campaign_Interlude_Habitation_To_Abyss", "Call Platform", null, false, 0f, null),
            ("Campaign_Interlude_Habitation_To_Abyss", "Prop_Lever_01_Flip", null, false, 0f, null),
            ("M3_Habitation_Shaft_Intro", "Entities/Trigger-CenterRoom", null, true, 0f, null),
            ("M3_Habitation_Shaft_Intro", "Entities/Trigger-RoomEnter", null, true, 0f, null),
            ("M3_Habitation_Shaft_Intro", "Entities/Trigger-EnterShaft", null, true, SHAFT_ZONE_BELOW, null),
            ("M3_Habitation_Shaft_To_Pier", "Scripting/Trigger-ElevatorFall", null, true, 0f, null),
            ("M3_Habitation_Pier_Entrance", "Scripting/Trigger-FirstBayOpen", nameof(UT_TriggerZone.onEnter), true, 0f, null),
            ("M3_Habitation_Pier_Entrance", "Scripting/Trigger-FirstBayOpen", nameof(UT_TriggerZone.onExit), true, 0f, "Scripting/Trigger-FirstBaySeal"),
            ("M3_Habitation_Pier_Entrance", "Scripting/Trigger-FirstBaySeal", null, true, 0f, null),
            ("Campaign_Interlude_Sink_To_Pipeworks", "Exit Trigger", null, true, 0f, null),
            ("Campaign_Interlude_Chute_To_Habitation", "Entities/Seal Door/Door Seal", null, true, CHUTE_SEAL_ZONE_BELOW, null),
            ("Campaign_Interlude_Chute_To_Habitation", "Entities/Trigger-EnterShaft", null, true, SHAFT_ZONE_BELOW, null),
            ("Campaign_Interlude_Duct_To_Abyss", "Scripting/Trigger - Lock and Despawn Remains", null, true, 0f, null),
            ("Campaign_Interlude_Diver_To_Nest_SafeArea", "ENV_Hunter_StopperDoor/Close Trigger", null, true, 0f, null)
        };

        /// <summary>
        /// Level buttons that only press once every living player is there, by level and the button's path below it: within
        /// <c>Radius</c> of the button, or inside the box of the trigger at <c>Zone</c> when that is set. Buttons that start a
        /// mover need no entry (see <see cref="WKTButtonSync.MOVER_GATHER_RADIUS"/>).
        /// <list type="bullet">
        /// <item>The switch that opens the pier's first bay onto the pier: everyone must be in the bay, past its first door.</item>
        /// <item>The button that opens the door from the computer room to the abyss tram.</item>
        /// <item>The tram's own button, which sets off the ride down.</item>
        /// <item>The seal in the Tangled Sink's exit room, which shuts the floor hatch up from the sink: everyone must be in the
        /// room, whose box the speedrun tracker's trigger is. A radius would also count players still climbing in the sink.</item>
        /// </list>
        /// </summary>
        private static readonly (string Level, string Path, float Radius, string? Zone)[] s_gatheredButtons = {
            ("M3_Habitation_Pier_Entrance", "Scripting/Prop_Button_02_Switch", 0f, "Scripting/Trigger-FirstBaySeal"),
            ("Campaign_Interlude_Habitation_To_Abyss", "Scripting/Prop_Button_03", BUTTON_RADIUS, null),
            ("Campaign_Interlude_Habitation_To_Abyss", "Exterior/M4_Interlude_Collapse_Anim.01/Tram_Interfacility_Offline_Front_01/Prop_Button_03.01", BUTTON_RADIUS, null),
            ("Campaign_Interlude_Sink_To_Pipeworks", "Scripting/Buttons/Button_Main_Seal", 0f, "UT_SpeedrunTracker")
        };

        /// <summary>
        /// Adds to <paramref name="buttons"/> the buttons of a level whose press waits for everyone (see
        /// <see cref="s_gatheredButtons"/>): each one's path below the level, how close to it everyone must be, and the path of
        /// the trigger whose box they must be in instead, or null.
        /// </summary>
        public static void CollectGatheredButtons(string levelName, List<(string Path, float Radius, string? Zone)> buttons) {
            foreach (var (level, path, radius, zone) in s_gatheredButtons) {
                if (levelName.StartsWith(level, StringComparison.Ordinal))
                    buttons.Add((path, radius, zone));
            }
        }

        /// <summary>
        /// Whether an event waits for the group: one that builds a branch, or a listed group start's (see
        /// <see cref="IsGroupStart"/>) that changes the world. A listed trigger's other events, such as the airlock's empty
        /// exit, must not: they would wait for everyone to be back in a room they all just left, for good. Pure.
        /// </summary>
        public static bool WaitsForGroup(bool listed, bool buildsBranch, WKTListenerMasks masks) => buildsBranch || (listed && masks.HasWorld);

        /// <summary>How many of <paramref name="positions"/> are within <paramref name="radius"/> of <paramref name="center"/>. Pure.</summary>
        public static int CountNear(ReadOnlySpan<Vector3> positions, Vector3 center, float radius) {
            var near = 0;
            var radiusSquared = radius * radius;
            foreach (var position in positions) {
                if ((position - center).sqrMagnitude <= radiusSquared)
                    near++;
            }
            return near;
        }

        /// <summary>The living players' positions here (the local player and those shown for the others), and how many there are.</summary>
        public static int LivingPositions(Span<Vector3> positions) {
            var count = 0;
            if (ENT_Player.playerObject is { dead: false } local && count < positions.Length)
                positions[count++] = local.transform.position;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount && count < positions.Length; i++) {
                if (!WKTPlayerPositions.IsRemoteDead(i))
                    positions[count++] = WKTPlayerPositions.GetRemotePosition(i);
            }
            return count;
        }

        /// <summary>Whether every living player is near the local player, who sets something off.</summary>
        public static bool AllNearLocal(out int near, out int living) {
            if (ENT_Player.playerObject is not { dead: false } local) {
                near = 0;
                living = 0;
                return false;
            }
            return AllNear(local.transform.position, out near, out living);
        }

        /// <summary>Whether every living player is near <paramref name="center"/>; <paramref name="near"/> of <paramref name="living"/> are.</summary>
        public static bool AllNear(Vector3 center, out int near, out int living) => AllNear(center, RADIUS, out near, out living);

        /// <summary>Whether every living player is within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public static bool AllNear(Vector3 center, float radius, out int near, out int living) {
            Span<Vector3> positions = stackalloc Vector3[WKTLobbyConsts.MAX_PLAYERS + 1];
            living = LivingPositions(positions);
            near = CountNear(positions.Slice(0, living), center, radius);
            return near >= living;
        }

        /// <summary>
        /// Whether a level input starts something every player must be there for (see <see cref="s_groupStarts"/>), and
        /// whether they must all be inside its trigger's box for it (<paramref name="inZone"/>), with how many meters under the
        /// box still count (<paramref name="below"/>). <paramref name="eventName"/> is the input's event field;
        /// <paramref name="zonePath"/> is the path below the level of another trigger whose box counts instead, or null.
        /// </summary>
        public static bool IsGroupStart(string levelName, string inputPath, string eventName, out bool inZone, out float below, out string? zonePath) {
            foreach (var (level, path, onlyEvent, zone, zoneBelow, otherZone) in s_groupStarts) {
                // A zone's entry is that trigger alone, not what hangs under it (the Teeth's distant callout timers).
                if (!levelName.StartsWith(level, StringComparison.Ordinal) ||
                    (zone ? inputPath != path : inputPath.IndexOf(path, StringComparison.Ordinal) < 0) ||
                    (onlyEvent != null && onlyEvent != eventName)) continue;
                inZone = zone;
                below = zoneBelow;
                zonePath = otherZone;
                return true;
            }
            inZone = false;
            below = 0f;
            zonePath = null;
            return false;
        }

        /// <summary>
        /// Whether a point at <paramref name="local"/> in a box's space is inside the box, or within <paramref name="margin"/>
        /// of it (also in the box's space), or up to <paramref name="below"/> under its bottom. Pure.
        /// </summary>
        public static bool IsInBox(Vector3 local, Vector3 center, Vector3 size, Vector3 margin, float below = 0f) {
            var offset = local - center;
            var halfHeight = size.y * 0.5f;
            return Mathf.Abs(offset.x) <= size.x * 0.5f + margin.x && offset.y <= halfHeight + margin.y && offset.y >= -halfHeight - margin.y - below &&
                   Mathf.Abs(offset.z) <= size.z * 0.5f + margin.z;
        }

        /// <summary>
        /// Whether <paramref name="position"/> is inside <paramref name="box"/>, give or take <see cref="ZONE_MARGIN"/>, or up to
        /// <paramref name="below"/> meters under it, enabled or not.
        /// </summary>
        public static bool IsInZone(BoxCollider box, Vector3 position, float below = 0f) {
            var transform = box.transform;
            var scale = transform.lossyScale;
            var scaleY = Mathf.Max(Mathf.Abs(scale.y), 1e-4f);
            var margin = new Vector3(ZONE_MARGIN / Mathf.Max(Mathf.Abs(scale.x), 1e-4f), ZONE_MARGIN / scaleY, ZONE_MARGIN / Mathf.Max(Mathf.Abs(scale.z), 1e-4f));
            return IsInBox(transform.InverseTransformPoint(position), box.center, box.size, margin, below / scaleY);
        }

        /// <summary>
        /// Whether every living player is inside <paramref name="box"/> (or up to <paramref name="below"/> meters under it);
        /// <paramref name="inside"/> of <paramref name="living"/> are.
        /// </summary>
        public static bool AllInZone(BoxCollider box, float below, out int inside, out int living) {
            Span<Vector3> positions = stackalloc Vector3[WKTLobbyConsts.MAX_PLAYERS + 1];
            living = LivingPositions(positions);
            inside = 0;
            for (var i = 0; i < living; i++) {
                if (IsInZone(box, positions[i], below))
                    inside++;
            }
            return living > 0 && inside >= living;
        }

        /// <summary>
        /// Someone started a shortcut at <paramref name="center"/>: the living local player, if near, is moved onto their own
        /// spot around it, or onto the center itself where that spot is blocked.
        /// </summary>
        public static void PullLocal(Vector3 center, int slot) {
            if (ENT_Player.playerObject is not { dead: false } local) return;
            if ((local.transform.position - center).sqrMagnitude > PULL_LIMIT * PULL_LIMIT) return;
            var spot = FindSpot(center, slot);
            local.Teleport(spot, local.transform.rotation, keepVelocity: false);
            WKT.Instance.Logger.LogInfo("Pulled to the player who started a shortcut.");
        }

        /// <summary>Whether an event changes which levels the route has (see <see cref="s_loaders"/>).</summary>
        public static bool IsLoading(UnityEventBase unityEvent) {
            var count = unityEvent.GetPersistentEventCount();
            for (var i = 0; i < count; i++) {
                var target = unityEvent.GetPersistentTarget(i);
                if (target != null && IsLoader(target.GetType(), unityEvent.GetPersistentMethodName(i))) return true;
            }
            return false;
        }

        /// <summary>Whether <paramref name="method"/> on a <paramref name="targetType"/> changes which levels the route has. Pure.</summary>
        public static bool IsLoader(Type targetType, string method) {
            foreach (var (type, name) in s_loaders) {
                if (type.IsAssignableFrom(targetType) && name == method) return true;
            }
            return false;
        }

        /// <summary>Whom an event's level scripting brings to whoever set it off: the widest of its listeners'.</summary>
        public static WKTLoadPull GetLoadPull(UnityEventBase unityEvent) {
            var pull = WKTLoadPull.None;
            var count = unityEvent.GetPersistentEventCount();
            for (var i = 0; i < count; i++) {
                var target = unityEvent.GetPersistentTarget(i);
                if (target == null) continue;
                var listenerPull = GetLoadPull(target.GetType(), unityEvent.GetPersistentMethodName(i));
                if (listenerPull == WKTLoadPull.Everyone) return listenerPull;
                if (listenerPull != WKTLoadPull.None)
                    pull = listenerPull;
            }
            return pull;
        }

        /// <summary>
        /// Whom <paramref name="method"/> on a <paramref name="targetType"/> brings over: making every level again takes
        /// everyone's away, deleting the other branches takes theirs from whoever is on one. Unloading behind, generating
        /// on and switching branch take nobody's (every machine keeps its players' levels). Pure.
        /// </summary>
        public static WKTLoadPull GetLoadPull(Type targetType, string method) {
            if (!typeof(UT_WorldLoaderController).IsAssignableFrom(targetType)) return WKTLoadPull.None;
            return method switch {
                nameof(UT_WorldLoaderController.SendToWorldLoader) => WKTLoadPull.Everyone,
                nameof(UT_WorldLoaderController.DeleteOtherBranches) => WKTLoadPull.OtherBranches,
                _ => WKTLoadPull.None
            };
        }

        /// <summary>
        /// Whether a player on the branch <paramref name="localBranch"/> is brought over by level scripting that brings
        /// <paramref name="pull"/>, keeping the branch <paramref name="keptBranch"/>. Pure.
        /// </summary>
        public static bool IsPulledByLoading(WKTLoadPull pull, string? localBranch, string keptBranch) =>
            pull switch {
                WKTLoadPull.Everyone => true,
                WKTLoadPull.OtherBranches => localBranch != null && keptBranch.Length > 0 && localBranch != keptBranch,
                _ => false
            };

        /// <summary>
        /// Someone set off level scripting that takes the local player's level away, at <paramref name="center"/>: the living
        /// local player, if more than <see cref="RADIUS"/> away however far, is moved onto their own spot around it, crouched
        /// so they fit wherever it is.
        /// </summary>
        public static void PullLocalFar(Vector3 center, int slot) {
            if (ENT_Player.playerObject is not { dead: false } local) return;
            var distance = (local.transform.position - center).magnitude;
            if (distance <= RADIUS) return;
            WKTPlayerCrouch.Crouch(local);
            local.Teleport(FindSpot(center, slot), local.transform.rotation, keepVelocity: false);
            WKT.Instance.Logger.LogInfo($"Brought {distance:F0} m to the player who set off level loading.");
        }

        /// <summary>The local player's spot around <paramref name="center"/>, or the center itself where that spot is blocked.</summary>
        private static Vector3 FindSpot(Vector3 center, int slot) {
            var spot = PullSpot(center, slot);
            var mask = LayerMask.GetMask("Default", "World", "Platform");
            var bottom = spot + Vector3.up * (c_capsule_radius + 0.05f);
            var top = spot + Vector3.up * (c_capsule_height - c_capsule_radius);
            return Physics.CheckCapsule(bottom, top, c_capsule_radius, mask, QueryTriggerInteraction.Ignore) ||
                   Physics.Linecast(center + Vector3.up, spot + Vector3.up, mask, QueryTriggerInteraction.Ignore)
                ? center
                : spot;
        }

        /// <summary>The spot around <paramref name="center"/> for run slot <paramref name="slot"/>. Pure.</summary>
        public static Vector3 PullSpot(Vector3 center, int slot) => center + WKTPlayerSpots.Offset(slot) * (PULL_SPOT_RADIUS / WKTPlayerSpots.RADIUS);

        /// <summary>Whether an event builds a branch, a shortcut's start.</summary>
        public static bool CallsGenerator(UnityEventBase unityEvent) {
            var count = unityEvent.GetPersistentEventCount();
            for (var i = 0; i < count; i++) {
                if (unityEvent.GetPersistentTarget(i) is UT_BranchGenerator && unityEvent.GetPersistentMethodName(i) == c_generate) return true;
            }
            return false;
        }

        /// <summary>The path of <paramref name="target"/> below <paramref name="root"/>, names joined by '/'.</summary>
        public static string PathBelow(Transform target, Transform root) {
            var path = target.name;
            for (var current = target.parent; current != null && current != root; current = current.parent)
                path = current.name + "/" + path;
            return path;
        }

    }
}
