using System;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    /// <summary>
    /// Exception thrown by sync backends to carry structured error codes.
    /// </summary>
    public class SyncBackendException : Exception
    {
        public SyncBackendCode Code { get; }

        public SyncBackendException(SyncBackendCode code, string message, Exception inner = null)
            : base(message, inner)
        {
            Code = code;
        }
    }
}
