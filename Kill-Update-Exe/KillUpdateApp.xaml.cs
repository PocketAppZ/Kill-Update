﻿namespace KillUpdate
{
    using System;
    using System.Windows;
    using TaskbarIconHost;

    /// <summary>
    /// Represents an application that disables Windows Update.
    /// </summary>
    public partial class KillUpdateApp : Application, IDisposable
    {
        #region Init
        /// <summary>
        /// Initializes a new instance of the <see cref="KillUpdateApp"/> class.
        /// </summary>
        public KillUpdateApp()
        {
            Plugin = new KillUpdatePlugin();
            PluginApp = new App(this, Plugin, Plugin.AssemblyName);
        }

        private KillUpdatePlugin Plugin;
        private App PluginApp;
        #endregion

        #region Implementation of IDisposable
        /// <summary>
        /// Called when an object should release its resources.
        /// </summary>
        /// <param name="isDisposing">Indicates if resources must be disposed now.</param>
        protected virtual void Dispose(bool isDisposing)
        {
            if (!IsDisposed)
            {
                IsDisposed = true;

                if (isDisposing)
                    DisposeNow();
            }
        }

        /// <summary>
        /// Called when an object should release its resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="KillUpdateApp"/> class.
        /// </summary>
        ~KillUpdateApp()
        {
            Dispose(false);
        }

        /// <summary>
        /// True after <see cref="Dispose(bool)"/> has been invoked.
        /// </summary>
        private bool IsDisposed;

        /// <summary>
        /// Disposes of every reference that must be cleaned up.
        /// </summary>
        private void DisposeNow()
        {
            using (Plugin)
            {
            }

            using (PluginApp)
            {
            }
        }
        #endregion
    }
}
