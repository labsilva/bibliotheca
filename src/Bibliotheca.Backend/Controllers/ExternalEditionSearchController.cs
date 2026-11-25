using System.ComponentModel.DataAnnotations;
using Bibliotheca.Backend.Authorization;
using Bibliotheca.Backend.Jobs;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/external-editions")]
public sealed class ExternalEditionSearchController(IExternalEditionSearch search) : ControllerBase
{
    /// <summary>Search enabled external providers for editions by title and optional author.</summary>
    [HttpGet("search")]
    [ProducesResponseType<ExternalEditionSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<ExternalEditionSearchResponse> SearchAsync(
        [FromQuery, Required, StringLength(200)] string title,
        [FromQuery, StringLength(200)] string? author,
        CancellationToken cancellationToken)
        => search.SearchAsync(title, author, cancellationToken);

    /// <summary>List external search providers and their current configuration readiness.</summary>
    [HttpGet("providers")]
    [ProducesResponseType<IReadOnlyList<ExternalProviderStatus>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<IReadOnlyList<ExternalProviderStatus>> GetProviders(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Ok(search.GetProviders());
    }

    /// <summary>Queue an external edition for import or return its existing catalogue entry.</summary>
    [HttpPost("import")]
    [ProducesResponseType<ExistingExternalEditionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<BookImportStatus>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ImportAsync(
        ImportExternalEditionRequest request,
        [FromServices] BookImportQueue queue,
        [FromServices] StoreExternalEdition store,
        CancellationToken cancellationToken)
    {
        var owner = User.GetUsername();
        var existing = await queue.SubmitEditionAsync(owner, request.ProviderId, request.ExternalId,
            null, request.ShelfId, store, cancellationToken);
        if (existing is not null)
            return existing;

        ExternalEdition? edition;
        try
        {
            edition = await search.ResolveAsync(request.ProviderId, request.ExternalId, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, detail: "The provider could not resolve this edition.");
        }

        if (edition is null)
            return NotFound();

        return await queue.SubmitEditionAsync(owner, request.ProviderId, request.ExternalId,
            edition, request.ShelfId, store, cancellationToken) ?? NotFound();
    }
}

/// <summary>The selected provider edition and optional shelf owned by the caller.</summary>
public sealed record ImportExternalEditionRequest(
    [Required, StringLength(64)] string ProviderId,
    [Required, StringLength(200)] string ExternalId,
    [Range(1, int.MaxValue)] int? ShelfId);
