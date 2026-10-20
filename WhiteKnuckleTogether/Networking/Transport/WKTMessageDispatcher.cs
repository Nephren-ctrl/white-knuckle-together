using System;
using System.Runtime.InteropServices;
using WhiteKnuckleTogether.Networking.LiteNetLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Copies incoming native message data into a reused buffer and routes it to the handler registered for its
    /// <see cref="WKTMessageId"/>. Handler exceptions are contained so one bad message can never break the receive pump.
    /// Single-threaded: only use from the main thread.
    /// </summary>
    public sealed class WKTMessageDispatcher {
        private const int c_initial_buffer_size = 1024;
        private const byte c_first_gameplay_id = (byte)WKTMessageId.StartRun;
        private readonly WKTMessageHandler?[] m_handlers = new WKTMessageHandler?[byte.MaxValue + 1];
        private readonly NetDataReader m_reader = new();
        private readonly Action<string>? m_errorSink;
        private byte[] m_buffer = new byte[c_initial_buffer_size];

        public WKTMessageDispatcher(Action<string>? errorSink = null) => m_errorSink = errorSink;

        /// <summary>Reserved ids belong to the handshake and are handled by the server and client themselves.</summary>
        public static bool IsReserved(WKTMessageId id) => (byte)id < c_first_gameplay_id;

        /// <summary>Writes the message header. Call before writing the payload.</summary>
        public static void BeginMessage(NetDataWriter writer, WKTMessageId id) {
            writer.Reset();
            writer.Put((byte)id);
        }

        public void Register(WKTMessageId id, WKTMessageHandler handler) {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (IsReserved(id))
                throw new ArgumentException($"Message id {id} is reserved for the handshake.", nameof(id));
            if (m_handlers[(byte)id] != null)
                throw new InvalidOperationException($"A handler for message id {id} is already registered.");
            m_handlers[(byte)id] = handler;
        }

        public bool Unregister(WKTMessageId id) {
            if (m_handlers[(byte)id] == null) return false;
            m_handlers[(byte)id] = null;
            return true;
        }

        public bool IsRegistered(WKTMessageId id) => m_handlers[(byte)id] != null;

        public void Clear() => Array.Clear(m_handlers, 0, m_handlers.Length);

        /// <summary>
        /// Copies native message data into the internal buffer and returns a reader over it. Data larger than
        /// <see cref="WKTNetworkConsts.MAX_MESSAGE_SIZE"/> or with a negative size yields an empty reader.
        /// </summary>
        public NetDataReader Load(IntPtr data, int size) {
            if (size < 0 || size > WKTNetworkConsts.MAX_MESSAGE_SIZE)
                size = 0;
            EnsureCapacity(size);
            if (size > 0)
                Marshal.Copy(data, m_buffer, 0, size);
            m_reader.SetSource(m_buffer, 0, size);
            return m_reader;
        }

        /// <summary>Managed counterpart of <see cref="Load(IntPtr, int)"/>.</summary>
        public NetDataReader Load(byte[] data, int size) {
            if (size < 0 || size > data.Length)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (size > WKTNetworkConsts.MAX_MESSAGE_SIZE)
                size = 0;
            EnsureCapacity(size);
            Buffer.BlockCopy(data, 0, m_buffer, 0, size);
            m_reader.SetSource(m_buffer, 0, size);
            return m_reader;
        }

        /// <summary>Reads the message id from the reader, then dispatches the rest of the message.</summary>
        public WKTDispatchResult Dispatch(WKTPlayer sender, NetDataReader reader) {
            if (!reader.TryGetByte(out var id))
                return WKTDispatchResult.Empty;
            return Dispatch(sender, (WKTMessageId)id, reader);
        }

        /// <summary>Dispatches a message whose id has already been read.</summary>
        public WKTDispatchResult Dispatch(WKTPlayer sender, WKTMessageId id, NetDataReader reader) {
            if (IsReserved(id))
                return WKTDispatchResult.Reserved;
            var handler = m_handlers[(byte)id];
            if (handler == null)
                return WKTDispatchResult.NoHandler;
            try {
                handler(sender, reader);
                return WKTDispatchResult.Handled;
            }
            catch (Exception e) {
                m_errorSink?.Invoke($"Handler for message {id} from player {sender.Id} failed: {e}");
                return WKTDispatchResult.HandlerFailed;
            }
        }

        private void EnsureCapacity(int size) {
            if (m_buffer.Length >= size) return;
            var newSize = m_buffer.Length;
            while (newSize < size)
                newSize *= 2;
            m_buffer = new byte[newSize];
        }
    }
}
