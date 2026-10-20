using System;
using System.Collections.Generic;
using DarkMachine.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Clones the game's settings widgets for the mod's own settings. Clones are made under an inactive root so none of their
    /// components wake up before the game's setting binders are stripped, and so a cloned widget can never read or write the
    /// game's real settings (its <c>settings.json</c>).
    /// </summary>
    internal static class WKTSettingsRowCloner {
        /// <summary>
        /// Game components that bind widgets to game settings or controls. Stripped from every clone. Add newly introduced
        /// binders here.
        /// </summary>
        private static readonly Type[] s_strippedComponents = {
            typeof(Settings_Resolution),
            typeof(ToggleSettingsBinder),
            typeof(SliderSettingBinder),
            typeof(TextSettingsBinder),
            typeof(DropdownBinder),
            typeof(ControlSettingBinder),
            typeof(UI_FirstSelect),
            typeof(OS_Tooltip)
        };
        /// <summary>Stripped by name: Rewired's glyph would replace the key text of a cloned rebind button.</summary>
        private static readonly string[] s_strippedComponentNames = { "UnityUIControllerElementGlyph" };
        private static readonly List<Component> s_componentCache = new();
        private static readonly List<string> s_options = new();
        private static Transform? s_inertRoot;

        /// <summary>Removes every game setting binder in <paramref name="obj"/> and its children.</summary>
        public static void StripBinders(GameObject obj) {
            obj.GetComponentsInChildren(true, s_componentCache);
            foreach (var component in s_componentCache) {
                if (component == null) continue;
                var type = component.GetType();
                if (Array.IndexOf(s_strippedComponents, type) >= 0 || Array.IndexOf(s_strippedComponentNames, type.Name) >= 0)
                    Object.DestroyImmediate(component);
            }
            s_componentCache.Clear();
        }

        /// <summary>
        /// Clones <paramref name="source"/> without waking it, and strips its binders. Configure the clone, then call
        /// <see cref="Attach"/>.
        /// </summary>
        public static GameObject CloneInert(GameObject source, string name) {
            var clone = Object.Instantiate(source, GetInertRoot(), false);
            clone.name = name;
            StripBinders(clone);
            return clone;
        }

        /// <summary>Moves a clone made by <see cref="CloneInert"/> under <paramref name="parent"/> and wakes it.</summary>
        public static void Attach(GameObject obj, Transform parent, int siblingIndex = -1) {
            obj.transform.SetParent(parent, false);
            if (siblingIndex >= 0)
                obj.transform.SetSiblingIndex(siblingIndex);
            obj.SetActive(true);
        }

        /// <summary>Clears every listener of a cloned toggle, persistent (inspector assigned) ones too.</summary>
        public static Toggle ResetToggle(GameObject obj) {
            var toggle = obj.GetComponentInChildren<Toggle>(true);
            toggle.onValueChanged = new Toggle.ToggleEvent();
            return toggle;
        }

        /// <summary>Clears every listener of a cloned button, persistent ones too.</summary>
        public static void ResetButton(Button button) => button.onClick = new Button.ButtonClickedEvent();

        /// <summary>A copy of a settings column's title text.</summary>
        public static TMP_Text AddHeader(GameObject template, Transform parent, string text) {
            var obj = CloneInert(template, "WKT Header : " + text);
            var label = obj.GetComponent<TMP_Text>();
            label.text = text;
            Attach(obj, parent);
            return label;
        }

        /// <summary>A copy of a settings toggle (its label at <paramref name="labelPath"/>), driving <paramref name="onChanged"/>.</summary>
        public static Toggle AddToggle(GameObject template, Transform parent, string labelPath, string label, bool value, Action<bool> onChanged) {
            var obj = CloneInert(template, "WKT Toggle : " + label);
            var labelTransform = obj.transform.Find(labelPath);
            if (labelTransform != null && labelTransform.TryGetComponent<TMP_Text>(out var text))
                text.text = label;
            var toggle = ResetToggle(obj);
            toggle.SetIsOnWithoutNotify(value);
            toggle.onValueChanged.AddListener(onChanged.Invoke);
            Attach(obj, parent);
            return toggle;
        }

        /// <summary>
        /// A copy of a settings slider row: its label is the row's own text, its value is shown with <paramref name="format"/>
        /// in the row's value text, and moving it calls <paramref name="onChanged"/>.
        /// </summary>
        public static SubmitSlider AddSlider(GameObject template, Transform parent, string label, float min, float max, float value,
            Func<float, string> format, Action<float> onChanged) {
            var obj = CloneInert(template, "WKT Slider : " + label);
            obj.GetComponent<TMP_Text>().text = label;
            var slider = obj.GetComponentInChildren<SubmitSlider>(true);
            slider.onValueChanged = new SubmitSlider.SliderEvent();
            slider.onFinishChange = new SubmitSlider.SliderEvent();
            slider.wholeNumbers = false;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);
            TMP_Text? valueText = null;
            foreach (var text in obj.GetComponentsInChildren<TMP_Text>(true)) {
                if (text.gameObject == obj) continue;
                valueText = text;
                break;
            }
            if (valueText != null)
                valueText.text = format(value);
            slider.onValueChanged.AddListener(changed => {
                if (valueText != null)
                    valueText.text = format(changed);
                onChanged(changed);
            });
            Attach(obj, parent);
            return slider;
        }

        /// <summary>A copy of a settings dropdown row with <paramref name="options"/>, calling <paramref name="onChanged"/> with the picked index.</summary>
        public static TMP_Dropdown AddDropdown(GameObject template, Transform parent, string label, IList<string> options, int index, Action<int> onChanged) {
            var obj = CloneInert(template, "WKT Dropdown : " + label);
            obj.GetComponent<TMP_Text>().text = label;
            var dropdown = obj.GetComponentInChildren<TMP_Dropdown>(true);
            dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            SetOptions(dropdown, options, index);
            dropdown.onValueChanged.AddListener(onChanged.Invoke);
            Attach(obj, parent);
            return dropdown;
        }

        /// <summary>Replaces a dropdown's options and selection without calling its listeners.</summary>
        public static void SetOptions(TMP_Dropdown dropdown, IList<string> options, int index) {
            dropdown.ClearOptions();
            s_options.Clear();
            for (var i = 0; i < options.Count; i++)
                s_options.Add(options[i]);
            dropdown.AddOptions(s_options);
            s_options.Clear();
            dropdown.SetValueWithoutNotify(Mathf.Clamp(index, 0, options.Count - 1));
            dropdown.RefreshShownValue();
        }

        private static Transform GetInertRoot() {
            if (s_inertRoot != null) return s_inertRoot;
            var root = new GameObject("WKT Inert Clones", typeof(RectTransform));
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);
            s_inertRoot = root.transform;
            return s_inertRoot;
        }
    }
}
