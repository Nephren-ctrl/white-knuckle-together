using System;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

namespace WhiteKnuckleTogether {
    internal sealed class WKTToggleBinder : MonoBehaviour {
        private Toggle? m_toggle;
        private Action<bool>? m_event;
        private bool m_initialized;

        [UsedImplicitly]
        private void OnDestroy() => m_event = default;

        public void Bind(Action<bool> action) {
            m_event += action;
            TryLazyInitialize();
            OnToggleChanged(m_toggle.OrNull()?.isOn ?? false);
        }

        public void Unbind(Action<bool> action) {
            m_event -= action;
        }

        private void OnToggleChanged(bool value) {
            m_event?.Invoke(value);
            WKT.Instance.Logger.LogInfo($"Toggling {name} to {value}");
        }

        private void TryLazyInitialize() {
            if (m_initialized) return;
            m_initialized = true;
            m_toggle = GetComponent<Toggle>();
            m_toggle.onValueChanged.AddListener(OnToggleChanged);
        }
    }
}