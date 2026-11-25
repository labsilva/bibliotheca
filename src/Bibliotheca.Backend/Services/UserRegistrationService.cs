
using Bibliotheca.Backend.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Bibliotheca.Backend.Services;

public class UserRegistrationService : IServiceExecutor<UserRegistrationServiceArgs, UserRegistrationServiceResult>
{
    private readonly BibliothecaDbContext _context;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IConfiguration _configuration;

    public UserRegistrationService(
        BibliothecaDbContext context,
        IPasswordHasher<User> passwordHasher,
        IConfiguration configuration)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
    }

    public async Task<UserRegistrationServiceResult> ExecuteAsync(
        UserRegistrationServiceArgs args, 
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var firstUser = !await _context.Users.AnyAsync(cancellationToken);
        var inviteCode = firstUser ? null : await _context.UserInvites.AsTracking()
            .FirstOrDefaultAsync(invite => invite.InviteCode == args.InviteCode, cancellationToken);
        if (firstUser ? !IsValidBootstrapCode(args.InviteCode) : inviteCode is null)
        {
            // Invite code not found
            return UserRegistrationServiceResult.InvalidInviteCodeResult;
        }

        if (inviteCode is not null && inviteCode.InviteExpiresOn < DateTimeOffset.Now)
        {
            // Invite code expired
            return UserRegistrationServiceResult.ExpiredInviteCodeResult;
        }

        if (inviteCode?.Used == true)
        {
            return UserRegistrationServiceResult.AlreadyUsedInviteCodeResult;
        }
        var user = await _context
            .Users
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Name.Equals(args.Username), cancellationToken);

        // Check if there's a user with the same username
        if(user is not null)
        {
            return UserRegistrationServiceResult.DuplicatedUsername;
        }

        user = new User
        {
            Email = args.Email,
            FullName = args.Name,
            Name = args.Username,
            Password = string.Empty,
            Settings = "{}",
            Shelves = new List<Shelve>(),
        };
        user.Password = _passwordHasher.HashPassword(user, args.Password);

        var shelve = new Shelve
        {
            ShelveId = (await _context.Shelves.CountAsync(cancellationToken)) + 1,
            ShelveName = "Favorites",
            User = user,
        };
        user.Shelves.Add(shelve);

        if (inviteCode is not null)
            inviteCode.Used = true;
        await _context.Users.AddAsync(user, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return UserRegistrationServiceResult.SuccessResult;
    }

    private bool IsValidBootstrapCode(string supplied)
    {
        var configured = _configuration["Authentication:BootstrapCode"];
        if (configured is null || configured.Length < 32 || string.IsNullOrEmpty(supplied))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)));
    }
}

public record UserRegistrationServiceArgs(
    string InviteCode,
    string Username,
    string Name,
    string Email,
    string Password);

public record UserRegistrationServiceResult(
    bool Success,
    string? ErrorMessage)
{
    public static readonly UserRegistrationServiceResult SuccessResult = new UserRegistrationServiceResult(true, null);
    public static readonly UserRegistrationServiceResult InvalidInviteCodeResult = new UserRegistrationServiceResult(false, "Invalid invite code");
    public static readonly UserRegistrationServiceResult ExpiredInviteCodeResult = new UserRegistrationServiceResult(false, "Expired invite code");
    public static readonly UserRegistrationServiceResult AlreadyUsedInviteCodeResult = new UserRegistrationServiceResult(false, "Invite code was already used");
    public static readonly UserRegistrationServiceResult DuplicatedUsername = new UserRegistrationServiceResult(false, "That username is already taken");
}
