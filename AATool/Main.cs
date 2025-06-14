﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using AATool.Configuration;
using AATool.Data;
using AATool.Data.Speedrunning;
using AATool.Graphics;
using AATool.Net;
using AATool.Net.Requests;
using AATool.Saves;
using AATool.UI.Screens;
using AATool.Utilities;
using AATool.Winforms.Forms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AATool
{
    public class Main : Game
    {
        /*==========================================================
        ||                                                        ||
        ||      --------------------------------------------      ||
        ||         { Welcome to the AATool source code! }         ||
        ||      --------------------------------------------      ||
        ||             Developed by Darwin 'CTM' Baker            ||
        ||                                                        ||
        ||                                                        ||
        ||       //To anyone building modified versions of        ||
        ||       //this program, please put your name here        ||
        ||       //to help differentiate unofficial builds        ||
        ||                                                        ||
        ||       */const string ModderName = "appleplectic";/*                ||
        ||                                                        ||
        ||       //Thanks!                                        ||
        ||                                                        ||
        ||       Note: This is a "fast n' dirty" project that     ||
        ||          has evolved far beyond its original scope     ||
        ||          and has incurred a lot of technical debt.     ||
        ||          As such, the codebase is a bit of a mess :p   ||
        ||                                                        ||
        ||                                                        ||
        ====================================================HDWGH?*/

        public static string FullTitle      { get; private set; }
        public static string ShortTitle     { get; private set; }
        public static Version Version       { get; private set; }
        public static Random RNG            { get; private set; }

        public static readonly TextInfo TextInfo = new CultureInfo("en-US", false).TextInfo;

        public static GraphicsDeviceManager GraphicsManager { get; private set; }
        public static GraphicsDevice Device => GraphicsManager?.GraphicsDevice;

        public static UIMainScreen PrimaryScreen { get; private set; }
        public static UIOverlayScreen OverlayScreen { get; private set; }
        public static Dictionary<Type, UIScreen> SecondaryScreens { get; private set; }
        public static FNotes NotesWindow { get; private set; }

        public static bool IsBeta => FullTitle.ToLower().Contains("beta");
        public static bool IsModded => !string.IsNullOrEmpty(ModderName);

        public readonly Time Time;

        private bool announceUpdate;

        public Main()
        {
            SseManager.Start();
            
            Version = Assembly.GetExecutingAssembly().GetName().Version;
            GraphicsManager = new GraphicsDeviceManager(this);
            RNG = new Random();
            
            Config.Initialize();

            this.TargetElapsedTime = Config.Main.FpsCap == 0 
                ? TimeSpan.FromSeconds(1.0 / 60) 
                : TimeSpan.FromSeconds(1.0 / Config.Main.FpsCap);
            this.InactiveSleepTime = TimeSpan.Zero;
            this.IsFixedTimeStep = true;
            this.IsMouseVisible = true;
            this.Time = new Time();
        }

        protected override void Initialize()
        {
            //load assets
            Canvas.Initialize();
            SpriteSheet.Initialize();
            Tracker.Initialize();
            FontSet.Initialize();
            Leaderboard.Initialize();
            RunnerProfile.Initialize();
            Credits.Initialize();

            //get last player's identity
            if (!string.IsNullOrEmpty(Config.Tracking.LastPlayer))
                Player.FetchIdentityAsync(Config.Tracking.LastPlayer);

            //get solo player's identity
            if (Config.Tracking.Filter == ProgressFilter.Solo)
                Player.FetchIdentityAsync(Config.Tracking.SoloFilterName);

            //check for updates
            new UpdateRequest().EnqueueOnce();

            //check build number of last aatool session
            Version.TryParse(Config.Tracking.LastSession, out Version lastSession);
            if (lastSession is null || lastSession < Version.Parse("1.3.2"))
                this.announceUpdate = true;
            Config.Tracking.LastSession.Set(Version.ToString());
            Config.Tracking.TrySave();

            this.UpdateTitle();

            //instantiate screens
            SecondaryScreens = new ();
            PrimaryScreen = new UIMainScreen(this);
            OverlayScreen = new UIOverlayScreen(this);
            this.AddScreen(OverlayScreen);
            PrimaryScreen.Form.BringToFront();

            base.Initialize();
        }

        protected override void Update(GameTime gameTime)
        {
            Input.BeginUpdate(this.IsActive);

            this.Time.Update(gameTime);

            Debug.BeginTiming("update_main");

            //check minecraft version
            ActiveInstance.Update(this.Time);
            MinecraftServer.Update(this.Time);
            Tracker.Update(this.Time);
            SpriteSheet.Update(this.Time);
            Canvas.Update(this.Time);
            Player.SetFlags();

            //update visibilty of update popup
            if (UpdateRequest.IsDone && !UpdateRequest.Suppress)
            {
                if (this.announceUpdate || UpdateRequest.UserInitiated || UpdateRequest.UpdatesAreAvailable())
                    this.ShowUpdateScreen();
            }

            //update each screen
            PrimaryScreen.UpdateRecursive(this.Time);
            foreach (UIScreen screen in SecondaryScreens.Values)
                screen.UpdateRecursive(this.Time);

            //update notes screen
            if (Config.Notes.Enabled)
            {
                if (NotesWindow is null || NotesWindow.IsDisposed)
                {
                    NotesWindow = new FNotes();
                    NotesWindow.Show();
                }
                else
                {
                    NotesWindow.UpdateCurrentSave(Tracker.WorldName);
                }
            }
            else if (NotesWindow is not null && !NotesWindow.IsDisposed)
            {
                NotesWindow.Hiding = true;
                NotesWindow.Close();
            }

            //update window title
            if (Config.Main.FpsCap.Changed)
            {
                this.TargetElapsedTime = TimeSpan.FromSeconds(1.0 / Config.Main.FpsCap);
                this.UpdateTitle();
            }
            else if (Tracker.ObjectivesChanged 
                || Tracker.InGameTimeChanged 
                || Config.Tracking.FilterChanged
                || Tracker.ProgressChanged)
            {
                this.UpdateTitle();
            }
            
            NetRequest.Update(this.Time);
            Config.ClearAllFlags();
            Tracker.ClearFlags();
            Peer.ClearFlags();
            Player.ClearFlags();
            Input.EndUpdate();

            Debug.EndTiming("update_main");

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            Debug.BeginTiming("draw_main");
            lock (SpriteSheet.Atlas)
            {
                //render each secondary screen to its respective viewport
                foreach (UIScreen screen in SecondaryScreens.Values)
                {
                    screen.Prepare();
                    screen.Render();
                    screen.Present();
                }

                //render main screen to default backbuffer
                PrimaryScreen.Prepare();
                PrimaryScreen.Render();
                PrimaryScreen.Present();
                base.Draw(gameTime);
            }
            Debug.EndTiming("draw_main");
        }

        private void AddScreen(UIScreen screen)
        {
            if (SecondaryScreens.TryGetValue(screen.GetType(), out UIScreen old))
                old.Dispose();
            SecondaryScreens[screen.GetType()] = screen;
        }

        private void ShowUpdateScreen()
        {
            this.AddScreen(new UIUpdateScreen(this, this.announceUpdate));
            UpdateRequest.Suppress = true;
            UpdateRequest.UserInitiated = false;
        }

        private void AppendTitle(string text) => FullTitle += $"   ｜   {text}";

        private void UpdateTitle()
        {
            string name = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyTitleAttribute>().Title;
            string description = Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false)
                .OfType<AssemblyDescriptionAttribute>()
                .FirstOrDefault()?.Description ?? string.Empty;

            ShortTitle = string.IsNullOrWhiteSpace(description) 
                ? $"{name} {Version}"
                : $"{name} {Version} {description}";

            FullTitle = string.IsNullOrWhiteSpace(ModderName) 
                ? ShortTitle 
                : $"{ShortTitle} - UNOFFICIALLY MODIFIED BY: {ModderName}";

            //add category, version, and progress to title
            int completed = Tracker.Category.GetCompletedCount();
            int total = Tracker.Category.GetTargetCount();
            this.AppendTitle($"{Tracker.Category.CurrentVersion} {Tracker.Category.Name} ({completed} / {total})");

            if (Tracker.InGameTime > TimeSpan.Zero)
            {
                if (Tracker.InGameTime.Days is 0)
                {
                    //add igt to title
                    this.AppendTitle($"{Tracker.GetFullIgt()} IGT");
                }
                else if (string.IsNullOrEmpty(Tracker.WorldName))
                {
                    //add world name and days/hours played to title
                    this.AppendTitle($"{Tracker.GetDaysAndHours()}");
                }
                else
                {
                    //add world name and days/hours played to title
                    this.AppendTitle($"{Tracker.WorldName}: {Tracker.GetDaysAndHours()}");
                }
            }
            

            //add igt to title
            HashSet<Uuid> players = Tracker.GetAllPlayers();
            if (players.Count > 1 && Config.Tracking.Filter == ProgressFilter.Combined)
                this.AppendTitle($"Tracking {players.Count} Players");
            else if (Player.TryGetName(Tracker.GetMainPlayer(), out string playerOne))
                this.AppendTitle(playerOne);
            else
                this.AppendTitle(Config.Tracking.LastPlayer);

            //add fps cap to title
            if (Config.Main.FpsCap < 60)
                this.AppendTitle($"{Config.Main.FpsCap.Value} FPS Cap");

            //assign title to window
            if (PrimaryScreen is not null)
                PrimaryScreen.Form.Text = "  " + FullTitle;
        }

        public static void QuitBecause(string reason, Exception exception = null)
        {
            //show user a message and quit if for some reason the program fails to load properly
            string caption = "Missing Assets";
            if (File.Exists("AAUpdate.exe"))
            {
                string message = $"One or more required assets failed to load!\n{reason}\n\nWould you like to repair your installation?";
                if (exception is not null)
                    message += $"\n\n{exception.GetType()}:{exception.StackTrace}";
                DialogResult result = MessageBox.Show(message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                if (result is DialogResult.Yes)
                    UpdateHelper.RunAAUpdate(1);
            }
            else
            {
                string message = $"One or more required assets failed to load and the update executable could not be found!\n{reason}\n\nWould you like to go to the AATool GitHub page to download and re-install manually?";
                if (exception is not null)
                    message += $"\n\n{exception.GetType()}:{exception.StackTrace}";
                DialogResult result = MessageBox.Show(message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                if (result is DialogResult.Yes)
                    _ = Process.Start(Paths.Web.LatestRelease);
            }
        }
    }
}
