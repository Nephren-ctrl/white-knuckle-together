using System;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>The game's own notice popup, for the mod's notices and questions. See <c>SettingsManager.UpdateSettings</c>.</summary>
    internal static class WKTAlert {
        /// <summary>Where the game's own notices go, and the notice it uses for them.</summary>
        private const string c_alert_root_locator = "UI_Alert_Root";
        private const string c_notice_popup = "Popup-Notice-Generic";
        private const string c_cancel_label = "Cancel";
        /// <summary>Above every screen of the menu.</summary>
        private const int c_sorting_order = 30000;

        /// <summary>A notice with one OK button. Shown as a notification instead when the popup is unavailable.</summary>
        public static void ShowNotice(string title, string description) {
            var data = new UI_Popup_Alert.PopupData {
                title = title,
                desc = description,
                leftButtonDesc = "OK",
                // An empty description hides the button.
                rightButtonDesc = string.Empty
            };
            if (TryShow(data)) return;
            WKT.Instance.Logger.LogError($"Cannot show the \"{title}\" popup. Showing a notification instead.");
            WKTToast.Show(description);
        }

        /// <summary>
        /// Asks before doing something: <paramref name="confirm"/> runs only if the player picks <paramref name="confirmLabel"/>,
        /// which is on the left like the game's own yes. False when the popup is unavailable, and nothing runs.
        /// </summary>
        public static bool ShowConfirm(string title, string description, string confirmLabel, Action confirm) {
            var data = new UI_Popup_Alert.PopupData {
                title = title,
                desc = description,
                leftButtonDesc = confirmLabel,
                rightButtonDesc = c_cancel_label,
                leftButtonAction = confirm
            };
            if (TryShow(data)) return true;
            WKT.Instance.Logger.LogError($"Cannot show the \"{title}\" popup.");
            return false;
        }

        private static bool TryShow(UI_Popup_Alert.PopupData data) {
            var root = UT_Locator.GetLocator(c_alert_root_locator);
            if (root == null) return false;
            var popup = CL_UIManager.CreatePopup(c_notice_popup, data, root.transform);
            if (popup == null) return false;
            // The alert root is on the main menu's own canvas, which the Play screen and the lobby are drawn over. A canvas
            // of its own puts the popup on top of every screen, and its raycaster lets its buttons be clicked there.
            var canvas = popup.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = c_sorting_order;
            popup.gameObject.AddComponent<GraphicRaycaster>();
            return true;
        }
    }
}
