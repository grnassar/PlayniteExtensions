﻿using HumbleLibrary.Models;
using HumbleLibrary.Services;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace HumbleLibrary
{
    [LoadPlugin]
    public class HumbleLibrary : LibraryPluginBase<HumbleLibrarySettingsViewModel>
    {
        public HumbleLibrary(IPlayniteAPI api) : base(
            "Humble",
            Guid.Parse("96e8c4bc-ec5c-4c8b-87e7-18ee5a690626"),
            new LibraryPluginProperties { CanShutdownClient = false, HasCustomizedGameImport = true, HasSettings = true },
            null,
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"icon.png"),
            (_) => new HumbleLibrarySettingsView(),
            api)
        {
            SettingsViewModel = new HumbleLibrarySettingsViewModel(this, PlayniteApi);
        }

        internal string GetGameId(Order.SubProduct product)
        {
            return $"{product.machine_name}_{product.human_name}";
        }

        internal string GetGameId(TroveGame troveGame)
        {
            return $"{troveGame.machine_name}_{troveGame.human_name}_TROVE";
        }

        internal List<GameMetadata> GetTroveGames()
        {
            var chunkDataUrlBase = @"https://www.humblebundle.com/api/v1/trove/chunk?property=popularity&direction=desc&index=";
            var games = new List<GameMetadata>();

            using (var webClient = new WebClient { Encoding = Encoding.UTF8 })
            {
                var initialPageSrc = webClient.DownloadString(@"https://www.humblebundle.com/subscription/trove");
                var chunkMatch = Regex.Match(initialPageSrc, @"chunks"":\s*(\d+)");
                if (chunkMatch.Success)
                {
                    var chunks = int.Parse(chunkMatch.Groups[1].Value);
                    for (int i = 0; i < chunks; i++)
                    {
                        var chunkDataStr = webClient.DownloadString(chunkDataUrlBase + i);
                        foreach (var troveGame in Serialization.FromJson<List<TroveGame>>(chunkDataStr))
                        {
                            var game = new GameMetadata
                            {
                                Name = troveGame.human_name.RemoveTrademarks(),
                                GameId = GetGameId(troveGame),
                                Description = troveGame.description_text,
                                Publishers = troveGame.publishers?.Select(a => new MetadataNameProperty(a.publisher_name)).ToList(),
                                Developers = troveGame.developers?.Select(a => new MetadataNameProperty(a.developer_name)).ToList(),
                                Source = new MetadataNameProperty("Humble"),
                                //Platforms = new List<MetadataProperty> { new MetadataSpecProperty("pc_windows") }
                                //Platforms = new List<MetadataProperty> { }
                            };
                            // does not work, because Platforms is IEnumerable - that's for views and shouldn't be used for internal members; should be ICollection - TODO
                            //if (troveGame.downloads.ContainsKey("windows")) { game.Platforms.Add(new MetadataSpecProperty("pc_windows")); }
                            //if (troveGame.downloads.ContainsKey("mac")) { game.Platforms.Add(new MetadataSpecProperty("mac")); }
                            //if (troveGame.downloads.ContainsKey("linux")) { game.Platforms.Add(new MetadataSpecProperty("linux")); }
                            var platforms = new List<MetadataProperty> { };
                            if (troveGame.downloads.ContainsKey("windows")) { platforms.Add(new MetadataSpecProperty("pc_windows")); }
                            if (troveGame.downloads.ContainsKey("mac")) { platforms.Add(new MetadataSpecProperty("mac")); }
                            if (troveGame.downloads.ContainsKey("linux")) { platforms.Add(new MetadataSpecProperty("linux")); }
                            game.Platforms = platforms;

                            if (SettingsViewModel.Settings.TagTroveGames)
                            {
                                game.Tags = new List<MetadataProperty> { new MetadataNameProperty("Trove") };
                            }
                            games.Add(game);
                        }
                    }
                }
                else
                {
                    Logger.Warn("Failed to get number of trove chunks.");
                }
            }

            return games.OrderBy(a => a.Name).ToList();
        }

        public override IEnumerable<Game> ImportGames(LibraryImportGamesArgs args)
        {
            var importedGames = new List<Game>();
            Exception importError = null;
            if (!SettingsViewModel.Settings.ConnectAccount)
            {
                return importedGames;
            }

            try
            {
                var orders = new List<Order>();
                using (var view = PlayniteApi.WebViews.CreateOffscreenView(
                    new WebViewSettings
                    {
                        JavaScriptEnabled = false
                    }))
                {
                    var api = new HumbleAccountClient(view);
                    var keys = api.GetLibraryKeys();
                    orders = api.GetOrders(keys);
                }

                var selectedProducts = new List<Order.SubProduct>();
                var allTpks = orders.SelectMany(a => a.tpkd_dict?.all_tpks).ToList();

                foreach (var order in orders)
                {
                    if (order.subproducts.HasItems())
                    {
                        foreach (var product in order.subproducts)
                        {
                            var alreadyAdded = selectedProducts.FirstOrDefault(a => a.human_name == product.human_name);
                            if (alreadyAdded != null)
                            {
                                continue;
                            }

                            if (product.downloads?.Any(a => a.platform == "windows") == true)
                            {
                                if (SettingsViewModel.Settings.IgnoreThirdPartyStoreGames && order.tpkd_dict?.all_tpks.HasItems() == true)
                                {
                                    var exst = allTpks.FirstOrDefault(a =>
                                    !a.human_name.IsNullOrEmpty() &&
                                    (a.human_name.Equals(product.human_name, StringComparison.OrdinalIgnoreCase) ||
                                    Regex.IsMatch(a.human_name, Regex.Escape(product.human_name) + @".+\sKey$", RegexOptions.IgnoreCase) ||
                                    Regex.IsMatch(a.human_name, Regex.Escape(product.human_name) + @".*\s\(?Steam\)?$", RegexOptions.IgnoreCase) ||
                                    Regex.IsMatch(a.human_name + @"\s*+$", Regex.Escape(product.human_name), RegexOptions.IgnoreCase)));

                                    if (exst != null && !SettingsViewModel.Settings.ImportThirdPartyDrmFree)
                                    {
                                        continue;
                                    }
                                }

                                selectedProducts.Add(product);
                            }
                        }
                    }
                }

                foreach (var product in selectedProducts)
                {
                    var gameId = GetGameId(product);
                    if (PlayniteApi.ApplicationSettings.GetGameExcludedFromImport(gameId, Id))
                    {
                        continue;
                    }

                    var alreadyImported = PlayniteApi.Database.Games.FirstOrDefault(a => a.GameId == gameId && a.PluginId == Id);
                    if (alreadyImported == null)
                    {
                        importedGames.Add(PlayniteApi.Database.ImportGame(new GameMetadata()
                        {
                            Name = product.human_name.RemoveTrademarks(),
                            GameId = gameId,
                            Icon = product.icon.IsNullOrEmpty() ? null : new MetadataFile(product.icon),
                            Source = new MetadataNameProperty("Humble")
                        }, this));
                    }
                }

                if (SettingsViewModel.Settings.ImportTroveGames)
                {
                    foreach (var troveGame in GetTroveGames())
                    {
                        // Don't import Trove game if it's part of main library
                        if (!SettingsViewModel.Settings.ImportAllTrove && selectedProducts.Any(a => a.human_name.RemoveTrademarks().Equals(troveGame.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        var alreadyImported = PlayniteApi.Database.Games.FirstOrDefault(a => a.GameId == troveGame.GameId && a.PluginId == Id);
                        if (alreadyImported == null && !PlayniteApi.ApplicationSettings.GetGameExcludedFromImport(troveGame.GameId, Id))
                        {
                            importedGames.Add(PlayniteApi.Database.ImportGame(troveGame, this));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to import Humble games.");
                importError = e;
            }

            if (importError != null)
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    ImportErrorMessageId,
                    string.Format(PlayniteApi.Resources.GetString("LOCLibraryImportError"), Name) +
                    System.Environment.NewLine + importError.Message,
                    NotificationType.Error,
                    () => OpenSettingsView()));
            }
            else
            {
                PlayniteApi.Notifications.Remove(ImportErrorMessageId);
            }

            return importedGames;
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return SettingsViewModel;
        }
    }
}