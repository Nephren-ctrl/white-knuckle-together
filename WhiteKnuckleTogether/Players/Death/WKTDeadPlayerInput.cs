using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// While the local player is dead in a multiplayer run, the game reads every gameplay input as idle, so a dead player
    /// can do nothing in the world: perks, items and hands included. The spectator reads the input it needs first. The
    /// pause menu's buttons stay live, so a dead player can still pause and disconnect.
    /// </summary>
    internal static class WKTDeadPlayerInput {
        private const int c_left_hand = 0;
        private const int c_right_hand = 1;
        private const string c_focus = "Focus";
        private static int s_cycle;

        /// <summary>Set by the spectator while the local player is dead and the run goes on.</summary>
        public static bool IsActive { get; set; }
        /// <summary>This frame's look input, sensitivity applied. Zero while the pause menu is open.</summary>
        public static Vector2 Look { get; private set; }
        /// <summary>Whether focus is held this frame, which lets a dead player talk to the living (see <see cref="ModifierId.DeadCanTalk"/>).</summary>
        public static bool FocusHeld { get; private set; }

        /// <summary>
        /// Which way the player asked to switch the spectated player since the last call: -1 back (left hand), 1 forward
        /// (right hand), or 0. Clears the request.
        /// </summary>
        public static int TakeCycle() {
            var cycle = s_cycle;
            s_cycle = 0;
            return cycle;
        }

        /// <summary>Call after <paramref name="input"/> read this frame's input, before the pause menu filters it.</summary>
        public static void Filter(InputManager input) {
            if (!IsActive) {
                Look = Vector2.zero;
                FocusHeld = false;
                s_cycle = 0;
                return;
            }
            FocusHeld = InputManager.GetButton(c_focus).Pressed && !WKTChat.IsTyping;
            // The pause menu clears its own frames, and its clicks must not switch players.
            if (WKTMultiplayerPause.IsOpen) {
                Look = Vector2.zero;
                return;
            }
            Look = InputManager.GetLookVector();
            var player = ENT_Player.playerObject;
            if (player != null) {
                if (IsDown(player, c_left_hand))
                    s_cycle = -1;
                if (IsDown(player, c_right_hand))
                    s_cycle = 1;
            }
            WKTMultiplayerPause.ClearGameplayInput(input);
        }

        public static void Reset() {
            IsActive = false;
            Look = Vector2.zero;
            FocusHeld = false;
            s_cycle = 0;
        }

        /// <summary>Down rather than Pressed, which toggle grab turns into a toggle.</summary>
        private static bool IsDown(ENT_Player player, int hand) {
            var hands = player.hands;
            if (hands == null || hand >= hands.Length || hands[hand] == null) return false;
            var button = InputManager.GetButton(hands[hand].fireButton);
            return button != null && button.Down;
        }
    }
}
