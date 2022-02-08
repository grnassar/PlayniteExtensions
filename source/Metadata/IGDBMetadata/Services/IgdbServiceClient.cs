﻿using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.Services;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IgdbModels = PlayniteServices.Models.IGDB;
using SdkModels = Playnite.SDK.Models;

namespace IGDBMetadata.Services
{
    public class IgdbServiceClient : BaseServicesClient
    {
        private readonly ILogger logger = LogManager.GetLogger();

        public IgdbServiceClient(Version playniteVersion) : base(ConfigurationManager.AppSettings["ServicesUrl"], playniteVersion)
        {
        }

        public IgdbServiceClient(Version playniteVersion, string endpoint) : base(endpoint, playniteVersion)
        {
        }

        public IgdbModels.ExpandedGame GetMetadata(SdkModels.Game game)
        {
            // Only serialize minimum amount of data needed
            var gameData = Serialization.ToJson(new Dictionary<string, object>
            {
                { nameof(SdkModels.Game.Name), game.Name },
                { nameof(SdkModels.Game.ReleaseDate), game.ReleaseDate },
                { nameof(SdkModels.Game.PluginId), game.PluginId },
                { nameof(SdkModels.Game.GameId), game.GameId }
            });
            return ExecutePostRequest<IgdbModels.ExpandedGame>("/igdb/metadata_v3", gameData);
        }

        public List<IgdbModels.ExpandedGameLegacy> GetIGDBGames(string searchName)
        {
            var encoded = Uri.EscapeDataString(searchName);
            return ExecuteGetRequest<List<IgdbModels.ExpandedGameLegacy>>($"/igdb/games/{encoded}");
        }

        public IgdbModels.ExpandedGame GetIGDBGameExpanded(UInt64 id)
        {
            return ExecuteGetRequest<IgdbModels.ExpandedGame>($"/igdb/game_parsed_v2/{id}");
        }
    }
}
