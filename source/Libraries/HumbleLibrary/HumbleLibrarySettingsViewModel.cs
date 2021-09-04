﻿using HumbleLibrary.Services;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HumbleLibrary
{
    public class HumbleLibrarySettings
    {
        public bool ConnectAccount { get; set; } = false;
        public bool IgnoreThirdPartyStoreGames { get; set; } = true;
        public bool ImportThirdPartyDrmFree { get; set; } = false;
        public bool ImportTroveGames { get; set; } = false;
        public bool ImportAllTrove { get; set; } = false;
        public bool TagTroveGames { get; set; } = false;
    }

    public class HumbleLibrarySettingsViewModel : PluginSettingsViewModel<HumbleLibrarySettings, HumbleLibrary>
    {
        public bool IsUserLoggedIn
        {
            get
            {
                using (var view = PlayniteApi.WebViews.CreateOffscreenView(
                    new WebViewSettings
                    {
                        JavaScriptEnabled = false
                    }))
                {
                    var api = new HumbleAccountClient(view);
                    return api.GetIsUserLoggedIn();
                }
            }
        }

        public RelayCommand<object> LoginCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                Login();
            });
        }

        public HumbleLibrarySettingsViewModel(HumbleLibrary plugin, IPlayniteAPI api) : base(plugin, api)
        {
            var savedSettings = LoadSavedSettings();
            if (savedSettings != null)
            {
                Settings = savedSettings;
            }
            else
            {
                Settings = new HumbleLibrarySettings();
            }
        }

        private void Login()
        {
            try
            {
                using (var view = PlayniteApi.WebViews.CreateView(490, 670))
                {
                    var api = new HumbleAccountClient(view);
                    api.Login();
                }

                OnPropertyChanged(nameof(IsUserLoggedIn));
            }
            catch (Exception e) when (!Debugger.IsAttached)
            {
                Logger.Error(e, "Failed to authenticate user.");
            }
        }
    }
}