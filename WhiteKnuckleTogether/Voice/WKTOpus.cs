using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Calls into libopus (Native/opus.dll, built by tools/build-opus.ps1). A pure C# port was tried first and allocated
    /// over 100 KB per frame; libopus allocates nothing per call. Arrays are passed pinned, without copies.
    /// </summary>
    internal static class WKTOpus {
        public const string LIBRARY = "opus";
        public const int OK = 0;
        public const int APPLICATION_VOIP = 2048;
        public const int SET_BITRATE = 4002;
        public const int SET_MAX_BANDWIDTH = 4004;
        public const int SET_COMPLEXITY = 4010;
        public const int SET_INBAND_FEC = 4012;
        public const int SET_PACKET_LOSS_PERC = 4014;
        public const int SET_SIGNAL = 4024;
        public const int SIGNAL_VOICE = 3001;
        public const int BANDWIDTH_FULLBAND = 1105;
        private static bool s_loaded;

        /// <summary>
        /// Loads opus.dll from next to the mod's DLL. Windows only searches the game's own folders for native libraries, but
        /// once a library of that name is loaded every later call finds it. False, logged by the caller, if it cannot load.
        /// </summary>
        public static bool TryLoad(out string error) {
            error = string.Empty;
            if (s_loaded) return true;
            var directory = Path.GetDirectoryName(typeof(WKTOpus).Assembly.Location) ?? string.Empty;
            var path = Path.Combine(directory, LIBRARY + ".dll");
            if (!File.Exists(path)) {
                error = "missing " + path;
                return false;
            }
            if (LoadLibrary(path) == IntPtr.Zero) {
                error = $"could not load {path} (error {Marshal.GetLastWin32Error()})";
                return false;
            }
            s_loaded = true;
            return true;
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string path);

        [DllImport(LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern EncoderHandle opus_encoder_create(int sampleRate, int channels, int application, out int error);

        [DllImport(LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_encode_float(EncoderHandle encoder, float[] pcm, int frameSize, byte[] data, int maxDataBytes);

        /// <summary>A variadic C function. On x64 an int argument is passed exactly like a fixed one.</summary>
        [DllImport(LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_encoder_ctl(EncoderHandle encoder, int request, int value);

        [DllImport(LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        private static extern void opus_encoder_destroy(IntPtr encoder);

        [DllImport(LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern DecoderHandle opus_decoder_create(int sampleRate, int channels, out int error);

        /// <summary><paramref name="data"/> null with length 0 conceals a lost frame.</summary>
        [DllImport(LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_decode_float(DecoderHandle decoder, byte[]? data, int length, float[] pcm, int frameSize, int decodeFec);

        [DllImport(LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        private static extern void opus_decoder_destroy(IntPtr decoder);

        /// <summary>A native encoder, destroyed with its handle.</summary>
        public sealed class EncoderHandle : SafeHandleZeroOrMinusOneIsInvalid {
            public EncoderHandle() : base(true) { }

            protected override bool ReleaseHandle() {
                opus_encoder_destroy(handle);
                return true;
            }
        }

        /// <summary>A native decoder, destroyed with its handle.</summary>
        public sealed class DecoderHandle : SafeHandleZeroOrMinusOneIsInvalid {
            public DecoderHandle() : base(true) { }

            protected override bool ReleaseHandle() {
                opus_decoder_destroy(handle);
                return true;
            }
        }
    }
}
