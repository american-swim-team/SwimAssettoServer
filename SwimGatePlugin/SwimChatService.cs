using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using AssettoServer.Commands;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Shared.Network.Packets.Shared;
using Microsoft.Extensions.Hosting;
using Serilog;
using SwimGatePlugin.Packets;

namespace SwimGatePlugin;

public class SwimChatService : BackgroundService
{
    private readonly SwimGateConfiguration _config;
    private readonly SwimApiClient _apiClient;
    private readonly EntryCarManager _entryCarManager;
    private readonly ChatService _chatService;
    private readonly ProfanityFilter? _profanityFilter;
    private readonly List<ChatRoleConfig> _sortedRoles;
    private readonly ConcurrentDictionary<byte, ChatRoleConfig?> _clientRoles = new();

    public SwimChatService(
        SwimGateConfiguration config,
        SwimApiClient apiClient,
        EntryCarManager entryCarManager,
        ChatService chatService,
        CSPServerScriptProvider scriptProvider)
    {
        _config = config;
        _apiClient = apiClient;
        _entryCarManager = entryCarManager;
        _chatService = chatService;

        _sortedRoles = config.ChatRoles.OrderBy(r => r.Priority).ToList();

        if (config.ProfanityFilter.Enabled)
            _profanityFilter = new ProfanityFilter(config.ProfanityFilter);

        if (_sortedRoles.Count > 0)
        {
            scriptProvider.AddScript(
                Assembly.GetExecutingAssembly().GetManifestResourceStream("SwimGatePlugin.lua.chatroles.lua")!,
                "chatroles.lua");
        }

        _entryCarManager.ClientConnected += OnClientConnected;
        _chatService.MessageReceived += OnChatMessageReceived;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_sortedRoles.Count == 0)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            BroadcastAllColors();
        }
    }

    private void BroadcastAllColors()
    {
        foreach (var car in _entryCarManager.EntryCars)
        {
            if (car.Client == null) continue;
            var color = GetColorForSession(car.SessionId);
            if (color == Color.White) continue;

            var packet = new ChatRoleColorPacket
            {
                CarIndex = car.SessionId,
                Color = color
            };
            _entryCarManager.BroadcastPacket(packet);
        }
    }

    private void OnClientConnected(ACTcpClient sender, EventArgs args)
    {
        sender.LuaReady += OnLuaReady;
        sender.Disconnecting += OnDisconnecting;

        // Resolve role for this client
        var (success, roles) = _apiClient.GetRolesAsync(sender.Guid).GetAwaiter().GetResult();
        ChatRoleConfig? resolved = null;

        if (success)
        {
            resolved = _sortedRoles.FirstOrDefault(cr =>
                roles.Contains(cr.Role, StringComparer.OrdinalIgnoreCase));
        }

        _clientRoles[sender.SessionId] = resolved;

        if (resolved != null && !string.IsNullOrEmpty(resolved.Prefix))
            sender.Name = $"{resolved.Prefix} {sender.Name}";
    }

    private void OnDisconnecting(ACTcpClient sender, EventArgs args)
    {
        _clientRoles.TryRemove(sender.SessionId, out _);
    }

    private void OnLuaReady(ACTcpClient sender, EventArgs args)
    {
        if (_sortedRoles.Count == 0)
            return;

        // Send all existing clients' colors to the newly ready client
        foreach (var car in _entryCarManager.EntryCars)
        {
            if (car.Client != null)
            {
                var color = GetColorForSession(car.SessionId);
                var packet = new ChatRoleColorPacket
                {
                    CarIndex = car.SessionId,
                    Color = color
                };
                sender.SendPacket(packet);
            }
        }

        // Send the new client's color to all existing clients
        var newColor = GetColorForSession(sender.SessionId);
        var broadcastPacket = new ChatRoleColorPacket
        {
            CarIndex = sender.SessionId,
            Color = newColor
        };
        _entryCarManager.BroadcastPacket(broadcastPacket);

        // BroadcastPacket skips clients without HasSentFirstUpdate - send directly to those
        foreach (var car in _entryCarManager.EntryCars)
        {
            if (car.Client is { HasSentFirstUpdate: false } client && client != sender)
                client.SendPacket(broadcastPacket);
        }
    }

    private Color GetColorForSession(byte sessionId)
    {
        if (_clientRoles.TryGetValue(sessionId, out var role) && role != null)
        {
            try
            {
                return ColorTranslator.FromHtml(role.Color);
            }
            catch
            {
                return Color.White;
            }
        }

        return Color.White;
    }

    private void OnChatMessageReceived(ACTcpClient sender, ChatEventArgs args)
    {
        if (_profanityFilter == null)
            return;

        string message = _profanityFilter.Filter(args.Message);

        if (message == args.Message)
            return;

        args.Cancel = true;

        var chatMsg = new ChatMessage
        {
            SessionId = sender.SessionId,
            Message = message
        };

        foreach (var car in _entryCarManager.EntryCars)
        {
            if (car.Client is { HasSentFirstUpdate: true })
                car.Client.SendPacket(chatMsg);
        }
    }
}
