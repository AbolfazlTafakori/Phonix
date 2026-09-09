using Microsoft.AspNetCore.DataProtection;

namespace Phonix.Api.Security;

// The price a buyer is charged is the price they were looking at when they committed to paying.
//
// Products priced in USD follow a live rate that moves under an open page, and the card-to-card flow asks
// the buyer to transfer the money BEFORE the order exists: they read an amount, leave for their banking app,
// come back with a receipt. Pricing the order from the catalogue at that later moment could file it for more
// than they actually transferred, through no fault of theirs.
//
// So the moment the buyer engages payment, the server quotes the line prices and signs them. The quote is
// carried by the client and honoured when the order is placed. It is a Data Protection token — signed with
// the same persisted key ring as sessions, scoped to its own purpose, and time-limited — so it cannot be
// edited into a cheaper price, replayed against another account, or used after it goes stale.
public interface IPriceLock
{
    string Issue(int userId, IEnumerable<(int productId, int? planId, long unitPrice)> lines);

    // The locked unit price per (product, plan), or null when the token is missing, tampered with, expired,
    // or belongs to somebody else.
    IReadOnlyDictionary<(int productId, int? planId), long>? Resolve(string? token, int userId);
}

public sealed class PriceLock : IPriceLock
{
    // Long enough for a real card-to-card transfer — open the banking app, log in, wait for the SMS code —
    // and short enough that the rate cannot wander far from what was quoted.
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);
    private const string Purpose = "Phonix.PriceLock.v1";

    private readonly ITimeLimitedDataProtector _protector;

    public PriceLock(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public string Issue(int userId, IEnumerable<(int productId, int? planId, long unitPrice)> lines)
    {
        var body = string.Join(",", lines
            .Where(l => l.unitPrice > 0)
            .Select(l => $"{l.productId}:{l.planId?.ToString() ?? ""}:{l.unitPrice}"));
        return _protector.Protect($"{userId}|{body}", Lifetime);
    }

    public IReadOnlyDictionary<(int productId, int? planId), long>? Resolve(string? token, int userId)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        string payload;
        try
        {
            payload = _protector.Unprotect(token);
        }
        catch
        {
            return null; // tampered with, expired, or issued under a key ring this node no longer has
        }

        var parts = payload.Split('|', 2);
        // The owner is inside the signed payload: a quote handed to one account can never price another's order.
        if (parts.Length != 2 || !int.TryParse(parts[0], out var owner) || owner != userId) return null;

        var map = new Dictionary<(int, int?), long>();
        foreach (var entry in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = entry.Split(':');
            if (f.Length != 3) continue;
            if (!int.TryParse(f[0], out var productId)) continue;
            int? planId = string.IsNullOrEmpty(f[1]) ? null : int.TryParse(f[1], out var pl) ? pl : null;
            if (!long.TryParse(f[2], out var price) || price <= 0) continue;
            map[(productId, planId)] = price;
        }
        return map.Count > 0 ? map : null;
    }
}
