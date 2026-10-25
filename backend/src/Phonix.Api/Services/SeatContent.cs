using System.Text.RegularExpressions;

namespace Phonix.Api.Services;

// Reading back the seat blocks a delivery was built from (see StockFulfillmentService.BuildSlotDeliveryContent).
//
// The customer's panel already parses this content to list the profiles, and the seat index it derives there is
// what a buyer's submission is filed against — so the SAME parse has to exist server-side for staff to address
// "seat 3 of this account" and mean the same seat. Kept deliberately in step with the frontend's parseSeats:
// blocks split on the divider, a block counts as a seat only when it carries its own «User : A - 8» label, and
// the index is its position among those.
public static class SeatContent
{
    private static readonly Regex DividerLine = new(@"^\s*[─—-]{3,}\s*$", RegexOptions.Compiled);
    private static readonly Regex UserLine = new(@"^user\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PassLine = new(@"^pass\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const string Divider = "──────────";
    private static readonly string Join = $"\n\n{Divider}\n\n";

    // Raw blocks, in delivery order, with the seat index of each (-1 for a block that is not a seat).
    private static List<(string Raw, int SeatIndex, string Label, string Account)> Blocks(string? content)
    {
        var blocks = new List<List<string>> { new() };
        foreach (var line in (content ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (DividerLine.IsMatch(line)) blocks.Add(new List<string>());
            else blocks[^1].Add(line);
        }

        var result = new List<(string, int, string, string)>();
        var seatIndex = 0;
        foreach (var block in blocks)
        {
            var raw = string.Join("\n", block).Trim();
            if (raw.Length == 0) continue;
            var users = block.Select(l => l.Trim()).Where(l => UserLine.IsMatch(l))
                .Select(l => l[(l.IndexOf(':') + 1)..].Trim()).ToList();
            // «User :» appears twice in a seat block — the shared account's login first, then the seat's own
            // label last. One occurrence means this block is not a seat.
            if (users.Count > 1)
            {
                result.Add((raw, seatIndex, users[^1], users[0]));
                seatIndex++;
            }
            else result.Add((raw, -1, "", users.FirstOrDefault() ?? ""));
        }
        return result;
    }

    // The seats this delivery defines, or nothing when it isn't in the seat format.
    public static IReadOnlyList<DeliveredSeat> Parse(string? content) =>
        Blocks(content).Where(b => b.SeatIndex >= 0)
            .Select(b => new DeliveredSeat(b.SeatIndex, b.Label, b.Account)).ToList();

    // Same, but a delivery with no seat blocks at all (a plain one-user account, or a legacy delivery) counts
    // as ONE seat at index 0 with no label — which is how staff address "this whole account" with the same
    // per-seat machinery.
    public static IReadOnlyList<DeliveredSeat> ParseOrWhole(string? content)
    {
        var seats = Parse(content);
        if (seats.Count > 0) return seats;
        return string.IsNullOrWhiteSpace(content)
            ? Array.Empty<DeliveredSeat>()
            : new[] { new DeliveredSeat(0, "", "") };
    }

    // The content as a buyer whose listed seats are out of warranty may see it. A revoked seat keeps its block
    // and its label — the panel still shows the profile, greyed, with the reason in place of the details — but
    // the credentials are gone from the payload, not merely hidden by the page. A delivery with no seat blocks
    // is withheld whole (index 0 addresses the entire account).
    public static string MaskRevoked(string? content, IReadOnlyCollection<int> revokedSeatIndexes)
    {
        if (revokedSeatIndexes.Count == 0) return content ?? "";
        var blocks = Blocks(content);
        if (blocks.All(b => b.SeatIndex < 0))
            return revokedSeatIndexes.Contains(0) ? "" : content ?? "";

        return string.Join(Join, blocks.Select(b =>
            b.SeatIndex >= 0 && revokedSeatIndexes.Contains(b.SeatIndex) ? Strip(b.Raw) : b.Raw));
    }

    // Empties one seat block of everything that gets someone into the account, leaving the header and the
    // seat's own label so the profile is still identifiable in the buyer's panel.
    private static string Strip(string raw)
    {
        var lines = raw.Replace("\r\n", "\n").Split('\n').ToList();
        var seen = 0;
        var kept = new List<string>();
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (PassLine.IsMatch(t)) continue;      // the password never travels for a revoked seat
            if (UserLine.IsMatch(t) && seen++ == 0)
            {
                kept.Add("User : —");               // the account login, withheld
                continue;
            }
            kept.Add(line);
        }
        return string.Join("\n", kept).Trim();
    }
}
