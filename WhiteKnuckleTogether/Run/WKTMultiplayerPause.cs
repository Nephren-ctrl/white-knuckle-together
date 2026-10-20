using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The world keeps going for everyone else in a multiplayer run, so the pause menu cannot stop time there. It opens
    /// over the running game instead, and while it is open the local player's controls read as idle: no moving, looking,
    /// jumping, crouching or using items. Hands keep the grip they had when it opened, so opening it while hanging never
    /// drops anyone. Menu navigation stays live. Singleplayer pauses exactly as before.
    /// </summary>
    internal static class WKTMultiplayerPause {
        private const int c_hand_count = 2;
        /// <summary>The buttons the pause menu itself is driven with.</summary>
        private static readonly string[] s_menuButtons = { "Pause", "UIBackButton", "Cancel" };
        private static readonly AccessTools.FieldRef<InputManager, List<InputManager.ButtonInput>?> s_buttonInputs =
            AccessTools.FieldRefAccess<InputManager, List<InputManager.ButtonInput>?>("buttonInputs");
        private static readonly AccessTools.FieldRef<InputManager, List<InputManager.VectorInput>?> s_vectorInputs =
            AccessTools.FieldRefAccess<InputManager, List<InputManager.VectorInput>?>("vectorInputs");
        private static readonly InputManager.ButtonInput?[] s_handButtons = new InputManager.ButtonInput?[c_hand_count];
        private static readonly bool[] s_handPressed = new bool[c_hand_count];
        /// <summary>Whether the hands' grip was taken for the menu that is open.</summary>
        private static bool s_holdsHands;
        /// <summary>
        /// The frame the menu was last closed. The key that closes it also asks the game to pause, which must not open it
        /// again in the same frame.
        /// </summary>
        private static int s_closedFrame = -1;

        /// <summary>The menu is open over a multiplayer run that keeps going.</summary>
        public static bool IsOpen {
            get {
                var gameManager = CL_GameManager.gMan;
                return IsMultiplayerRun && gameManager != null && !gameManager.isPaused && gameManager.pauseMenu != null &&
                       gameManager.pauseMenu.activeInHierarchy;
            }
        }

        private static bool IsMultiplayerRun => WKT.InstanceOrNull != null && WKT.InstanceOrNull.Network.IsMultiplayerRun;

        /// <summary>
        /// In a multiplayer run, opens the pause menu like the game does, but without stopping time, sound or the world,
        /// and returns true: the game's own pause must not run. Returns false otherwise.
        /// </summary>
        public static bool TryOpen(CL_GameManager gameManager) {
            if (!IsMultiplayerRun) return false;
            if (gameManager.pauseMenu == null || gameManager.pauseMenu.activeSelf || Time.frameCount == s_closedFrame) return true;
            Cursor.lockState = CursorLockMode.None;
            CL_GameManager.showCursor = true;
            gameManager.pauseMenu.SetActive(true);
            return true;
        }

        /// <summary>Call after the game closed its pause menu.</summary>
        public static void OnClosed() => s_closedFrame = Time.frameCount;

        /// <summary>
        /// Call after <paramref name="input"/> read this frame's input. While the menu is open, gameplay input reads as idle.
        /// While the chat has the keyboard, every button reads as idle, menu buttons too, so typing never moves, pauses or
        /// clicks anything.
        /// </summary>
        public static void FilterInput(InputManager input) {
            var chat = WKTChat.OwnsKeyboard;
            if (!IsOpen && !chat) {
                if (s_holdsHands)
                    ReleaseHands();
                return;
            }
            if (!s_holdsHands)
                HoldHands();
            var buttons = s_buttonInputs(input);
            if (buttons != null) {
                foreach (var button in buttons)
                    FilterButton(button, chat);
            }
            var vectors = s_vectorInputs(input);
            if (vectors == null) return;
            foreach (var vector in vectors)
                ClearVector(vector);
        }

        /// <summary>Makes every gameplay button and vector of <paramref name="input"/> read as idle this frame. Menu buttons stay live.</summary>
        public static void ClearGameplayInput(InputManager input) {
            var buttons = s_buttonInputs(input);
            if (buttons != null) {
                foreach (var button in buttons) {
                    if (IsMenuButton(button.name)) continue;
                    button.Down = button.Up = false;
                    button.Pressed = false;
                }
            }
            var vectors = s_vectorInputs(input);
            if (vectors == null) return;
            foreach (var vector in vectors)
                ClearVector(vector);
        }

        private static void ClearVector(InputManager.VectorInput vector) {
            vector.vector = Vector2.zero;
            vector.axis = 0f;
            vector.Pressed = vector.Down = vector.Up = false;
        }

        /// <summary>Takes each hand's button as it is now, which keeps grabbing for as long as the menu is open.</summary>
        private static void HoldHands() {
            s_holdsHands = true;
            var hands = ENT_Player.playerObject == null ? null : ENT_Player.playerObject.hands;
            for (var i = 0; i < c_hand_count; i++) {
                var button = hands != null && i < hands.Length && hands[i] != null ? InputManager.GetButton(hands[i].fireButton) : null;
                s_handButtons[i] = button;
                s_handPressed[i] = button != null && button.Pressed;
            }
        }

        /// <summary>
        /// The menu closed. A hand button let go while it was open never sent its release, so an item in use (a held fire,
        /// a placed piton) stayed in use and could not be dropped until clicked again: the release is sent now.
        /// </summary>
        private static void ReleaseHands() {
            s_holdsHands = false;
            for (var i = 0; i < c_hand_count; i++) {
                var button = s_handButtons[i];
                if (button != null && s_handPressed[i] && !button.Pressed)
                    button.Up = true;
                s_handButtons[i] = null;
            }
        }

        private static void FilterButton(InputManager.ButtonInput button, bool includeMenuButtons) {
            if (!includeMenuButtons && IsMenuButton(button.name)) return;
            button.Down = button.Up = false;
            button.Pressed = false;
            for (var i = 0; i < c_hand_count; i++) {
                if (ReferenceEquals(s_handButtons[i], button))
                    button.Pressed = s_handPressed[i];
            }
        }

        private static bool IsMenuButton(string name) {
            foreach (var menuButton in s_menuButtons)
                if (name == menuButton) return true;
            return false;
        }
    }
}
