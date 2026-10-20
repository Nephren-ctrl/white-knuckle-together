using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Hatches that shut below the group for good in a synced co-op run, and what keeps anyone from being left under them:
    /// <list type="bullet">
    /// <item>The giant airlock under the first computer room (<c>Campaign_Interlude_Silo_To_Pipeworks</c>), which its two
    /// buttons shut. Neither button presses until every living player is above the hatch.</item>
    /// <item>The vent hatch out of the Teeth's shaft (<c>M3_Habitation_Shaft_To_Pier</c>), which the group's gate shuts once
    /// everyone climbed through.</item>
    /// <item>The floor hatch into the Tangled Sink's exit room (<c>Campaign_Interlude_Sink_To_Pipeworks</c>), which the room's
    /// seal button shuts once everyone is in the room (see <see cref="WKTGatherCheck"/>), or the rising goo presses by
    /// itself.</item>
    /// <item>The Expulsion Chute's floor hatch out (<c>Campaign_Interlude_Chute_To_Habitation</c>), which its seal trigger shuts
    /// once everyone climbed well above it, or the rising goo by itself.</item>
    /// </list>
    /// As a last resort, a living local player who somehow ends up under one of them once it shut behind the group, with
    /// another living player above it, is moved to the highest such player after <see cref="LEFT_BEHIND_SECONDS"/>, crouched
    /// so they fit wherever that player is. Owned by <see cref="WKTDoorSync"/>.
    /// </summary>
    internal sealed class WKTOneWayDoors {
        /// <summary>How far under a hatch a player must be to count as left below it.</summary>
        public const float BELOW_MARGIN = 2f;
        /// <summary>How far above a hatch another player must be for the one below to be moved to them.</summary>
        public const float ABOVE_MARGIN = 1f;
        /// <summary>How long a player must be stuck under a shut hatch before they are moved.</summary>
        public const float LEFT_BEHIND_SECONDS = 4f;
        /// <summary>How long the wait panel keeps saying who is missing after a button would not press.</summary>
        private const float c_notice_seconds = 4f;
        public const string WAIT_LABEL = "Waiting for everyone to climb above the hatch";
        private static readonly int s_openBool = Animator.StringToHash("open");
        /// <summary>
        /// By level, the hatch's path below it, the paths of the buttons that shut it, and the path of the switch whose state
        /// tells it is shut, or null. A hatch with a switch is shut while the switch is on; an animated one while its animator's
        /// "open" bool is false; a <see cref="UT_Door"/> while it is not open.
        /// </summary>
        private static readonly (string Level, string Door, string[] Buttons, string? Seal)[] s_doors = {
            ("Campaign_Interlude_Silo_To_Pipeworks", "Scripting/Doors/Door_Airlock_Giant_01",
                new[] { "M1_Campaign_Silo_To_Pipeworks_01/Level_Hull/Button.001_BigSiloDoor", "Scripting/Lockdown Sequence/Prop_Button_02_Switch" }, null),
            ("M3_Habitation_Shaft_To_Pier", "Door_Habitation_Room_01/Door_Habitation_Room_01", Array.Empty<string>(), null),
            ("Campaign_Interlude_Sink_To_Pipeworks", "Scripting/Doors/Door_Sewer_01", Array.Empty<string>(), "Scripting/Buttons/Button_Main_Seal"),
            ("Campaign_Interlude_Chute_To_Habitation", "Entities/Seal Door/Door_Reinforced_01", Array.Empty<string>(), null)
        };
        private readonly List<Hatch> m_hatches = new();
        private float m_noticeUntil = float.NegativeInfinity;
        private Hatch? m_noticeHatch;
        private float m_stuckTime;

        public void IndexLevel(M_Level level) {
            foreach (var (levelName, doorPath, buttons, sealPath) in s_doors) {
                if (!level.name.StartsWith(levelName, StringComparison.Ordinal)) continue;
                var door = level.transform.Find(doorPath);
                if (door == null) {
                    WKT.Instance.Logger.LogWarning($"Level {level.name} has no hatch {doorPath}.");
                    continue;
                }
                CL_ToggleButton? seal = null;
                if (sealPath != null && (level.transform.Find(sealPath) is not { } sealObject || !sealObject.TryGetComponent(out seal))) {
                    WKT.Instance.Logger.LogWarning($"Level {level.name} has no hatch switch {sealPath}.");
                    continue;
                }
                var hatch = new Hatch(level.gameObject.GetInstanceID(), door, door.GetComponent<UT_Door>(), door.GetComponent<Animator>(), seal);
                foreach (var path in buttons) {
                    if (level.transform.Find(path) is { } button && button.TryGetComponent<CL_Button>(out var component))
                        hatch.Buttons.Add(component.GetInstanceID());
                    else
                        WKT.Instance.Logger.LogWarning($"Level {level.name} has no hatch button {path}.");
                }
                m_hatches.Add(hatch);
            }
        }

        public void ForgetLevel(M_Level level) {
            var id = level.gameObject.GetInstanceID();
            m_hatches.RemoveAll(hatch => hatch.LevelId == id);
            if (m_noticeHatch != null && m_noticeHatch.LevelId == id)
                m_noticeHatch = null;
        }

        /// <summary>Whether a press of <paramref name="button"/> may go ahead: one that shuts a hatch waits for everyone above it.</summary>
        public bool AllowPress(CL_Button button) {
            var id = button.GetInstanceID();
            foreach (var hatch in m_hatches) {
                if (!hatch.Buttons.Contains(id) || hatch.Door == null) continue;
                if (CountAbove(hatch.Door.position.y, out _, out _)) return true;
                m_noticeHatch = hatch;
                m_noticeUntil = Time.unscaledTime + c_notice_seconds;
                return false;
            }
            return true;
        }

        public void Update() {
            UpdateNotice();
            UpdateLeftBehind();
        }

        private void UpdateNotice() {
            if (m_noticeHatch?.Door == null || Time.unscaledTime >= m_noticeUntil) return;
            if (CountAbove(m_noticeHatch.Door.position.y, out var above, out var living)) {
                m_noticeUntil = float.NegativeInfinity;
                return;
            }
            WKTWaitHud.Report(WAIT_LABEL, above, living);
        }

        /// <summary>Whether every living player is above a hatch at <paramref name="doorY"/>; <paramref name="above"/> of <paramref name="living"/> are.</summary>
        private static bool CountAbove(float doorY, out int above, out int living) {
            Span<Vector3> positions = stackalloc Vector3[WKTLobbyConsts.MAX_PLAYERS + 1];
            living = WKTGatherCheck.LivingPositions(positions);
            above = CountAbove(positions.Slice(0, living), doorY);
            return above >= living;
        }

        /// <summary>How many of <paramref name="positions"/> are above <paramref name="doorY"/>. Pure.</summary>
        public static int CountAbove(ReadOnlySpan<Vector3> positions, float doorY) {
            var above = 0;
            foreach (var position in positions) {
                if (position.y > doorY)
                    above++;
            }
            return above;
        }

        /// <summary>
        /// Whether a player at <paramref name="localY"/> is left under a shut hatch at <paramref name="doorY"/> while another living
        /// player is above it, at <paramref name="highestY"/>. Pure.
        /// </summary>
        public static bool IsLeftBehind(float localY, float doorY, float highestY) =>
            localY < doorY - BELOW_MARGIN && highestY > doorY + ABOVE_MARGIN;

        private void UpdateLeftBehind() {
            var local = ENT_Player.playerObject;
            if (local == null || local.dead || !TryFindHighestOther(out var target)) {
                m_stuckTime = 0f;
                return;
            }
            var y = local.transform.position.y;
            var stuck = false;
            foreach (var hatch in m_hatches) {
                if (hatch.Door == null || !hatch.IsShutForGood() || !IsLeftBehind(y, hatch.Door.position.y, target.y)) continue;
                stuck = true;
                break;
            }
            if (!stuck) {
                m_stuckTime = 0f;
                return;
            }
            m_stuckTime += Time.deltaTime;
            if (m_stuckTime < LEFT_BEHIND_SECONDS) return;
            m_stuckTime = 0f;
            WKTPlayerCrouch.Crouch(local);
            local.Teleport(target, local.transform.rotation, keepVelocity: false);
            WKT.Instance.Logger.LogInfo($"Left under a shut hatch at y {y:F1}. Moved to the highest player, at y {target.y:F1}.");
        }

        /// <summary>The highest living other player's position. False when there is none.</summary>
        private static bool TryFindHighestOther(out Vector3 position) {
            position = default;
            var found = false;
            for (var i = 0; i < WKTPlayerPositions.RemoteCount; i++) {
                if (WKTPlayerPositions.IsRemoteDead(i)) continue;
                var candidate = WKTPlayerPositions.GetRemotePosition(i);
                if (found && candidate.y <= position.y) continue;
                position = candidate;
                found = true;
            }
            return found;
        }

        private sealed class Hatch {
            public readonly int LevelId;
            public readonly Transform Door;
            private readonly UT_Door? m_door;
            private readonly Animator? m_animator;
            private readonly CL_ToggleButton? m_seal;
            public readonly HashSet<int> Buttons = new();

            public Hatch(int levelId, Transform door, UT_Door? utDoor, Animator? animator, CL_ToggleButton? seal) {
                LevelId = levelId;
                Door = door;
                m_door = utDoor;
                m_animator = animator;
                m_seal = seal;
            }

            /// <summary>Shut behind the group.</summary>
            public bool IsShutForGood() {
                if (m_seal != null) return m_seal.state;
                if (m_door != null) return !m_door.IsOpen();
                return m_animator != null && m_animator.isActiveAndEnabled && m_animator.runtimeAnimatorController != null && !m_animator.GetBool(s_openBool);
            }
        }
    }
}
