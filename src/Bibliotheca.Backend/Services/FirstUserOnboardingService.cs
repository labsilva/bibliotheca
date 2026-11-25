
using Bibliotheca.Backend.Data;
using System.Security.Cryptography;

namespace Bibliotheca.Backend.Services;

public class FirstUserOnboardingService : IServiceExecutor<FirstUserOnboardingServiceArgs, FirstUserOnboardingServiceResult>
{
    private readonly BibliothecaDbContext _context;
    private readonly ILogger<FirstUserOnboardingService> _logger;

    public FirstUserOnboardingService(
        BibliothecaDbContext context,
        ILogger<FirstUserOnboardingService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<FirstUserOnboardingServiceResult> ExecuteAsync(
        FirstUserOnboardingServiceArgs args, 
        CancellationToken cancellationToken = default)
    {
        var inviteCode = new UserInvite
        {
            InviteCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            InviteExpiresOn = DateTimeOffset.Now.AddMinutes(60),
            Used = false
        };

        await _context.UserInvites.AddAsync(inviteCode, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return new FirstUserOnboardingServiceResult(inviteCode.InviteCode);
    }
}

public record FirstUserOnboardingServiceArgs();

public record FirstUserOnboardingServiceResult(string InviteCode);
