﻿namespace WhMgr.Data.Subscriptions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Timers;

    using ServiceStack.OrmLite;

    using WhMgr.Configuration;
    using WhMgr.Data.Subscriptions.Models;
    using WhMgr.Diagnostics;
    using WhMgr.Net.Models;

    public class SubscriptionManager
    {
        #region Variables

        private static readonly IEventLogger _logger = EventLogger.GetLogger("MANAGER", Program.LogLevel);

        private readonly WhConfig _whConfig;
        private List<SubscriptionObject> _subscriptions;
        private readonly OrmLiteConnectionFactory _connFactory;
        private readonly Timer _reloadTimer;

        #endregion

        #region Properties

        public IReadOnlyList<SubscriptionObject> Subscriptions => _subscriptions;

        #endregion

        #region Constructor

        public SubscriptionManager(WhConfig whConfig)
        {
            _logger.Trace($"SubscriptionManager::SubscriptionManager");

            _whConfig = whConfig;

            if (_whConfig?.Database?.Main == null)
            {
                var err = "Main database is not configured in config.json file.";
                _logger.Error(err);
                throw new NullReferenceException(err);
            }

            if (_whConfig?.Database?.Scanner == null)
            {
                var err = "Scanner database is not configured in config.json file.";
                _logger.Error(err);
                throw new NullReferenceException(err);
            }

            _connFactory = new OrmLiteConnectionFactory(_whConfig.Database.Main.ToString(), MySqlDialect.Provider);

            // Reload subscriptions every 60 seconds to account for UI changes
            _reloadTimer = new Timer(_whConfig.ReloadSubscriptionChangesMinutes * 60 * 1000);
            _reloadTimer.Elapsed += OnReloadTimerElapsed;
            _reloadTimer.Start();

            ReloadSubscriptions();
        }

        private void OnReloadTimerElapsed(object sender, ElapsedEventArgs e)
        {
            // TODO: Only reload based on last_changed timestamp in metadata table
            ReloadSubscriptions();
        }

        #endregion

        #region User

        public SubscriptionObject GetUserSubscriptions(ulong guildId, ulong userId)
        {
            if (!IsDbConnectionOpen())
            {
                throw new Exception("Not connected to database.");
            }

            try
            {
                var conn = GetConnection();
                var where = conn?
                    .From<SubscriptionObject>()
                    .Where(x => x.GuildId == guildId && x.UserId == userId);
                var query = conn?.LoadSelect(where);
                var sub = query?.FirstOrDefault();
                return sub ?? new SubscriptionObject { UserId = userId, GuildId = guildId };
            }
            catch (MySql.Data.MySqlClient.MySqlException ex)
            {
                _logger.Error(ex);
                return GetUserSubscriptions(guildId, userId);
            }
        }

        public List<SubscriptionObject> GetUserSubscriptionsByPokemonId(int pokeId)
        {
            return _subscriptions?.Where(x =>
                x.Enabled && x.Pokemon != null &&
                x.Pokemon.Exists(y => y.PokemonId == pokeId)
            )?.ToList();
        }

        public List<SubscriptionObject> GetUserSubscriptionsByPvPPokemonId(int pokeId)
        {
            return _subscriptions?.Where(x =>
                x.Enabled && x.PvP != null &&
                x.PvP.Exists(y => y.PokemonId == pokeId)
            )?.ToList();
        }

        public List<SubscriptionObject> GetUserSubscriptionsByRaidBossId(int pokeId)
        {
            return _subscriptions?.Where(x =>
                x.Enabled && x.Raids != null &&
                x.Raids.Exists(y => y.PokemonId == pokeId)
            )?.ToList();
        }

        public List<SubscriptionObject> GetUserSubscriptionsByQuestReward(string reward)
        {
            return _subscriptions?.Where(x =>
                x.Enabled && x.Quests != null &&
                x.Quests.Exists(y => reward.Contains(y.RewardKeyword))
            )?.ToList();
        }

        public List<SubscriptionObject> GetUserSubscriptionsByEncounterReward(List<int> encounterRewards)
        {
            return _subscriptions?.Where(x =>
                x.Enabled && x.Invasions != null &&
                x.Invasions.Exists(y => encounterRewards.Contains(y.RewardPokemonId))
            )?.ToList();
        }

        public List<SubscriptionObject> GetUserSubscriptionsByLureType(PokestopLureType lureType)
        {
            return _subscriptions?.Where(x =>
                x.Enabled && x.Lures != null &&
                x.Lures.Exists(y => lureType == y.LureType)
            )?.ToList();
        }

        public List<SubscriptionObject> GetUserSubscriptions()
        {
            try
            {
                if (!IsDbConnectionOpen())
                {
                    throw new Exception("Not connected to database.");
                }

                var conn = GetConnection();
                var where = conn?
                    .From<SubscriptionObject>()?
                    .Where(x => x.Enabled);
                var results = conn?
                    .LoadSelect(where)?
                    .ToList();
                return results;
            }
            catch (OutOfMemoryException mex)
            {
                _logger.Debug($"-------------------OUT OF MEMORY EXCEPTION!");
                _logger.Error(mex);
                Environment.FailFast($"Out of memory: {mex}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }

            return null;
        }

        public void ReloadSubscriptions()
        {
            var subs = GetUserSubscriptions();
            if (subs == null)
                return;

            _subscriptions = subs;
        }

        #endregion

        #region Remove

        public static bool RemoveAllUserSubscriptions(ulong guildId, ulong userId)
        {
            _logger.Trace($"SubscriptionManager::RemoveAllUserSubscription [GuildId={guildId}, UserId={userId}]");

            try
            {
                using (var conn = DataAccessLayer.CreateFactory().Open())
                {
                    conn.Delete<PokemonSubscription>(x => x.GuildId == guildId && x.UserId == userId);
                    conn.Delete<PvPSubscription>(x => x.GuildId == guildId && x.UserId == userId);
                    conn.Delete<RaidSubscription>(x => x.GuildId == guildId && x.UserId == userId);
                    conn.Delete<QuestSubscription>(x => x.GuildId == guildId && x.UserId == userId);
                    conn.Delete<GymSubscription>(x => x.GuildId == guildId && x.UserId == userId);
                    conn.Delete<InvasionSubscription>(x => x.GuildId == guildId && x.UserId == userId);
                    conn.Delete<LureSubscription>(x => x.GuildId == guildId && x.UserId == userId);
                    conn.Delete<SubscriptionObject>(x => x.GuildId == guildId && x.UserId == userId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }

            return false;
        }

        #endregion

        #region Private Methods

        private System.Data.IDbConnection GetConnection()
        {
            return _connFactory.Open();
        }

        private bool IsDbConnectionOpen()
        {
            return _connFactory != null;
        }

        #endregion
    }
}
