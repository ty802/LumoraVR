using System;
using Godot;
using Aquamarine.Source.Logging;
using System.Diagnostics;
using DiscordRPC;
using Logger = Aquamarine.Source.Logging.Logger;
using EngineCore = Aquamarine.Source.Core.Engine;
using Aquamarine.Source.Core;

namespace Aquamarine.Source.Management
{
    public partial class DiscordManager : Node
    {
        private const string ClientId = "1330400493054464092";
        private const string DefaultLargeImageKey = "lumoravralpha";
        private const string DefaultSmallImageKey = "lumoravrbeta_tester";
        private const double PresenceUpdateIntervalSeconds = 5.0;
        private const int DefaultDiscordCapacity = 16;

        public static DiscordManager Instance;

        private DiscordRpcClient _discord;
        private bool _isInitialized;
        private readonly Stopwatch _uptime = new();
        private double _updateTimer;
        private PresenceSnapshot? _fallbackSnapshot;
        private PresenceSnapshot? _lastSnapshot;

        public override void _Ready()
        {
            Instance = this;
            InitializeDiscord();
        }

        public void InitializeDiscord()
        {
            try
            {
                _discord = new DiscordRpcClient(ClientId);
                _discord.Initialize();
                _isInitialized = true;
                _uptime.Start();
                Logger.Log("Discord manager initialized successfully");
                RefreshPresence(force: true);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize Discord: {ex.Message}");
                _isInitialized = false;
            }
        }

        public override void _Process(double delta)
        {
            if (!_isInitialized) return;

            try
            {
                _discord.Invoke();
                _updateTimer += delta;
                if (_updateTimer >= PresenceUpdateIntervalSeconds)
                {
                    _updateTimer = 0;
                    RefreshPresence();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Discord callback error: {ex.Message}");
                _isInitialized = false;
            }
        }

        /// <summary>
        /// Retains compatibility with legacy manual presence updates (e.g., from Engine).
        /// This snapshot is used as a fallback when richer world data is unavailable.
        /// </summary>
        public void UpdatePresence(string details, string state, string largeImage, string largeText)
        {
            if (!_isInitialized) return;

            var snapshot = new PresenceSnapshot(
                details ?? "LumoraVR",
                state ?? "In Game",
                string.IsNullOrWhiteSpace(largeImage) ? DefaultLargeImageKey : largeImage,
                string.IsNullOrWhiteSpace(largeText) ? "LumoraVR" : largeText,
                DefaultSmallImageKey,
                state ?? "In Game",
                null,
                0,
                0,
                null
            );

            _fallbackSnapshot = snapshot;
            ApplyPresence(snapshot, force: true);
        }

        private void RefreshPresence(bool force = false)
        {
            if (!_isInitialized) return;

            try
            {
                var snapshot = BuildPresenceSnapshot();
                ApplyPresence(snapshot, force);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error updating Discord presence: {ex.Message}");
            }
        }

        private void ApplyPresence(in PresenceSnapshot snapshot, bool force)
        {
            if (!force && _lastSnapshot.HasValue && snapshot.Equals(_lastSnapshot.Value))
            {
                return;
            }

            var presence = CreateRichPresence(snapshot);
            _discord.SetPresence(presence);
            _lastSnapshot = snapshot;
        }

        private RichPresence CreateRichPresence(in PresenceSnapshot snapshot)
        {
            var presence = new RichPresence
            {
                Details = snapshot.Details,
                State = snapshot.State,
                Assets = new DiscordRPC.Assets
                {
                    LargeImageKey = snapshot.LargeImageKey,
                    LargeImageText = snapshot.LargeImageText,
                    SmallImageKey = snapshot.SmallImageKey,
                    SmallImageText = snapshot.SmallImageText
                },
                Timestamps = new Timestamps
                {
                    StartUnixMilliseconds = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)_uptime.Elapsed.TotalMilliseconds)
                }
            };

            if (!string.IsNullOrWhiteSpace(snapshot.PartyId) || snapshot.PartySize > 0 || snapshot.PartyMax > 0)
            {
                presence.Party = new DiscordRPC.Party
                {
                    ID = snapshot.PartyId,
                    Size = snapshot.PartySize,
                    Max = snapshot.PartyMax
                };
            }

            if (!string.IsNullOrWhiteSpace(snapshot.JoinSecret))
            {
                presence.Secrets = new Secrets
                {
                    JoinSecret = snapshot.JoinSecret
                };
            }

            return presence;
        }

        private PresenceSnapshot BuildPresenceSnapshot()
        {
            var engine = TryGetEngine();
            if (engine?.WorldManager == null)
            {
                return _fallbackSnapshot ?? CreateDefaultSnapshot("Starting LumoraVR", "Loading...");
            }

            var worldManager = engine.WorldManager;
            var totalWorlds = worldManager.Worlds?.Count ?? 0;
            var worldCountText = FormatWorldCount(totalWorlds);
            var activeInstance = worldManager.ActiveWorldInstance;
            var activeWorld = activeInstance?.World;

            if (activeWorld == null)
            {
                return CreateDefaultSnapshot($"{worldCountText} ready", "In menus", "World Browser", "Browsing worlds");
            }

            var worldName = GetWorldDisplayName(activeInstance, activeWorld);
            var playerCount = GetPlayerCount(activeWorld);
            var capacity = DetermineCapacity(activeWorld, playerCount);
            var privacy = activeInstance.Privacy;

            var details = $"{worldName} ({worldCountText})";
            string state;
            string smallText;
            string partyId = null;
            int partySize = 0;
            int partyMax = 0;

            switch (privacy)
            {
                case WorldInstance.WorldPrivacyLevel.Hidden:
                    state = "In hidden world";
                    smallText = "Hidden world";
                    break;
                case WorldInstance.WorldPrivacyLevel.Private:
                    state = "In private world";
                    smallText = $"Private session • {Math.Max(playerCount, 1)} online";
                    break;
                default:
                    state = "In public world";
                    smallText = $"Public instance • {playerCount}/{capacity}";
                    partySize = playerCount;
                    partyMax = capacity;
                    partyId = activeWorld.SessionID?.Value;
                    break;
            }

            return new PresenceSnapshot(
                details,
                state,
                DefaultLargeImageKey,
                worldName,
                DefaultSmallImageKey,
                smallText,
                partyId,
                partySize,
                partyMax,
                null
            );
        }

        private static string GetWorldDisplayName(WorldInstance instance, World world)
        {
            if (!string.IsNullOrWhiteSpace(instance?.WorldName))
            {
                return instance.WorldName;
            }

            if (!string.IsNullOrWhiteSpace(world?.WorldName?.Value))
            {
                return world.WorldName.Value;
            }

            return "Untitled World";
        }

        private static string FormatWorldCount(int count)
        {
            var suffix = count == 1 ? "world" : "worlds";
            return $"{count} {suffix}";
        }

        private static int GetPlayerCount(World world)
        {
            if (world == null)
            {
                return 0;
            }

            try
            {
                return world.GetAllUsers().Count;
            }
            catch
            {
                return 0;
            }
        }

        private static int DetermineCapacity(World world, int currentCount)
        {
            var maxFromAllocator = world?.RefIDAllocator?.GetMaxUserCount() ?? DefaultDiscordCapacity;
            var clamped = Math.Min(maxFromAllocator, DefaultDiscordCapacity);
            return Math.Max(clamped, Math.Max(currentCount, 1));
        }

        private PresenceSnapshot CreateDefaultSnapshot(string details, string state, string largeText = "LumoraVR", string smallText = null)
        {
            return new PresenceSnapshot(
                details,
                state,
                DefaultLargeImageKey,
                largeText,
                DefaultSmallImageKey,
                smallText ?? state,
                null,
                0,
                0,
                null
            );
        }

        private static EngineCore TryGetEngine()
        {
            try
            {
                return EngineCore.Instance;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public override void _ExitTree()
        {
            if (_isInitialized)
            {
                _discord.Dispose();
            }
        }

        private readonly record struct PresenceSnapshot(
            string Details,
            string State,
            string LargeImageKey,
            string LargeImageText,
            string SmallImageKey,
            string SmallImageText,
            string PartyId,
            int PartySize,
            int PartyMax,
            string JoinSecret
        );
    }
}
