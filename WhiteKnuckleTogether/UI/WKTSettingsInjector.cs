using System;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Adds the mod's settings to the game's own settings menu, in the main menu and in a run alike: its keys to a tab of the
    /// Controls page. Every widget is a clone of one of the game's own with the game's binders stripped (see
    /// <see cref="WKTSettingsRowCloner"/>), and is saved in the mod's config instead.
    /// </summary>
    internal static class WKTSettingsInjector {
        private const string c_controls_page = "SettingsParent/Settings Pane/Controls Page";
        private const string c_tab_selector = "Controls Page Tab Selector";
        private const string c_tab_button_template = "Controller Bindings ";
        private const string c_kbm_tab = "Binding Tab Root/KBM Bindings Tab";
        private const string c_kbm_layout = "KBM Bindings Layout Group";
        private const string c_reset_all_button = "Reset Bindings";
        private const string c_mod_tab_name = "WKT Mod Binds";
        private const string c_mod_tab_title = "MOD BINDS";
        private const string c_accessibility_tab = "SettingsParent/Settings Pane/Gameplay Settings/Tab Holder/Accesibility Tab";
        /// <summary>The Accessibility tab's column that the game leaves nearly empty, so the mod's toggles fit there.</summary>
        private const string c_other_column = "Other Column";
        private const string c_toggle_template = "Accessibility/Epilepsy";
        private const string c_header_template = "Other Column/Gameplay Toggles";
        private const string c_spacer_template = "Accessibility/--- (1).02";
        private const string c_multiplayer_header = "Multiplayer";
        /// <summary>Where a settings toggle's label sits.</summary>
        public const string TOGGLE_LABEL = "Background/Label (1)";
        private const string c_rebind_template = "Rebind + Alt Prefab : Jump";
        private const string c_rebind_binding = "RebindUIPrefab";
        private const string c_rebind_alt = "Alt";
        private const string c_action_name_text = "ActionNameText";
        private const string c_rebind_button = "TriggerRebindButton";
        private const string c_reset_button = "ResetToDefaultButton";
        private const string c_remove_button = "RemoveBindingButton";

        /// <summary>Call once per settings menu, when it starts.</summary>
        public static void Inject(Transform settingsMenu) {
            var config = WKTPlugin.ModConfig;
            if (config == null) return;
            RunStage("add the mod's keys to the Controls page", () => AddKeyTab(settingsMenu, config));
            RunStage("add the multiplayer toggles to the Accessibility tab", () => AddMultiplayerToggles(settingsMenu, config));
            RunStage("add voice chat to the Audio tab", () => WKTVoiceSettingsUI.Add(settingsMenu, config));
        }

        /// <summary>A broken stage only loses the mod's own rows, never the game's menu.</summary>
        private static void RunStage(string stage, Action action) {
            try {
                action();
            }
            catch (Exception e) {
                WKT.Instance.Logger.LogError($"Failed to {stage}: {e}");
            }
        }

        /// <summary>
        /// The game's keyboard binds fill their tab exactly, so the mod's keys get a tab of their own beside it: a copy of the
        /// keyboard binds tab, emptied, with its "reset bindings" button resetting the mod's keys instead.
        /// </summary>
        private static void AddKeyTab(Transform settingsMenu, WKTConfig config) {
            if (!WKTUIPath.TryFind(settingsMenu, c_controls_page, out var controlsPage, true)) return;
            if (!WKTUIPath.TryFind(controlsPage, c_kbm_tab, out var kbmTab)) return;
            if (!WKTUIPath.TryFind<UI_TabGroup>(controlsPage, c_tab_selector, out var tabGroup)) return;
            if (!WKTUIPath.TryFind<Button>(tabGroup.transform, c_tab_button_template, out var buttonTemplate)) return;
            if (!WKTUIPath.TryFind(kbmTab, c_kbm_layout + "/" + c_rebind_template, out var rowTemplate)) return;
            var tab = WKTSettingsRowCloner.CloneInert(kbmTab.gameObject, c_mod_tab_name);
            var layout = tab.transform.Find(c_kbm_layout);
            for (var i = layout.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(layout.GetChild(i).gameObject);
            var rows = new WKTKeyRebindRow[config.KeyBindings.Count];
            for (var i = 0; i < rows.Length; i++)
                rows[i] = AddKeyRow(layout, rowTemplate.gameObject, config.KeyBindings[i]);
            var reset = tab.transform.Find(c_reset_all_button);
            if (reset != null && reset.TryGetComponent<Button>(out var resetButton)) {
                WKTSettingsRowCloner.ResetButton(resetButton);
                resetButton.onClick.AddListener(() => {
                    foreach (var row in rows)
                        row.ResetToDefault();
                });
            }
            AddSubTab(tabGroup, buttonTemplate, tab, kbmTab.parent, c_mod_tab_name, c_mod_tab_title);
        }

        /// <summary>
        /// Adds <paramref name="page"/> (a clone made inert) under <paramref name="pageParent"/> as a new tab of
        /// <paramref name="tabGroup"/>, with a copy of <paramref name="buttonTemplate"/> placed after it as the tab's button.
        /// </summary>
        public static void AddSubTab(UI_TabGroup tabGroup, Button buttonTemplate, GameObject page, Transform pageParent, string name, string title) {
            // Kept inactive: the tab group shows it.
            page.SetActive(false);
            page.transform.SetParent(pageParent, false);
            var button = WKTSettingsRowCloner.CloneInert(buttonTemplate.gameObject, name + " Button").GetComponent<Button>();
            WKTSettingsRowCloner.ResetButton(button);
            var buttonText = button.GetComponentInChildren<TMP_Text>(true);
            buttonText.text = title;
            WKTSettingsRowCloner.Attach(button.gameObject, tabGroup.transform, buttonTemplate.transform.GetSiblingIndex() + 1);
            tabGroup.tabs.Add(new UI_TabGroup.Tab { name = name, button = button, tabObject = page, buttonText = buttonText });
            // The group may have started already, and then never wires a tab added after.
            button.onClick.AddListener(() => tabGroup.SelectTab(name));
            button.image.color = tabGroup.closeColor;
            if (tabGroup.changeTextColor)
                buttonText.color = tabGroup.textCloseColor;
        }

        /// <summary>
        /// The mod's toggles go under a header of their own in the Accessibility tab's "Other" column: the tab's
        /// Accessibility column is full, and more rows there run under the menu's buttons.
        /// </summary>
        private static void AddMultiplayerToggles(Transform settingsMenu, WKTConfig config) {
            if (!WKTUIPath.TryFind(settingsMenu, c_accessibility_tab, out var tab, true)) return;
            if (!WKTUIPath.TryFind(tab, c_other_column, out var column)) return;
            if (!WKTUIPath.TryFind(tab, c_toggle_template, out var template)) return;
            if (WKTUIPath.TryFind(tab, c_spacer_template, out var spacer))
                WKTSettingsRowCloner.Attach(WKTSettingsRowCloner.CloneInert(spacer.gameObject, "WKT Spacer"), column);
            if (WKTUIPath.TryFind(tab, c_header_template, out var header))
                WKTSettingsRowCloner.AddHeader(header.gameObject, column, c_multiplayer_header);
            AddToggle(template, column, "Censor Chat", config.CensorChat);
            AddToggle(template, column, "Offer Hand Only In Focus", config.GrabAssistsNeedFocus);
            AddToggle(template, column, "Show Dead Ghosts", config.ShowDeadGhosts);
        }

        private static void AddToggle(Transform template, Transform column, string label, ConfigEntry<bool> entry) =>
            WKTSettingsRowCloner.AddToggle(template.gameObject, column, TOGGLE_LABEL, label, entry.Value, value => entry.Value = value);

        private static WKTKeyRebindRow AddKeyRow(Transform layout, GameObject template, WKTKeyBinding binding) {
            var row = WKTSettingsRowCloner.CloneInert(template, "WKT Rebind : " + binding.DisplayName);
            var bindingRoot = row.transform.Find(c_rebind_binding);
            if (bindingRoot == null)
                throw new InvalidOperationException($"The rebind row has no {c_rebind_binding}.");
            var alt = row.transform.Find(c_rebind_alt);
            if (alt != null)
                alt.gameObject.SetActive(false);
            var actionName = bindingRoot.Find(c_action_name_text);
            if (actionName != null && actionName.TryGetComponent<Text>(out var actionText))
                actionText.text = binding.DisplayName;
            var rebindButton = bindingRoot.Find(c_rebind_button).GetComponent<Button>();
            var resetButton = FindButton(bindingRoot, c_reset_button);
            var removeButton = FindButton(bindingRoot, c_remove_button);
            WKTSettingsRowCloner.ResetButton(rebindButton);
            var keyText = CreateKeyText(rebindButton.transform, resetButton != null ? resetButton.GetComponentInChildren<TMP_Text>(true) : null);
            var rebindRow = row.AddComponent<WKTKeyRebindRow>();
            rebindRow.Setup(binding, rebindButton, resetButton, removeButton, keyText);
            WKTSettingsRowCloner.Attach(row, layout);
            return rebindRow;
        }

        private static Button? FindButton(Transform root, string path) {
            var found = root.Find(path);
            if (found == null || !found.TryGetComponent<Button>(out var button)) return null;
            WKTSettingsRowCloner.ResetButton(button);
            return button;
        }

        /// <summary>The game draws the bound key as a Rewired glyph, which the clone lost, so the key is written out instead.</summary>
        private static TMP_Text CreateKeyText(Transform button, TMP_Text? style) {
            var obj = new GameObject("WKT Key Text", typeof(RectTransform));
            var rect = (RectTransform)obj.transform;
            rect.SetParent(button, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = obj.AddComponent<TextMeshProUGUI>();
            if (style != null) {
                text.font = style.font;
                text.fontSize = style.fontSize;
                text.color = style.color;
            }
            text.enableAutoSizing = true;
            text.fontSizeMax = style != null ? style.fontSize : 24f;
            text.fontSizeMin = 8f;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }
    }
}
