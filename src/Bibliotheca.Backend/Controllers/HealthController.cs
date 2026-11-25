using Bibliotheca.Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(BibliothecaDbContext database) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        // Readiness is for the container's local probe, never a public API bypass.
        var address = HttpContext.Connection.RemoteIpAddress;
        if (address is null || !IPAddress.IsLoopback(address))
            return NotFound();

        return await database.Database.CanConnectAsync(cancellationToken)
            ? Ok(new { ready = true })
            : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
