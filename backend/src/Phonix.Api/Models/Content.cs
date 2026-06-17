namespace Phonix.Api.Models;

public interface IContentItem
{
    int Id { get; set; }
    int SortOrder { get; set; }
}

public class HeroSlide : IContentItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Image { get; set; } = "";
    public string Logo { get; set; } = "";
    public string ButtonText { get; set; } = "";
    public string ButtonLink { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class HomeCategory : IContentItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Href { get; set; } = "";
    public string IconClass { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Showcase : IContentItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string? Logo { get; set; }
    public string Href { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class BlogPost : IContentItem
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Title { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public string Content { get; set; } = "";
    public string Date { get; set; } = "";
    public string Image { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class NavLink
{
    public string Label { get; set; } = "";
    public string Href { get; set; } = "";
    public bool HasMenu { get; set; }
}

public class StatItem
{
    public string? Value { get; set; }
    public string Label { get; set; } = "";
    public string? Icon { get; set; }
}

public class SocialLink
{
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Href { get; set; } = "";
}

public class BrandInfo
{
    public string SiteName { get; set; } = "";
    public string LogoLine1 { get; set; } = "";
    public string LogoLine2 { get; set; } = "";
    public string Logo { get; set; } = "";
}

public class HeaderContent
{
    public string SearchPlaceholder { get; set; } = "";
    public string CartLabel { get; set; } = "";
    public string CartLink { get; set; } = "";
    public string AccountLabel { get; set; } = "";
    public string AccountLink { get; set; } = "";
    public List<NavLink> NavLinks { get; set; } = new();
}

public class SectionTitles
{
    public string CategoriesTitle { get; set; } = "";
    public string BestSellersTitle { get; set; } = "";
    public string BlogTitle { get; set; } = "";
}

public class FooterContent
{
    public string AboutTitle { get; set; } = "";
    public string AboutText { get; set; } = "";
    public string LinksTitle { get; set; } = "";
    public List<NavLink> Links { get; set; } = new();
    public List<SocialLink> Socials { get; set; } = new();
    public string Copyright { get; set; } = "";
}

public class SiteContent
{
    public BrandInfo Brand { get; set; } = new();
    public HeaderContent Header { get; set; } = new();
    public List<StatItem> Stats { get; set; } = new();
    public SectionTitles Sections { get; set; } = new();
    public FooterContent Footer { get; set; } = new();
}

public class AdvancedSettings
{
    public string MetaTitle { get; set; } = "";
    public string MetaDescription { get; set; } = "";
    public string MetaKeywords { get; set; } = "";
    public bool MaintenanceMode { get; set; }
    public string MaintenanceTitle { get; set; } = "";
    public string MaintenanceMessage { get; set; } = "";
    public string AnalyticsId { get; set; } = "";
    public string CustomHeadScript { get; set; } = "";
}
