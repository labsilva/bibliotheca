using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Migrations;
using Bibliotheca.Backend.Services;
using Extensions.Hosting.AsyncInitialization;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Initialization;

public class EntityFrameworkMigrationInitializer : IAsyncInitializer
{
    private readonly BibliothecaDbContext _context;
    private readonly ILogger<EntityFrameworkMigrationInitializer> _logger;
    private readonly IWebHostEnvironment _environment;

    public EntityFrameworkMigrationInitializer(
        BibliothecaDbContext context,
        ILogger<EntityFrameworkMigrationInitializer> logger,
        IWebHostEnvironment environment)
    {
        _context = context;
        _logger = logger;
        _environment = environment;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        PrivateStorageValidation.Validate(_environment, _context.Database.GetConnectionString()!);
        await _context.Database.MigrateAsync(cancellationToken);
        var backfill = new SearchFieldsBackfill(_context);
        await AuthorCreditRepair.RepairCatalogueAsync(_context, _logger, cancellationToken);
    }
}
