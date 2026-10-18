using System;

namespace RvtMcp.Plugin.Views.Toast
{
    /// <summary>
    /// Tells the host dispatcher's unhandled-exception handler whether a fault came
    /// from toast work, and remembers that toasts are off once one did. Depth is
    /// touched only on the toast dispatcher thread; nested toast work is counted.
    /// </summary>
    internal sealed class ToastFaultGuard
    {
        private int _depth;
        private volatile bool _broken;

        /// <summary>True after a toast fault; the host then drops every post for the session.</summary>
        public bool IsBroken => _broken;

        public bool InToastWork => _depth > 0;

        public void Run(Action work)
        {
            _depth++;
            try
            {
                work();
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>
        /// Claims a dispatcher fault for the toast when toast work is on the stack, and
        /// marks the toast broken. Returns false for anyone else's exception.
        /// </summary>
        public bool TryClaim()
        {
            if (_depth <= 0)
                return false;
            _broken = true;
            return true;
        }
    }
}
