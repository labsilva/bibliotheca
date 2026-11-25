using Bibliotheca.Backend.Hubs;
using Bibliotheca.Backend.Jobs;
using Microsoft.AspNetCore.SignalR;

namespace Bibliotheca.Backend.Services;

public sealed class NotifyFetchedBookService(IHubContext<BibliothecaHub> hubContext)
{
    public Task SendAsync(string owner, BookImportNotification notification, CancellationToken cancellationToken) =>
        hubContext.Clients.User(owner).SendAsync(BibliothecaHub.BookImported, notification, cancellationToken);
}

public sealed record BookImportNotification(
    string Isbn, bool Success, string? Title, int? BookId, BookImportStatus Import);
