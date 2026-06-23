using CherryBox.Core.Configuration;
using CherryBox.Core.Entities;
using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CherryBox.Download.Plugin;

public sealed class DownloadLimitService : IDownloadLimitService
{
    private readonly CherryBoxDbContext _db;
    private readonly IConfigManager _config;

    public DownloadLimitService(CherryBoxDbContext db, IConfigManager config)
    {
        _db = db;
        _config = config;
    }

    public async Task<DownloadLimitUsageDto?> GetUsageAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return null;

        return await BuildUsageAsync(user, cancellationToken);
    }

    public async Task<(bool Allowed, string? BlockReason)> CanEnqueueAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return (false, "User not found.");

        if (user.Role == UserRole.Admin)
            return (true, null);

        var usage = await BuildUsageAsync(user, cancellationToken);
        if (!usage.IsLimited)
            return (true, null);

        if (usage.UsedCount + usage.InFlightCount < usage.LimitMax + usage.BonusCount)
            return (true, null);

        var periodLabel = FormatPeriodLabel(usage.Period);
        return (
            false,
            $"Download limit reached ({usage.UsedCount + usage.InFlightCount}/{usage.LimitMax + usage.BonusCount} per {periodLabel}). " +
            "Request more downloads from an admin in Account settings.");
    }

    public async Task<DownloadLimitRequestDto?> CreateRequestAsync(
        Guid userId,
        CreateDownloadLimitRequestRequest request,
        CancellationToken cancellationToken = default)
    {
        var hasPending = await _db.DownloadLimitRequests.AnyAsync(
            r => r.UserId == userId && r.Status == ViewTimeRequestStatus.Pending,
            cancellationToken);
        if (hasPending)
            throw new InvalidOperationException("You already have a pending download limit request.");

        var entity = new DownloadLimitRequest
        {
            UserId = userId,
            RequestedCount = request.RequestedCount,
            Message = request.Message?.Trim()
        };
        _db.DownloadLimitRequests.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, cancellationToken);
        return ToRequestDto(entity, user.Username);
    }

    public Task<DownloadLimitPolicyDto> GetPolicyAsync(CancellationToken cancellationToken = default)
    {
        var download = _config.Current.Download;
        return Task.FromResult(new DownloadLimitPolicyDto(
            download.DefaultDownloadLimitMax,
            download.DefaultDownloadLimitPeriod));
    }

    public async Task<DownloadLimitPolicyDto> UpdatePolicyAsync(
        UpdateDownloadLimitPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DefaultDownloadLimitMax < 0)
            throw new InvalidOperationException("Download limit must be zero or greater.");

        _config.Current.Download.DefaultDownloadLimitMax = request.DefaultDownloadLimitMax;
        _config.Current.Download.DefaultDownloadLimitPeriod = request.DefaultDownloadLimitPeriod;
        await _config.SaveAsync(cancellationToken);

        return new DownloadLimitPolicyDto(
            request.DefaultDownloadLimitMax,
            request.DefaultDownloadLimitPeriod);
    }

    public async Task<IReadOnlyList<DownloadLimitUserDto>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(cancellationToken);
        var results = new List<DownloadLimitUserDto>();
        foreach (var user in users)
        {
            var usage = await BuildUsageAsync(user, cancellationToken);
            results.Add(new DownloadLimitUserDto(
                user.Id,
                user.Username,
                user.Role,
                user.DownloadLimitMax,
                user.DownloadLimitPeriod,
                usage.LimitMax,
                usage.Period,
                usage.UsedCount,
                usage.InFlightCount,
                usage.BonusCount,
                usage.RemainingCount));
        }

        return results;
    }

    public async Task<DownloadLimitUserDto?> UpdateUserAsync(
        Guid userId,
        UpdateDownloadLimitUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return null;

        if (request.UseDefaultLimit)
        {
            user.DownloadLimitMax = null;
            user.DownloadLimitPeriod = null;
        }
        else
        {
            if (request.DownloadLimitMax.HasValue)
            {
                if (request.DownloadLimitMax.Value < 0)
                    throw new InvalidOperationException("Download limit must be zero or greater.");
                user.DownloadLimitMax = request.DownloadLimitMax.Value;
            }

            if (request.DownloadLimitPeriod.HasValue)
                user.DownloadLimitPeriod = request.DownloadLimitPeriod.Value;
        }

        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var usage = await BuildUsageAsync(user, cancellationToken);
        return new DownloadLimitUserDto(
            user.Id,
            user.Username,
            user.Role,
            user.DownloadLimitMax,
            user.DownloadLimitPeriod,
            usage.LimitMax,
            usage.Period,
            usage.UsedCount,
            usage.InFlightCount,
            usage.BonusCount,
            usage.RemainingCount);
    }

    public async Task<IReadOnlyList<DownloadLimitRequestDto>> ListRequestsAsync(CancellationToken cancellationToken = default)
    {
        var requests = await _db.DownloadLimitRequests
            .AsNoTracking()
            .Include(r => r.User)
            .ToListAsync(cancellationToken);

        return requests
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => ToRequestDto(r, r.User?.Username ?? "Unknown"))
            .ToList();
    }

    public async Task<DownloadLimitRequestDto?> ResolveRequestAsync(
        Guid id,
        Guid adminUserId,
        ResolveDownloadLimitRequestRequest request,
        CancellationToken cancellationToken = default)
    {
        var limitRequest = await _db.DownloadLimitRequests
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (limitRequest is null || limitRequest.Status != ViewTimeRequestStatus.Pending)
            return null;

        var user = limitRequest.User
            ?? await _db.Users.FirstOrDefaultAsync(u => u.Id == limitRequest.UserId, cancellationToken);
        if (user is null)
            return null;

        limitRequest.ResolvedByUserId = adminUserId;
        limitRequest.ResolvedAt = DateTimeOffset.UtcNow;
        limitRequest.AdminNote = request.AdminNote?.Trim();

        if (request.Approve)
        {
            var granted = request.GrantedCount ?? limitRequest.RequestedCount ?? 1;
            if (granted <= 0)
                throw new InvalidOperationException("Granted download count must be positive.");

            var period = ResolvePeriod(user);
            var periodStart = DownloadLimitPeriodHelper.GetPeriodStart(period, DateTimeOffset.UtcNow);
            var currentBonus = GetEffectiveBonus(user, periodStart);
            user.DownloadLimitBonus = currentBonus + granted;
            user.DownloadLimitBonusPeriodStart = periodStart;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            limitRequest.Status = ViewTimeRequestStatus.Approved;
            limitRequest.GrantedCount = granted;
        }
        else
        {
            limitRequest.Status = ViewTimeRequestStatus.Denied;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToRequestDto(limitRequest, user.Username);
    }

    private async Task<DownloadLimitUsageDto> BuildUsageAsync(User user, CancellationToken cancellationToken)
    {
        var period = ResolvePeriod(user);
        var now = DateTimeOffset.UtcNow;
        var periodStart = DownloadLimitPeriodHelper.GetPeriodStart(period, now);
        var periodResetsAt = DownloadLimitPeriodHelper.GetPeriodResetsAt(period, now);
        var limitMax = ResolveLimitMax(user);
        var isLimited = user.Role != UserRole.Admin && limitMax > 0;
        var bonus = isLimited ? GetEffectiveBonus(user, periodStart) : 0;

        var usedCount = 0;
        var inFlightCount = 0;
        if (isLimited)
        {
            usedCount = await CountCompletedAsync(user.Id, periodStart, cancellationToken);
            inFlightCount = await CountInFlightAsync(user.Id, cancellationToken);
        }

        var remaining = isLimited
            ? Math.Max(0, limitMax + bonus - usedCount - inFlightCount)
            : int.MaxValue;

        var hasPendingRequest = await _db.DownloadLimitRequests.AnyAsync(
            r => r.UserId == user.Id && r.Status == ViewTimeRequestStatus.Pending,
            cancellationToken);

        return new DownloadLimitUsageDto(
            isLimited,
            limitMax,
            period,
            usedCount,
            inFlightCount,
            bonus,
            remaining,
            periodStart,
            periodResetsAt,
            hasPendingRequest);
    }

    private int ResolveLimitMax(User user)
    {
        if (user.Role == UserRole.Admin)
            return 0;

        if (user.DownloadLimitMax.HasValue)
            return Math.Max(0, user.DownloadLimitMax.Value);

        return Math.Max(0, _config.Current.Download.DefaultDownloadLimitMax);
    }

    private DownloadLimitPeriod ResolvePeriod(User user) =>
        user.DownloadLimitPeriod ?? _config.Current.Download.DefaultDownloadLimitPeriod;

    private static int GetEffectiveBonus(User user, DateTimeOffset periodStart)
    {
        if (!user.DownloadLimitBonusPeriodStart.HasValue)
            return 0;

        return user.DownloadLimitBonusPeriodStart.Value == periodStart
            ? Math.Max(0, user.DownloadLimitBonus)
            : 0;
    }

    private async Task<int> CountCompletedAsync(Guid userId, DateTimeOffset periodStart, CancellationToken cancellationToken)
    {
        var jobs = await _db.DownloadJobs
            .AsNoTracking()
            .Where(j => j.CreatedByUserId == userId && j.Status == DownloadJobStatus.Completed)
            .ToListAsync(cancellationToken);

        return jobs.Count(j => j.UpdatedAt >= periodStart);
    }

    private Task<int> CountInFlightAsync(Guid userId, CancellationToken cancellationToken) =>
        _db.DownloadJobs.CountAsync(
            j => j.CreatedByUserId == userId &&
                 (j.Status == DownloadJobStatus.Pending || j.Status == DownloadJobStatus.Running),
            cancellationToken);

    private static DownloadLimitRequestDto ToRequestDto(DownloadLimitRequest request, string username) =>
        new(
            request.Id,
            request.UserId,
            username,
            request.RequestedCount,
            request.Message,
            request.Status,
            request.GrantedCount,
            request.AdminNote,
            request.CreatedAt,
            request.ResolvedAt);

    private static string FormatPeriodLabel(DownloadLimitPeriod period) => period switch
    {
        DownloadLimitPeriod.Hour => "hour",
        DownloadLimitPeriod.Day => "day",
        DownloadLimitPeriod.Week => "week",
        DownloadLimitPeriod.Month => "month",
        DownloadLimitPeriod.Year => "year",
        _ => "period"
    };
}

internal static class DownloadLimitPeriodHelper
{
    public static DateTimeOffset GetPeriodStart(DownloadLimitPeriod period, DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return period switch
        {
            DownloadLimitPeriod.Hour => utc.AddHours(-1),
            DownloadLimitPeriod.Day => new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero),
            DownloadLimitPeriod.Week => StartOfWeek(utc),
            DownloadLimitPeriod.Month => new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero),
            DownloadLimitPeriod.Year => new DateTimeOffset(utc.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
            _ => utc.AddHours(-1)
        };
    }

    public static DateTimeOffset GetPeriodResetsAt(DownloadLimitPeriod period, DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return period switch
        {
            DownloadLimitPeriod.Hour => utc.AddHours(1),
            DownloadLimitPeriod.Day => new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1),
            DownloadLimitPeriod.Week => StartOfWeek(utc).AddDays(7),
            DownloadLimitPeriod.Month => new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1),
            DownloadLimitPeriod.Year => new DateTimeOffset(utc.Year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero),
            _ => utc.AddHours(1)
        };
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset utc)
    {
        var date = utc.Date;
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return new DateTimeOffset(date.AddDays(-diff), TimeSpan.Zero);
    }
}
