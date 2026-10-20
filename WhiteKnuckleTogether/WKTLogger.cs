using System.Text;
using UnityEngine;

namespace WhiteKnuckleTogether {
    internal sealed class WKTLogger {
        private const string c_plugin_name = "[WKT] ";
        private readonly StringBuilder m_sb = new();

        public void LogInfo(string message) => Log(LogType.Info, message);
        
        public void LogWarning(string message) => Log(LogType.Warning, message);
        
        public void LogError(string message) => Log(LogType.Error, message);

        private void Log(LogType type, string message) {
            m_sb.Append(c_plugin_name);
            m_sb.Append(message);
            switch (type) {
                case LogType.Info:
                    Debug.Log(m_sb.ToString());
                    break;
                case LogType.Warning:
                    Debug.LogWarning(m_sb.ToString());
                    break;
                case LogType.Error:
                    Debug.LogError(m_sb.ToString());
                    break;
            }
            m_sb.Clear();
        }

        private enum LogType : byte {
            Info,
            Warning,
            Error
        }
    }
}