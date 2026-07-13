namespace Phonix.Api.Models;

public class ProductFeature
{
    public string Text { get; set; } = "";
    public bool Included { get; set; } = true;
}

// A question/answer pair shown on the product page and emitted as FAQPage JSON-LD so the
// listing can win an FAQ rich result and be cited by AI answer engines. Managed per product
// in the admin panel.
public class ProductFaq
{
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
}

// One input the customer must (or may) provide at checkout for a given plan — e.g. the email to send a
// Spotify/Duolingo invite to, or account credentials the team logs in with. Defined per plan in the admin
// panel. Type drives the input control on the storefront and validation; Sensitive marks secrets (passwords)
// that are encrypted at rest and kept out of plain backups.
public class PlanInputField
{
    public string Label { get; set; } = "";
    // text | email | password | phone | textarea
    public string Type { get; set; } = "text";
    public bool Required { get; set; } = true;
    public bool Sensitive { get; set; }
}

// A tutorial image or video attached to a plan's how-to section. Stored in the protected uploads area and
// streamed through an authenticated endpoint (no direct public URL), so it isn't trivially downloadable.
public class PlanTutorialMedia
{
    // image | video
    public string Kind { get; set; } = "image";
    // Storage id returned by the protected upload endpoint.
    public string Id { get; set; } = "";
}

public class ProductPlan
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int Months { get; set; }
    public long Price { get; set; }
    // When > 0 this plan is priced in USD; its Toman Price is recomputed from the live rate (see UsdRateService).
    public double PriceUsd { get; set; }
    public int DiscountPercent { get; set; }
    public bool IsActive { get; set; } = true;
    // Optional capacity shown on the plan card to help buyers pick — e.g. how many users/devices the plan
    // covers ("۵ کاربر"). 0 means "no capacity", so the badge is hidden entirely.
    public int UserCount { get; set; }
    // Optional per-plan rules/terms the buyer must read and explicitly accept at checkout before this plan
    // can be ordered. Empty = no acceptance step. A fixed liability warning (misuse → the buyer bears
    // responsibility for a suspended account) is shown alongside it on the storefront.
    public string Rules { get; set; } = "";

    // ── Per-plan "collect info from the customer" settings (all optional; off by default) ──
    // Master switch: when false the storefront skips the whole info step for this plan and the fields below
    // are ignored.
    public bool CollectsInfo { get; set; }
    // Inputs requested from the customer before payment.
    public List<PlanInputField> InputFields { get; set; } = new();
    // Short always-visible warning shown above the form (e.g. "turn off two-factor first").
    public string WarningText { get; set; } = "";
    // Longer how-to shown inside a collapsible "آموزش" panel, plus optional non-downloadable media.
    public string TutorialText { get; set; } = "";
    public List<PlanTutorialMedia> TutorialMedia { get; set; } = new();
    // When true the customer also gets a free-text optional notes box.
    public bool AllowNotes { get; set; }

    public long FinalPrice => DiscountPercent <= 0
        ? Price
        : (long)Math.Round(Price * (1 - DiscountPercent / 100.0));
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int CategoryId { get; set; }
    public long Price { get; set; }
    // When > 0 the product is priced in USD; its Toman Price above is recomputed from the live USDT→Toman
    // rate (see UsdRateService) so it tracks the exchange rate. 0 means the Toman Price is set manually.
    public double PriceUsd { get; set; }
    public int DiscountPercent { get; set; }
    public long Stock { get; set; }
    public bool IsActive { get; set; } = true;
    public bool Featured { get; set; }
    public string Image { get; set; } = "";
    public string Logo { get; set; } = "";
    // Dedicated landscape image for the product-list card, so the listing can differ from the detail-page
    // image/logo. Optional; the storefront falls back to Logo/Image when it's empty.
    public string ListImage { get; set; } = "";
    public List<string> Gallery { get; set; } = new();
    public string Sku { get; set; } = "";
    public string Description { get; set; } = "";
    public string Warning { get; set; } = "";
    // Minimum identity level required to buy this product (1 = bank card, 2 = national ID). Configured in
    // the admin panel and never shown to customers; enforced at checkout. Defaults to 1 so level-0 users
    // (registered only) can never purchase.
    public int RequiredLevel { get; set; } = 1;
    // Pre-written delivery text for this product; prefills the admin deliver modal so staff
    // don't retype the same instructions for every order of the same product. (Legacy single template,
    // kept for backward compatibility; the multi-template system below supersedes it.)
    public string DeliveryTemplate { get; set; } = "";
    // Multiple named, reusable delivery templates the admin can pick from in the deliver modal. Managed via
    // the product templates endpoints and persisted with the product.
    public List<ProductDeliveryTemplate> DeliveryTemplates { get; set; } = new();
    public List<ProductFeature> Features { get; set; } = new();
    public List<ProductFaq> Faq { get; set; } = new();
    public List<ProductPlan> Plans { get; set; } = new();

    public long FinalPrice => DiscountPercent <= 0
        ? Price
        : (long)Math.Round(Price * (1 - DiscountPercent / 100.0));
}
