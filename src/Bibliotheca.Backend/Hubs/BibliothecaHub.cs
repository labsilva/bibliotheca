using Microsoft.AspNetCore.SignalR;

namespace Bibliotheca.Backend.Hubs;

public sealed class BibliothecaHub : Hub
{
    public const string BookImported = "BookImported";
}
