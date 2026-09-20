namespace Phonix.Api.Models;

// A WireGuard sales catalogue kept entirely separate from the ordinary product catalogue AND from the V2Ray
// one: the plans are many and panel-specific, and a tunnel plan (quota + devices across servers) is not a
// V2Ray plan (inbounds + IP limit). A category groups plans; the storefront lists categories, and opening
// one shows that category's plans.
public class WireGuardCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";   // optional public image URL, shown on the storefront card
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public string CreatedAtUtc { get; set; } = "";
}

// One sellable WireGuard plan. It carries EVERYTHING a customer record needs, so a purchase can be
// provisioned from the plan alone: which panel and which specific tunnel(s) to place the customer on, and
// the exact limits bought (traffic, duration, device limit). Zero means unlimited for traffic and duration,
// matching W-UI's own convention; the device limit is at least 1 because the panel issues one device per
// customer at creation.
public class WireGuardPlan
{
    public int Id { get; set; }
    public int CategoryId { get; set; }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    // The provisioning target: the customer is created ONLY on these tunnels of this panel. Several tunnels
    // on one purchase is W-UI's own model — the allowance, expiry and device limit are shared across them,
    // so one server being blocked leaves the rest working.
    public int PanelId { get; set; }
    public List<int> InterfaceIds { get; set; } = new();

    // Shown on the plan card and in the storefront. Defaulted from the chosen tunnel when the operator picks
    // one (wireguard / amneziawg / openvpn), but editable — the panel is the source of truth for what is
    // served, this is the label the customer sees.
    public string Protocol { get; set; } = "";

    public long VolumeGb { get; set; }     // 0 = unlimited
    public int DurationDays { get; set; }  // 0 = never expires (a month is 30 days, a year 365)
    public int DeviceLimit { get; set; } = 1; // how many devices the customer may hold; W-UI counts devices, not accounts
    public int Quantity { get; set; }      // how many of this plan may be sold; 0 = unlimited
    // How many have actually been sold against that cap. Meaningless while Quantity is 0.
    public int Sold { get; set; }

    // A capped plan that has run out. The storefront stops offering it and placement refuses it.
    public bool SoldOut => Quantity > 0 && Sold >= Quantity;

    public long Price { get; set; }
    public int DiscountPercent { get; set; }

    public bool Active { get; set; } = true;
    public int SortOrder { get; set; }
    public string CreatedAtUtc { get; set; } = "";

    // The customer pays this: price after the plan's own discount, floored at zero.
    public long FinalPrice => DiscountPercent is > 0 and <= 100 ? Price - Price * DiscountPercent / 100 : Price;
}
