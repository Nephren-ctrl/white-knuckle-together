using System.Collections.Generic;
using DarkMachine.UI;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The voice chat settings, in a "Voice Chat" tab next to the game's Video and Audio tabs: voice chat off entirely, the input
    /// mode (disabled, push to talk or voice activation), the microphone, a microphone test that plays it back, and the microphone's volume, the
    /// activation threshold with a live level meter and everyone else's volume. The tab is a copy of the Audio tab, emptied.
    /// </summary>
    internal static class WKTVoiceSettingsUI {
        private const string c_video_settings = "SettingsParent/Settings Pane/Video Settings";
        private const string c_tab_selector = "Controls Page Tab Selector";
        private const string c_tab_button_template = "Audio";
        private const string c_audio_tab = "Main Panel/Tab - Audio";
        private const string c_header_template = "Other/Gameplay Toggles";
        private const string c_toggle_template = "Other/MuteWhenNotFocused";
        private const string c_slider_template = "Audio/SliderAsset - Master Volume";
        private const string c_tab_name = "WKT Voice Chat";
        private const string c_tab_title = "VOICE CHAT";
        private const string c_hands_free_warning = "Bluetooth headset mic: opening it drops game audio to phone quality.";
        private const float c_warning_font_scale = 0.55f;
        private static readonly Color s_warningColor = new(1f, 0.55f, 0.4f);
        /// <summary>Indexed by <see cref="VoiceInputMode"/>.</summary>
        private static readonly string[] s_inputModes = { "Disabled", "Push to Talk", "Voice Activation" };

        public static void Add(Transform settingsMenu, WKTConfig config) {
            if (!WKTUIPath.TryFind(settingsMenu, c_video_settings, out var videoSettings, true)) return;
            if (!WKTUIPath.TryFind<UI_TabGroup>(videoSettings, c_tab_selector, out var tabGroup)) return;
            if (!WKTUIPath.TryFind<Button>(tabGroup.transform, c_tab_button_template, out var buttonTemplate)) return;
            if (!WKTUIPath.TryFind(videoSettings, c_audio_tab, out var audioTab)) return;
            if (!WKTUIPath.TryFind(audioTab, c_header_template, out var header)) return;
            if (!WKTUIPath.TryFind(audioTab, c_toggle_template, out var toggle)) return;
            if (!WKTUIPath.TryFind(audioTab, c_slider_template, out var slider)) return;
            var resolution = settingsMenu.GetComponentInChildren<Settings_Resolution>(true);
            if (resolution == null) {
                WKT.Instance.Logger.LogError("Found no dropdown in the settings menu to copy for voice chat.");
                return;
            }
            var dropdown = resolution.gameObject;
            var page = WKTSettingsRowCloner.CloneInert(audioTab.gameObject, c_tab_name);
            // The copied columns keep their look and layout; their rows go. A third column is not needed.
            for (var i = page.transform.childCount - 1; i >= 0; i--) {
                var copiedColumn = page.transform.GetChild(i);
                if (i >= 2) {
                    Object.DestroyImmediate(copiedColumn.gameObject);
                    continue;
                }
                for (var j = copiedColumn.childCount - 1; j >= 0; j--)
                    Object.DestroyImmediate(copiedColumn.GetChild(j).gameObject);
            }
            var column = page.transform.GetChild(0);
            var levels = page.transform.GetChild(1);
            WKTSettingsRowCloner.AddHeader(header.gameObject, column, "Voice Chat");
            // Not speaking is the Disabled input mode; this turns hearing off too.
            WKTSettingsRowCloner.AddToggle(toggle.gameObject, column, WKTSettingsInjector.TOGGLE_LABEL, "Completely Disable Voice Chat",
                !config.VoiceEnabled.Value, value => config.VoiceEnabled.Value = !value);
            WKTSettingsRowCloner.AddDropdown(dropdown, column, "Input Mode", s_inputModes, (int)config.VoiceInputMode.Value,
                index => config.VoiceInputMode.Value = (VoiceInputMode)index);
            var microphone = WKTSettingsRowCloner.AddDropdown(dropdown, column, "Microphone", WKTMicrophoneDropdown.DefaultOnly, 0, _ => { });
            var warning = WKTSettingsRowCloner.AddHeader(header.gameObject, column, c_hands_free_warning);
            warning.fontSize *= c_warning_font_scale;
            warning.color = s_warningColor;
            microphone.gameObject.AddComponent<WKTMicrophoneDropdown>().Setup(microphone, config, warning.gameObject);
            WKTSettingsRowCloner.AddToggle(toggle.gameObject, column, WKTSettingsInjector.TOGGLE_LABEL, "Noise Suppression", config.NoiseSuppression.Value,
                value => config.NoiseSuppression.Value = value);
            WKTSettingsRowCloner.AddToggle(toggle.gameObject, column, WKTSettingsInjector.TOGGLE_LABEL, "Auto Mic Level", config.AutoGain.Value,
                value => config.AutoGain.Value = value);
            var test = WKTSettingsRowCloner.AddToggle(toggle.gameObject, column, WKTSettingsInjector.TOGGLE_LABEL, "Test Microphone", false,
                WKTMicrophoneTestToggle.SetTesting);
            test.gameObject.AddComponent<WKTMicrophoneTestToggle>();
            WKTSettingsRowCloner.AddHeader(header.gameObject, levels, "Levels");
            WKTSettingsRowCloner.AddSlider(slider.gameObject, levels, "Mic Volume", 0f, WKTConfig.MAX_VOLUME, config.MicVolume.Value, FormatPercent,
                value => config.MicVolume.Value = value);
            var threshold = WKTSettingsRowCloner.AddSlider(slider.gameObject, levels, "Activation Threshold", WKTConfig.MIN_ACTIVATION_THRESHOLD_DB,
                WKTConfig.MAX_ACTIVATION_THRESHOLD_DB, config.ActivationThresholdDb.Value, FormatDb, value => config.ActivationThresholdDb.Value = value);
            WKTVoiceLevelMeter.Attach(threshold);
            WKTSettingsRowCloner.AddSlider(slider.gameObject, levels, "Voice Chat Volume", 0f, WKTConfig.MAX_VOLUME, config.VoiceChatVolume.Value,
                FormatPercent, value => config.VoiceChatVolume.Value = value);
            WKTSettingsInjector.AddSubTab(tabGroup, buttonTemplate, page, audioTab.parent, c_tab_name, c_tab_title);
        }

        public static string FormatPercent(float value) => Mathf.RoundToInt(value * 100f) + "%";

        private static string FormatDb(float value) => Mathf.RoundToInt(value) + " dB";
    }

    /// <summary>
    /// The microphone list, read again each time the settings show it, since microphones come and go. A warning under it shows
    /// while the picked microphone is a Bluetooth headset's (see <see cref="WKTVoiceRules.IsHandsFreeDevice"/>).
    /// </summary>
    internal sealed class WKTMicrophoneDropdown : MonoBehaviour {
        public const string DEFAULT_OPTION = "Default";
        public static readonly string[] DefaultOnly = { DEFAULT_OPTION };
        private readonly List<string> m_options = new();
        private TMP_Dropdown m_dropdown = null!;
        private GameObject m_handsFreeWarning = null!;
        /// <summary>Null until <see cref="Setup"/>, which runs right after the component wakes.</summary>
        private WKTConfig? m_config;

        public void Setup(TMP_Dropdown dropdown, WKTConfig config, GameObject handsFreeWarning) {
            m_dropdown = dropdown;
            m_config = config;
            m_handsFreeWarning = handsFreeWarning;
            m_dropdown.onValueChanged.AddListener(OnPicked);
            Refresh();
        }

        [UsedImplicitly]
        private void OnEnable() {
            if (m_config != null)
                Refresh();
        }

        private void Refresh() {
            m_options.Clear();
            m_options.Add(DEFAULT_OPTION);
            m_options.AddRange(Microphone.devices);
            var index = m_options.IndexOf(m_config!.VoiceInputDevice.Value);
            WKTSettingsRowCloner.SetOptions(m_dropdown, m_options, index < 0 ? 0 : index);
            UpdateWarning();
        }

        private void OnPicked(int index) {
            m_config!.VoiceInputDevice.Value = index <= 0 || index >= m_options.Count ? string.Empty : m_options[index];
            UpdateWarning();
        }

        private void UpdateWarning() =>
            m_handsFreeWarning.SetActive(WKTVoiceRules.IsHandsFreeDevice(WKTVoiceCapture.ResolveDevice(m_config!.VoiceInputDevice.Value)));
    }

    /// <summary>
    /// The microphone test plays the microphone back only while the settings show it. Switching tabs deactivates the tab,
    /// but closing the settings only makes their canvas group non-interactable, so both end the test.
    /// </summary>
    internal sealed class WKTMicrophoneTestToggle : MonoBehaviour {
        private Toggle m_toggle = null!;

        [UsedImplicitly]
        private void Awake() => m_toggle = GetComponent<Toggle>();

        [UsedImplicitly]
        private void OnDisable() => StopTesting();

        [UsedImplicitly]
        private void Update() {
            if (m_toggle.isOn && !m_toggle.IsInteractable())
                StopTesting();
        }

        public static void SetTesting(bool testing) {
            var voice = WKTVoiceManager.Instance;
            if (voice != null)
                voice.IsTestingMicrophone = testing;
        }

        private void StopTesting() {
            SetTesting(false);
            m_toggle.SetIsOnWithoutNotify(false);
        }
    }

    /// <summary>
    /// A bar inside the activation threshold slider showing how loud the microphone is right now, so the threshold can be set
    /// just above the room's noise. Lights up while the microphone would be sent.
    /// </summary>
    internal sealed class WKTVoiceLevelMeter : MonoBehaviour {
        private static readonly Color s_quiet = new(1f, 1f, 1f, 0.25f);
        private static readonly Color s_open = new(0.4f, 1f, 0.4f, 0.6f);
        private SubmitSlider m_slider = null!;
        private RectTransform m_bar = null!;
        private Image m_image = null!;

        public static void Attach(SubmitSlider slider) {
            var background = slider.transform.Find("Background");
            var parent = background != null ? background : slider.transform;
            var obj = new GameObject("WKT Level", typeof(RectTransform));
            var rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = obj.AddComponent<Image>();
            image.raycastTarget = false;
            var meter = slider.gameObject.AddComponent<WKTVoiceLevelMeter>();
            meter.m_slider = slider;
            meter.m_bar = rect;
            meter.m_image = image;
        }

        [UsedImplicitly]
        private void Update() {
            var voice = WKTVoiceManager.Instance;
            var level = voice != null ? voice.LocalLevelDb : WKTVoiceActivation.SILENCE_DB;
            var fill = Mathf.InverseLerp(m_slider.minValue, m_slider.maxValue, level);
            m_bar.anchorMax = new Vector2(fill, 1f);
            m_image.color = level >= m_slider.value ? s_open : s_quiet;
        }
    }
}
