using Microsoft.AspNetCore.SignalR;

namespace Bibliotheca.Backend.Hubs;

public sealed class BookImportUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User.GetUsername() is { Length: > 0 } owner ? owner : null;
}
