using Aquamarine.Source.Logging;
using Aquamarine.Source.Management;
using Aquamarine.Source.Management.Data;
using Aquamarine.Source.Core;
using Aquamarine.Source.Core.UserSpace;
using Aquamarine.Source.Helpers;
using Aquamarine.Source.Core.WorldTemplates;
using Engine = Aquamarine.Source.Core.Engine;
using Godot;
using Godot.NativeInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Logger = Aquamarine.Source.Logging.Logger;
namespace Aquamarine.Source.Management.Client.UI;
public partial class WorldsTab : Control
{
    /* this api is stupid and we need to do a "big rewrite".. Mabye
       we may need a redis for sessions (or somting similer) may try realm or dragonfly
    */
    private readonly Dictionary<string, Data.WorldEntry> _sessions = new();
    private readonly Dictionary<string, LocalWorldInfo> _localWorlds = new();
    private readonly Dictionary<string, WorldEntry> _worldCards = new();
    public IReadOnlyDictionary<string, Data.WorldEntry> Sessions { get; private set; }
    public IReadOnlyDictionary<string, LocalWorldInfo> LocalWorlds => _localWorlds;
    private Node _holdernode;
    private Node _worldnameText;
    private Node _activeSessionsList;
    private Control _contextContainer;
    private TextureRect _contextPreview;
    private Texture2D _defaultPreview;
    private readonly System.Net.Http.HttpClient client = new();
    private readonly PeriodicTimer timer = new(new TimeSpan(0, 0, 20));
    private PackedScene _worldEntry;
    private PackedScene _sessionEntry;
    private Button _plusButton;
    private PackedScene _worldCreationUI;
    private WorldCreationUI _currentCreationUI;
    public delegate void SessionListUpdate();
    public event SessionListUpdate SessionListUpdated;
    public override void _Ready()
    {
        base._Ready();
        Sessions = _sessions.AsReadOnly();
        _worldEntry = ResourceLoader.Load<PackedScene>("res://Scenes/UI/Manu/WorldTab/WorldEntry.tscn");
        _sessionEntry = ResourceLoader.Load<PackedScene>("res://Scenes/UI/Manu/WorldTab/session_instance.tscn");
        _worldCreationUI = ResourceLoader.Load<PackedScene>("res://Scenes/UI/Manu/WorldTab/WorldCreationUI.tscn");
        _holdernode = GetNode("%WorldHolder");
        _worldnameText = GetNode("%WorldName");
        _activeSessionsList = GetNode("%SeessionList");
        _contextContainer = GetNode<Control>("%ContextContainer");
        _contextPreview = GetNodeOrNull<TextureRect>("%WorldPreview");
        _defaultPreview = TemplateManager.GetTemplate("Grid")?.GetPreviewTexture() ?? GenerateFallbackPreview();
        _plusButton = GetNodeOrNull<Button>("%ButtonPlus");

        // Connect plus button
        if (_plusButton != null)
        {
            _plusButton.Pressed += OnPlusButtonPressed;
        }

        if (WorldsManager.Instance != null)
        {
            WorldsManager.Instance.WorldCreated += OnWorldChanged;
            WorldsManager.Instance.WorldSwitched += OnWorldChanged;
        }

        CallDeferred(nameof(RefreshLocalWorlds));

        Task.Run(async () => { while (true) { await GetSessions(); await timer.WaitForNextTickAsync(); } });
    }

    private void ApplyRemoteSessionData(Dictionary<string, List<SessionInfo>> infos, Dictionary<string, string> worldNames)
    {
        var previousIds = new HashSet<string>(_sessions.Keys);
        _sessions.Clear();

        foreach (var kvp in infos)
        {
            if (!worldNames.TryGetValue(kvp.Key, out var worldName))
                continue;

            var dataEntry = new Data.WorldEntry(kvp.Key, worldName, kvp.Value.ToArray());
            _sessions[kvp.Key] = dataEntry;
            EnsureRemoteWorldCard(kvp.Key, worldName);
        }

        foreach (var removed in previousIds.Except(_sessions.Keys).ToList())
        {
            if (_worldCards.TryGetValue(removed, out var entry) && !entry.IsLocal)
            {
                entry.QueueFree();
                _worldCards.Remove(removed);
            }
        }

        RefreshLocalWorlds();
        SessionListUpdated?.Invoke();
    }

    private void EnsureRemoteWorldCard(string worldId, string worldName)
    {
        if (_worldEntry == null || _holdernode == null)
            return;

        if (!_worldCards.TryGetValue(worldId, out var entry))
        {
            entry = _worldEntry.Instantiate<WorldEntry>();
            _holdernode.AddChild(entry);
            _worldCards[worldId] = entry;
        }

        entry.AssignRemoteWorld(this, worldId, worldName);
    }

    private void EnsureLocalWorldCard(LocalWorldInfo info)
    {
        if (_worldEntry == null || _holdernode == null)
            return;

        if (!_worldCards.TryGetValue(info.WorldId, out var entry))
        {
            entry = _worldEntry.Instantiate<WorldEntry>();
            _holdernode.AddChild(entry);
            _worldCards[info.WorldId] = entry;
        }

        entry.AssignLocalWorld(this, info);
    }

    private Texture2D GenerateFallbackPreview()
    {
        var size = new Vector2I(512, 288);
        var image = Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgba8);
        var top = new Color(0.15f, 0.18f, 0.24f);
        var bottom = new Color(0.04f, 0.05f, 0.08f);
        var denominator = Mathf.Max(1, size.Y - 1);
        for (int y = 0; y < size.Y; y++)
        {
            var rowColor = top.Lerp(bottom, (float)y / denominator);
            for (int x = 0; x < size.X; x++)
            {
                image.SetPixel(x, y, rowColor);
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    private void RefreshLocalWorlds()
    {
        if (_holdernode == null)
            return;

        var updated = new Dictionary<string, LocalWorldInfo>();

        // Don't create a separate "local_home" entry - just list actual worlds from WorldsManager
        var worldsManager = WorldsManager.Instance;
        if (worldsManager != null)
        {
            foreach (var kvp in worldsManager.Worlds)
            {
                var instance = kvp.Value;
                if (string.IsNullOrEmpty(instance.WorldName))
                    continue;

                var preview = instance.PreviewTexture;
                if (preview == null && !string.IsNullOrEmpty(instance.TemplateName))
                {
                    preview = TemplateManager.GetTemplate(instance.TemplateName)?.GetPreviewTexture();
                }

                // Special display name for LocalHome
                var displayName = instance.WorldName;
                if (instance.WorldName == "LocalHome")
                {
                    displayName = $"{System.Environment.UserName}'s Local Home";
                }

                var description = string.IsNullOrEmpty(instance.TemplateName)
                    ? "Locally hosted world"
                    : $"Locally hosted world based on {instance.TemplateName}";

                var info = new LocalWorldInfo(
                    instance.WorldId,
                    displayName,
                    preview ?? _defaultPreview,
                    () => WorldsManager.Instance?.SwitchToWorld(instance.WorldId),
                    description);

                updated[info.WorldId] = info;
                EnsureLocalWorldCard(info);
            }
        }

        foreach (var removed in _localWorlds.Keys.Except(updated.Keys).ToList())
        {
            if (_worldCards.TryGetValue(removed, out var entry) && entry.IsLocal)
            {
                entry.QueueFree();
                _worldCards.Remove(removed);
            }
        }

        _localWorlds.Clear();
        foreach (var kvp in updated)
        {
            _localWorlds[kvp.Key] = kvp.Value;
        }
    }

    // REMOVED: BuildLocalHomeInfo() - was creating duplicate LocalHome entry
    // LocalHome now shown directly from WorldsManager.Worlds

    private void OnWorldChanged(string worldId)
    {
        RefreshLocalWorlds();
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        if (_contextContainer is Control ctrl)
        {
            ctrl.Hide();
        }
        if (SessionListUpdated is not null)
            SessionListUpdated();
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        if (WorldsManager.Instance != null)
        {
            WorldsManager.Instance.WorldCreated -= OnWorldChanged;
            WorldsManager.Instance.WorldSwitched -= OnWorldChanged;
        }
    }
    private async Task GetSessions()
    {
        var cts = new CancellationTokenSource();
        try
        {
            var requestTask = client.GetStringAsync(SessionInfo.SessionList, cts.Token);
            var delayTask = Task.Delay(8000);
            var completed = await Task.WhenAny(requestTask, delayTask);

            if (completed is not Task<string> jsonTask)
            {
                cts.Cancel();
                Logger.Error("Failed to get session list");
                return;
            }

            var json = jsonTask.Result;
            var list = System.Text.Json.JsonSerializer.Deserialize<List<SessionInfo>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<SessionInfo>();

            var infos = new Dictionary<string, List<SessionInfo>>();
            var worldNames = new Dictionary<string, string>();

            foreach (var sessionInfo in list)
            {
                if (string.IsNullOrWhiteSpace(sessionInfo.WorldIdentifier))
                    continue;

                if (!infos.TryGetValue(sessionInfo.WorldIdentifier, out var sessionsForWorld))
                {
                    sessionsForWorld = new List<SessionInfo>();
                    infos.Add(sessionInfo.WorldIdentifier, sessionsForWorld);
                    worldNames[sessionInfo.WorldIdentifier] = sessionInfo.Name;
                }

                sessionsForWorld.Add(sessionInfo);
            }

            this.RunOnNodeAsync(() => ApplyRemoteSessionData(infos, worldNames));
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to fetch session list: {ex.Message}");
        }
        finally
        {
            cts.Dispose();
        }
    }
    /// <summary>
    /// Loads the world info into the details modal
    /// 
    /// Called by the details button on the world entry
    /// </summary>
    /// <param name="worldid"></param>
    internal void LoadWorldInfo(string worldid)
    {
        if (_contextContainer is Control con)
        {
            con.Show();
        }

        if (_localWorlds.TryGetValue(worldid, out var localInfo))
        {
            ShowLocalWorldDetails(localInfo);
            return;
        }

        if (_sessions.TryGetValue(worldid, out var worldEntry))
        {
            ShowRemoteWorldDetails(worldEntry);
        }
    }

    private void ShowRemoteWorldDetails(Data.WorldEntry worldEntry)
    {
        if (_worldnameText is RichTextLabel text)
        {
            text.Text = worldEntry.WorldName;
        }

        if (_contextPreview != null)
        {
            _contextPreview.Texture = _defaultPreview;
        }

        if (_activeSessionsList != null)
        {
            foreach (Node child in _activeSessionsList.GetChildren())
            {
                child.QueueFree();
            }

            foreach (var session in worldEntry.Sessions)
            {
                if (_sessionEntry == null)
                    break;

                var inst = _sessionEntry.Instantiate<SessionInstance>();
                _activeSessionsList.AddChild(inst);
                inst.UpdateData(session, this);
            }
        }
    }

    private void ShowLocalWorldDetails(LocalWorldInfo info)
    {
        if (_worldnameText is RichTextLabel text)
        {
            text.Text = info.DisplayName;
        }

        if (_contextPreview != null)
        {
            _contextPreview.Texture = info.Preview ?? _defaultPreview;
        }

        if (_activeSessionsList != null)
        {
            foreach (Node child in _activeSessionsList.GetChildren())
            {
                child.QueueFree();
            }

            if (_sessionEntry != null)
            {
                var inst = _sessionEntry.Instantiate<SessionInstance>();
                _activeSessionsList.AddChild(inst);
                var subtitle = string.IsNullOrWhiteSpace(info.Description) ? "Hosted locally" : info.Description;
                inst.UpdateLocal(info.DisplayName, subtitle, info.JoinAction);
            }
        }
    }
    // TODO Fix later
    /// <summary>
    /// Joins the session with the given id
    /// </summary>
    /// <param name="id"></param>
    internal async void joinSession(string id)
    {
        bool connected = false;
        MultiplayerApi.PeerConnectedEventHandler updateconnected = (long id) => connected = true;
        Multiplayer.PeerConnected += updateconnected;
        var engine = Engine.Instance;
        engine.JoinNatServer(id);
        await Task.Delay(5000);
        if (!connected)
            engine.JoinNatServerRelay(id);
    }

    private void OnPlusButtonPressed()
    {
        Logger.Log("Plus button pressed - showing world creation UI");
        ShowWorldCreationUI();
    }

    private void ShowWorldCreationUI()
    {
        if (_worldCreationUI == null)
        {
            Logger.Error("World creation UI scene not loaded");
            return;
        }

        // Clear all children from context container to avoid overlap
        if (_contextContainer != null)
        {
            foreach (Node child in _contextContainer.GetChildren())
            {
                child.QueueFree();
            }
        }

        // Instantiate world creation UI
        _currentCreationUI = _worldCreationUI.Instantiate<WorldCreationUI>();
        _contextContainer.AddChild(_currentCreationUI);
        _contextContainer.Show();

        // Connect signals
        _currentCreationUI.WorldCreated += OnWorldCreated;
        _currentCreationUI.Cancelled += OnWorldCreationCancelled;

        Logger.Log("World creation UI shown");
    }

    private void OnWorldCreated(string worldName, string templateName)
    {
        Logger.Log($"World created: {worldName} with template {templateName}");

        var engine = Engine.Instance;
        if (engine?.WorldManager == null)
        {
            Logger.Error("Engine or WorldManager not available - cannot create world");
            CloseWorldCreationUI();
            return;
        }

        // Allocate a port for hosting the new session
        var port = SimpleIpHelpers.GetAvailablePortUdp(10) ?? 7000;
        var hostName = System.Environment.UserName;

        var world = engine.WorldManager.StartSession(worldName, templateName, (ushort)port, hostName);
        if (world == null)
        {
            Logger.Error($"Failed to start session for world '{worldName}'");
            return;
        }

        Logger.Log($"World '{worldName}' hosted on port {port}");

        // Close the creation UI
        CloseWorldCreationUI();
    }

    private void OnWorldCreationCancelled()
    {
        Logger.Log("World creation cancelled");
        CloseWorldCreationUI();
    }

    private void CloseWorldCreationUI()
    {
        if (_currentCreationUI != null)
        {
            _currentCreationUI.WorldCreated -= OnWorldCreated;
            _currentCreationUI.Cancelled -= OnWorldCreationCancelled;
            _currentCreationUI.QueueFree();
            _currentCreationUI = null;
        }

        if (_contextContainer is Control con)
            con.Hide();
    }
}
