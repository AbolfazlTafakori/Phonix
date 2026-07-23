namespace Phonix.Api.Models;

// A V2Ray sales catalog kept entirely separate from the ordinary product catalogue: these plans are many and
// panel-specific, so mixing them into the normal products would only create confusion. A category groups
// plans; the storefront lists categories, and opening one shows that category's plans.
public class V2RayCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";   // optional public image URL, shown on the storefront card
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public string CreatedAtUtc { get; set; } = "";
}

// One sellable V2Ray plan. It carries EVERYTHING an account needs, so a purchase can be provisioned from the
// plan alone with nothing inferred: which panel and which specific inbound(s) to create the client on, and
// the exact limits the customer is buying (traffic, duration, device/IP limit). Zero means unlimited for
// traffic, duration and IP, matching the panel's own convention.
public class V2RayPlan
{
    public int Id { get; set; }
    public int CategoryId { get; set; }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    // The provisioning target: the account is created ONLY on these inbounds of this panel.
    public int PanelId { get; set; }
    public List<int> InboundIds { get; set; } = new();

    // Shown on the plan card and in the storefront. Defaulted from the chosen inbound when the operator
    // picks one, but editable — the panel is the source of truth for what is actually served, these are the
    // labels the customer sees.
    public string Protocol { get; set; } = "";   // vless / vmess / trojan …
    public string Network { get; set; } = "";    // tcp / ws / grpc …

    public long VolumeGb { get; set; }     // 0 = unlimited
    public int DurationDays { get; set; }  // 0 = never expires (a month is 30 days, a year 365)
    public int IpLimit { get; set; }       // 0 = unlimited
    public int Quantity { get; set; }      // how many of this plan may be sold; 0 = unlimited

    public long Price { get; set; }
    public int DiscountPercent { get; set; }

    public bool Active { get; set; } = true;
    public int SortOrder { get; set; }
    public string CreatedAtUtc { get; set; } = "";

    // The customer pays this: price after the plan's own discount, floored at zero.
    public long FinalPrice => DiscountPercent is > 0 and <= 100 ? Price - Price * DiscountPercent / 100 : Price;
}
