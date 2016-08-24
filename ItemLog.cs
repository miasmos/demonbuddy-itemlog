//!CompilerOption:AddRef:Trinity.dll
using ItemLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using Zeta.Bot;
using Zeta.Bot.Settings;
using Zeta.Common;
using Zeta.Common.Plugins;
using Zeta.Game;
using Zeta.Game.Internals.Actors;

namespace ItemLogD3Plugin
{
    class GugaPlugin : IPlugin
    {
        private int version = 1;
        private List<ACDItem> lastInventoryState;
        TextWriter log;
        bool init;
        int frameCount, frameCountMax;
        List<int> itemCache;
        List<int> unidentifiedCache;
        private static readonly string MyPath = Utilities.AssemblyDirectory;

        private Connection connection;
        private Thread mainThread;
        private Thread netThread;

        public string Author { get { return "Jimmy06 & MaiN"; } }
        public string Description { get { return "This plugin will log items to a file."; } }
        public string Name { get { return "ItemLog"; } }
        public Version Version { get { return new Version(version / 100, version % 100); } }
        public int VersionRaw { get { return version; } }
        public bool Equals(IPlugin other) { return other.Name == Name && other.Version == Version; }
        public Window DisplayWindow { get { return Config.GetDisplayWindow(); } }

        List<ACDItem> itemQueue;

        Settings settings = new Settings();

        private static readonly log4net.ILog Logging = Logger.GetLoggerInstanceForType();

        public void OnInitialize() { }
        public void OnEnabled()
        {
            LogMessage("Enabled");

            connection = new Connection(this);
            LogMessage("Connecting to server and logging in...");
            connection.Connect();

            if (!connection.IsConnected())
            {
                connection.Disconnect();
                return;
            }
            LogMessage("Connected to server");
            GameEvents.OnGameJoined += OnGameJoined;
            mainThread = new Thread(MainThreadProc);
            mainThread.Start();
            // netThread = new Thread(NetThreadProc);
            // netThread.Start();
            itemQueue = new List<ACDItem>();

            LogMessage("Website:  www.buddystats.com  ");
            LogMessage("[ON]");


            init = false;
            GameEvents.OnGameJoined += OnGameJoined;
            Demonbuddy.App.Current.Dispatcher.ShutdownStarted += onExit;
        }

        public void OnDisabled()
        {
            if (connection != null)
            {
                connection.Disconnect();
                if (mainThread != null && mainThread.IsAlive)
                    mainThread.Abort();
                // if (netThread != null && netThread.IsAlive)
                //     netThread.Abort();
            }
            LogMessage("[OFF]");

            GameEvents.OnGameJoined -= OnGameJoined;
            Demonbuddy.App.Current.Dispatcher.ShutdownStarted -= onExit;
            init = false;
        }

        public void OnGameJoined(object sender, EventArgs args)
        {
            init = false;
        }

        public void onExit(object o, EventArgs e)
        {
            if (connection != null)
            {
                connection.Disconnect();
                if (mainThread != null && mainThread.IsAlive)
                    mainThread.Abort();
                // if (netThread != null && netThread.IsAlive)
                //     netThread.Abort();
            }
            GameEvents.OnGameJoined -= OnGameJoined;
            Demonbuddy.App.Current.Dispatcher.ShutdownStarted -= onExit;
        }

        public void OnShutdown()
        {
            if (connection != null)
            {
                connection.Disconnect();
                if (mainThread != null && mainThread.IsAlive)
                    mainThread.Abort();
                // if (netThread != null && netThread.IsAlive)
                //     netThread.Abort();
            }
            
            LogMessage("[OFF]");
            GameEvents.OnGameJoined -= OnGameJoined;
            Demonbuddy.App.Current.Dispatcher.ShutdownStarted -= onExit;
        }

        public void OnPulse()
        {
            if (!ZetaDia.IsInGame || !ZetaDia.Me.IsValid)
                return;

            if (!init)
            {
                ResetCache();
            }

            if (frameCount++ >= frameCountMax)
            {
                CheckItemsDropped();
                frameCount = 0;
            }
        }

        public bool isMainWindowClosed()
        {
            Process db = Process.GetCurrentProcess();
            IntPtr h = db.MainWindowHandle;
            return (h == IntPtr.Zero || h == null);
        }

        public void MainThreadProc()
        {
            DateTime lastSentAlive = DateTime.Now;
            DateTime lastConnect = DateTime.Now;
            int connTriesCount = 0;
            while (!isMainWindowClosed())
            {
                if (!connection.IsConnected())
                {
                    if (DateTime.Now.Subtract(lastConnect).TotalSeconds > 10)
                    {
                        LogMessage("Trying to reconnect...");
                        connTriesCount++;
                        connection.Disconnect();
                        connection.Connect();
                        if (connection.IsConnected())
                            LogMessage("Reconnecting has been succesful");
                        lastConnect = DateTime.Now;
                    }
                }
                else if ((!BotMain.IsRunning) && DateTime.Now.Subtract(lastSentAlive).TotalSeconds > 30)
                {
                    try
                    {
                        if (!connection.SendIsAlive())
                            connection.Disconnect();
                        lastSentAlive = DateTime.Now;
                    }
                    catch (Exception e)
                    {
                        LogMessage("ERROR IN MAIN THREAD:" + e.StackTrace);
                    }
                }
                Thread.Sleep(1000);
            }
        }

        // public void NetThreadProc()
        // {
        //     DateTime lastSentAlive = DateTime.Now;
        //     DateTime lastConnect = DateTime.Now;

        //     while (!isMainWindowClosed())
        //     {
        //         if (connection.IsConnected())
        //         {
        //             foreach(ACDItem item in itemQueue) {
        //                 connection.SendItemDrop(item);
        //                 // itemQueue.Remove(item);
        //             }
        //         }
        //         Thread.Sleep(1000);
        //     }
        // }

        private void ResetCache()
        {
            LogMessage("Reset Cache");
            lastInventoryState = null;
            frameCount = 0;
            frameCountMax = 15;
            unidentifiedCache = new List<int>();
            itemCache = new List<int>();
            foreach (ACDItem item in ZetaDia.Me.Inventory.Backpack)
            {
                if (!item.IsUnidentified)
                {
                    cacheItem(item);
                }
            }
            foreach (ACDItem item in ZetaDia.Me.Inventory.StashItems)
            {
                if (!item.IsUnidentified)
                {
                    cacheItem(item);
                }
            }
            foreach (ACDItem item in ZetaDia.Me.Inventory.Equipped)
            {
                cacheItem(item);
            }
            init = true;
        }

        public void CheckItemsDropped()
        {
            if (!init)
            {
                return;
            }

            if (lastInventoryState == null)
            {
                lastInventoryState = new List<ACDItem>(ZetaDia.Me.Inventory.Backpack);
                return;
            }

            foreach (ACDItem item in ZetaDia.Me.Inventory.Backpack)
            {
                if (item.IsUnidentified && !unidentifiedCache.Contains(item.AnnId))
                {
                    uCacheItem(item);
                    continue;
                }

                bool found = false;
                foreach (ACDItem itemLastState in lastInventoryState)
                {
                    if (item.AnnId == itemLastState.AnnId)
                    {
                        found = true;
                        break;
                    }
                }

                bool newlyIdentified = unidentifiedCache.Contains(item.AnnId) && !item.IsUnidentified;

                if ((!found && !itemCache.Contains(item.AnnId)) || newlyIdentified)
                {
                    LogMessage("Cached " + item.Name);
                    OnItemFound(item, newlyIdentified);
                }
            }
            

            lastInventoryState = new List<ACDItem>(ZetaDia.Me.Inventory.Backpack);
            
        }

        public void OnItemFound(ACDItem item, bool newlyIdentified)
        {
            if (newlyIdentified && !statsHaveLoaded(item))
            {
                return;
            }

            cacheItem(item);
            if (unidentifiedCache.Contains(item.AnnId))
            {
                unidentifiedCache.Remove(item.AnnId);
            }

            connection.SendItemDrop(item);
            // itemQueue.Add(item);
            LogMessage("Looted " + item.Name + " (" + item.AnnId.ToString() + ")");
        }

        public void cacheItem(ACDItem item)
        {
            itemCache.Add(item.AnnId);
        }

        public void uCacheItem(ACDItem item)
        {
            LogMessage("Cached unidentified " + item.Name);
            unidentifiedCache.Add(item.AnnId);
        }

        public void LogMessage(string msg)
        {
            Logging.Notice("[" + Name + "]:  " + msg);
        }

        private bool statsHaveLoaded(ACDItem item)
        {
            if (item.Stats == null)
            {
                return false;
            }
            LogMessage("Warning: Stats haven't loaded for " + item.AnnId);
            return true;
        }
    }
}
