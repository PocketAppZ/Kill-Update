﻿namespace KillUpdate
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.IO;
    using System.Reflection;
    using System.ServiceProcess;
    using System.Threading;
    using System.Windows.Input;
    using System.Windows.Threading;
    using RegistryTools;
    using TaskbarIconHost;
    using Tracing;
    using ZombifyMe;

    /// <summary>
    /// Represents a plugin that disables Windows Update.
    /// </summary>
    public class KillUpdatePlugin : IPluginClient, IDisposable
    {
        #region Plugin
        /// <summary>
        /// Gets the plugin name.
        /// </summary>
        public string Name
        {
            get { return "Kill-Update"; }
        }

        /// <summary>
        /// Gets the plugin unique ID.
        /// </summary>
        public Guid Guid
        {
            get { return new Guid("{FFFFFFFF-E9B9-4E5D-AD4C-489A017748A5}"); }
        }

        /// <summary>
        /// Gets the plugin assembly name.
        /// </summary>
        public string AssemblyName { get; } = "Kill-Update";

        /// <summary>
        ///  Gets a value indicating whether the plugin require elevated (administrator) mode to operate.
        /// </summary>
        public bool RequireElevated
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether the plugin want to handle clicks on the taskbar icon.
        /// </summary>
        public bool HasClickHandler
        {
            get { return false; }
        }

        /// <summary>
        /// Called once at startup, to initialize the plugin.
        /// </summary>
        /// <param name="isElevated">True if the caller is executing in administrator mode.</param>
        /// <param name="dispatcher">A dispatcher that can be used to synchronize with the UI.</param>
        /// <param name="settings">An interface to read and write settings in the registry.</param>
        /// <param name="logger">An interface to log events asynchronously.</param>
        public void Initialize(bool isElevated, Dispatcher dispatcher, Settings settings, ITracer logger)
        {
            IsElevated = isElevated;
            Dispatcher = dispatcher;
            Settings = settings;
            Logger = logger;

            InitServiceManager();

            InitializeCommand("Locked",
                              isVisibleHandler: () => true,
                              isEnabledHandler: () => StartType.HasValue && IsElevated,
                              isCheckedHandler: () => IsLockEnabled,
                              commandHandler: OnCommandLock);

            InitZombification();
        }

        private void InitializeCommand(string header, Func<bool> isVisibleHandler, Func<bool> isEnabledHandler, Func<bool> isCheckedHandler, Action commandHandler)
        {
            ICommand Command = new RoutedUICommand();
            CommandList.Add(Command);
            MenuHeaderTable.Add(Command, header);
            MenuIsVisibleTable.Add(Command, isVisibleHandler);
            MenuIsEnabledTable.Add(Command, isEnabledHandler);
            MenuIsCheckedTable.Add(Command, isCheckedHandler);
            MenuHandlerTable.Add(Command, commandHandler);
        }

        /// <summary>
        /// Gets the list of commands that the plugin can receive when an item is clicked in the context menu.
        /// </summary>
        public List<ICommand> CommandList { get; private set; } = new List<ICommand>();

        /// <summary>
        /// Reads a flag indicating if the state of a menu item has changed. The flag should be reset upon return until another change occurs.
        /// </summary>
        /// <param name="beforeMenuOpening">True if this function is called right before the context menu is opened by the user; otherwise, false.</param>
        /// <returns>True if a menu item state has changed since the last call; otherwise, false.</returns>
        public bool GetIsMenuChanged(bool beforeMenuOpening)
        {
            bool Result = IsMenuChanged;
            IsMenuChanged = false;

            return Result;
        }

        /// <summary>
        /// Reads the text of a menu item associated to command.
        /// </summary>
        /// <param name="command">The command associated to the menu item.</param>
        /// <returns>The menu text.</returns>
        public string GetMenuHeader(ICommand command)
        {
            return MenuHeaderTable[command];
        }

        /// <summary>
        /// Reads the state of a menu item associated to command.
        /// </summary>
        /// <param name="command">The command associated to the menu item.</param>
        /// <returns>True if the menu item should be visible to the user, false if it should be hidden.</returns>
        public bool GetMenuIsVisible(ICommand command)
        {
            return MenuIsVisibleTable[command]();
        }

        /// <summary>
        /// Reads the state of a menu item associated to command.
        /// </summary>
        /// <param name="command">The command associated to the menu item.</param>
        /// <returns>True if the menu item should appear enabled, false if it should be disabled.</returns>
        public bool GetMenuIsEnabled(ICommand command)
        {
            return MenuIsEnabledTable[command]();
        }

        /// <summary>
        /// Reads the state of a menu item associated to command.
        /// </summary>
        /// <param name="command">The command associated to the menu item.</param>
        /// <returns>True if the menu item is checked, false otherwise.</returns>
        public bool GetMenuIsChecked(ICommand command)
        {
            return MenuIsCheckedTable[command]();
        }

        /// <summary>
        /// Reads the icon of a menu item associated to command.
        /// </summary>
        /// <param name="command">The command associated to the menu item.</param>
        /// <returns>The icon to display with the menu text, null if none.</returns>
        public Bitmap? GetMenuIcon(ICommand command)
        {
            return null;
        }

        /// <summary>
        /// This method is called before the menu is displayed, but after changes in the menu have been evaluated.
        /// </summary>
        public void OnMenuOpening()
        {
        }

        /// <summary>
        /// Requests for command to be executed.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        public void OnExecuteCommand(ICommand command)
        {
            MenuHandlerTable[command]();
        }

        private void OnCommandLock()
        {
            bool LockIt = !IsLockEnabled;

            Settings.SetBool(LockedSettingName, LockIt);
            ChangeLockMode(LockIt);
            IsMenuChanged = true;
        }

        /// <summary>
        /// Reads a flag indicating if the plugin icon, that might reflect the state of the plugin, has changed.
        /// </summary>
        /// <returns>True if the icon has changed since the last call, false otherwise.</returns>
        public bool GetIsIconChanged()
        {
            bool Result = IsIconChanged;
            IsIconChanged = false;

            return Result;
        }

        /// <summary>
        /// Gets the icon displayed in the taskbar.
        /// </summary>
        public Icon Icon
        {
            get
            {
                Icon Result;

                if (IsElevated)
                    if (IsLockEnabled)
                        LoadEmbeddedResource("Locked-Enabled.ico", out Result);
                    else
                        LoadEmbeddedResource("Unlocked-Enabled.ico", out Result);
                else
                    if (IsLockEnabled)
                        LoadEmbeddedResource("Locked-Disabled.ico", out Result);
                    else
                        LoadEmbeddedResource("Unlocked-Disabled.ico", out Result);

                return Result;
            }
        }

        /// <summary>
        /// Gets the bitmap displayed in the preferred plugin menu.
        /// </summary>
        public Bitmap SelectionBitmap
        {
            get
            {
                LoadEmbeddedResource("Kill-Update.png", out Bitmap Result);
                return Result;
            }
        }

        /// <summary>
        /// Requests for the main plugin operation to be executed.
        /// </summary>
        public void OnIconClicked()
        {
        }

        /// <summary>
        /// Reads a flag indicating if the plugin tooltip, that might reflect the state of the plugin, has changed.
        /// </summary>
        /// <returns>True if the tooltip has changed since the last call, false otherwise.</returns>
        public bool GetIsToolTipChanged()
        {
            return false;
        }

        /// <summary>
        /// Gets the free text that indicate the state of the plugin.
        /// </summary>
        public string ToolTip
        {
            get
            {
                if (IsElevated)
                    return "Lock/Unlock Windows updates";
                else
                    return "Lock/Unlock Windows updates (Requires administrator mode)";
            }
        }

        /// <summary>
        /// Called when the taskbar is getting the application focus.
        /// </summary>
        public void OnActivated()
        {
        }

        /// <summary>
        /// Called when the taskbar is loosing the application focus.
        /// </summary>
        public void OnDeactivated()
        {
        }

        /// <summary>
        /// Requests to close and terminate a plugin.
        /// </summary>
        /// <param name="canClose">True if no plugin called before this one has returned false, false if one of them has.</param>
        /// <returns>True if the plugin can be safely terminated, false if the request is denied.</returns>
        public bool CanClose(bool canClose)
        {
            return true;
        }

        /// <summary>
        /// Requests to begin closing the plugin.
        /// </summary>
        public void BeginClose()
        {
            ExitZombification();
            StopServiceManager();
        }

        /// <summary>
        /// Gets a value indicating whether the plugin is closed.
        /// </summary>
        public bool IsClosed
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether the caller is executing in administrator mode.
        /// </summary>
        public bool IsElevated { get; private set; }

        /// <summary>
        /// Gets a dispatcher that can be used to synchronize with the UI.
        /// </summary>
        public Dispatcher Dispatcher { get; private set; } = null !;

        /// <summary>
        /// Gets an interface to read and write settings in the registry.
        /// </summary>
        public Settings Settings { get; private set; } = null!;

        /// <summary>
        /// Gets an interface to log events asynchronously.
        /// </summary>
        public ITracer Logger { get; private set; } = null!;

        private void AddLog(string message)
        {
            Logger.Write(Category.Information, message);
        }

        private bool LoadEmbeddedResource<T>(string resourceName, out T resource)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string ResourcePath = string.Empty;

            // Loads an "Embedded Resource" of type T (ex: Bitmap for a PNG file).
            // Make sure the resource is tagged as such in the resource properties.
            foreach (string Item in assembly.GetManifestResourceNames())
                if (Item.EndsWith(resourceName, StringComparison.InvariantCulture))
                    ResourcePath = Item;

            // If not found, it could be because it's not tagged as "Embedded Resource".
            if (ResourcePath.Length == 0)
            {
                AddLog($"Resource {resourceName} not found");
                resource = default!;
                return false;
            }

            using Stream rs = assembly.GetManifestResourceStream(ResourcePath);
            resource = (T)Activator.CreateInstance(typeof(T), rs);

            AddLog($"Resource {resourceName} loaded");
            return true;
        }

        private Dictionary<ICommand, string> MenuHeaderTable = new Dictionary<ICommand, string>();
        private Dictionary<ICommand, Func<bool>> MenuIsVisibleTable = new Dictionary<ICommand, Func<bool>>();
        private Dictionary<ICommand, Func<bool>> MenuIsEnabledTable = new Dictionary<ICommand, Func<bool>>();
        private Dictionary<ICommand, Func<bool>> MenuIsCheckedTable = new Dictionary<ICommand, Func<bool>>();
        private Dictionary<ICommand, Action> MenuHandlerTable = new Dictionary<ICommand, Action>();
        private bool IsIconChanged;
        private bool IsMenuChanged;
        #endregion

        #region Service Manager
        private void InitServiceManager()
        {
            AddLog("InitServiceManager starting");

            if (Settings.IsValueSet(LockedSettingName))
                if (IsSettingLock)
                    StartType = ServiceStartMode.Disabled;
                else
                    StartType = ServiceStartMode.Manual;

            UpdateTimer = new Timer(new TimerCallback(UpdateTimerCallback));
            FullRestartTimer = new Timer(new TimerCallback(FullRestartTimerCallback));
            UpdateWatch = new Stopwatch();
            UpdateWatch.Start();

            OnUpdate();

            UpdateTimer.Change(CheckInterval, CheckInterval);
            FullRestartTimer.Change(FullRestartInterval, Timeout.InfiniteTimeSpan);

            AddLog("InitServiceManager done");
        }

        private bool IsLockEnabled { get { return StartType.HasValue && StartType == ServiceStartMode.Disabled; } }
        private bool IsSettingLock
        {
            get
            {
                Settings.GetBool(LockedSettingName, true, out bool Value);
                return Value;
            }
        }

        private void StopServiceManager()
        {
            FullRestartTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            FullRestartTimer = null;
            UpdateTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            UpdateTimer = null;
        }

        private void UpdateTimerCallback(object parameter)
        {
            // Protection against reentering too many times after a sleep/wake up.
            // There must be at most two pending calls to OnUpdate in the dispatcher.
            int NewTimerDispatcherCount = Interlocked.Increment(ref TimerDispatcherCount);
            if (NewTimerDispatcherCount > 2)
            {
                Interlocked.Decrement(ref TimerDispatcherCount);
                return;
            }

            // For debug purpose.
            LastTotalElapsed = Math.Round(UpdateWatch.Elapsed.TotalSeconds, 0);

            Dispatcher.BeginInvoke(new OnUpdateHandler(OnUpdate));
        }

        private void FullRestartTimerCallback(object parameter)
        {
            if (UpdateTimer != null)
            {
                AddLog("Restarting the timer");

                // Restart the update timer from scratch.
                UpdateTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                UpdateTimer = new Timer(new TimerCallback(UpdateTimerCallback));
                UpdateTimer.Change(CheckInterval, CheckInterval);

                AddLog("Timer restarted");
            }
            else
                AddLog("No timer to restart");

            FullRestartTimer?.Change(FullRestartInterval, Timeout.InfiniteTimeSpan);
            AddLog($"Next check scheduled at {DateTime.UtcNow + FullRestartInterval}");
        }

        private int TimerDispatcherCount = 1;
        private double LastTotalElapsed = double.NaN;

        private delegate void OnUpdateHandler();
        private void OnUpdate()
        {
            try
            {
                AddLog("%% Running timer callback");

                int LastTimerDispatcherCount = Interlocked.Decrement(ref TimerDispatcherCount);
                UpdateWatch.Restart();

                AddLog($"Watch restarted, Elapsed = {LastTotalElapsed}, pending count = {LastTimerDispatcherCount}");

                Settings.RenewKey();

                AddLog("Key renewed");

                ServiceStartMode? PreviousStartType = StartType;
                bool LockIt = IsSettingLock;

                AddLog("Lock setting read");

                ServiceController[] Services = ServiceController.GetServices();
                if (Services == null)
                    AddLog("Failed to get services");
                else
                {
                    AddLog($"Found {Services.Length} service(s)");

                    foreach (ServiceController Service in Services)
                        if (Service.ServiceName == WindowsUpdateServiceName)
                        {
                            AddLog($"Checking {Service.ServiceName}");

                            StartType = Service.StartType;

                            AddLog($"Current start type: {StartType}");

                            if (PreviousStartType.HasValue && PreviousStartType.Value != StartType.Value)
                            {
                                AddLog("Start type changed");

                                ChangeLockMode(Service, LockIt);
                            }

                            StopIfRunning(Service, LockIt);

                            PreviousStartType = StartType;
                            break;
                        }
                }

                ZombifyMe.Zombification.SetAlive();

                AddLog("%% Timer callback completed");
            }
            catch (Exception e)
            {
                AddLog($"(from OnUpdate) {e.Message}");
            }
        }

        private void ChangeLockMode(bool lockIt)
        {
            AddLog("ChangeLockMode starting");

            try
            {
                using ServiceController Service = new ServiceController(WindowsUpdateServiceName);

                if (IsElevated)
                    ChangeLockMode(Service, lockIt);
                else
                    AddLog("Not elevated, cannot change");
            }
            catch (Exception e)
            {
                AddLog($"(from ChangeLockMode) {e.Message}");
            }
        }

        private void ChangeLockMode(ServiceController service, bool lockIt)
        {
            IsIconChanged = true;
            ServiceStartMode NewStartType = lockIt ? ServiceStartMode.Disabled : ServiceStartMode.Manual;
            NativeMethods.ChangeStartMode(service, NewStartType, out _);

            StartType = NewStartType;
            AddLog($"Service type={StartType}");
        }

        private void StopIfRunning(ServiceController service, bool lockIt)
        {
            if (lockIt && service.Status == ServiceControllerStatus.Running && service.CanStop)
            {
                AddLog("Stopping service");
                service.Stop();
                AddLog("Service stopped");
            }
        }

        private const string WindowsUpdateServiceName = "wuauserv";
        private const string LockedSettingName = "Locked";
        private readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);
        private readonly TimeSpan FullRestartInterval = TimeSpan.FromHours(1);
        private ServiceStartMode? StartType;
        private Timer? UpdateTimer;
        private Timer? FullRestartTimer;
        private Stopwatch UpdateWatch = null!;
        #endregion

        #region Zombification
        private static bool IsRestart { get { return Zombification.IsRestart; } }

        private void InitZombification()
        {
            AddLog("InitZombification starting");

            if (IsRestart)
                AddLog("This process has been restarted");

            Zombification = new Zombification("Kill-Update");
            Zombification.Delay = TimeSpan.FromMinutes(1);
            Zombification.WatchingMessage = string.Empty;
            Zombification.RestartMessage = string.Empty;
            Zombification.Flags = Flags.NoWindow | Flags.ForwardArguments;
            Zombification.IsSymmetric = true;
            Zombification.AliveTimeout = TimeSpan.FromMinutes(1);
            Zombification.ZombifyMe();

            AddLog("InitZombification done");
        }

        private void ExitZombification()
        {
            AddLog("ExitZombification starting");

            if (Zombification != null)
            {
                Zombification.Cancel();
                Zombification = null !;
            }

            AddLog("ExitZombification done");
        }

        private Zombification Zombification = null!;
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
        /// Finalizes an instance of the <see cref="KillUpdatePlugin"/> class.
        /// </summary>
        ~KillUpdatePlugin()
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
            using (Settings)
            {
            }

            using (FullRestartTimer)
            {
            }

            using (UpdateTimer)
            {
            }
        }
        #endregion
    }
}