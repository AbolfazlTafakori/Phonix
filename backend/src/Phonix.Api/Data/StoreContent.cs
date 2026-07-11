using Phonix.Api.Models;

namespace Phonix.Api.Data;

public partial class StoreData
{
    private readonly List<HeroSlide> _heroSlides = new();
    private readonly List<HomeCategory> _homeCategories = new();
    private readonly List<Showcase> _showcase = new();
    private readonly List<BlogPost> _blogPosts = new();
    private SiteContent _siteContent = new();
    private AdvancedSettings _advancedSettings = new();

    private int _heroSeq;
    private int _homeCatSeq;
    private int _showcaseSeq;
    private int _blogSeq;

    private IReadOnlyList<T> GetItems<T>(List<T> list) where T : IContentItem
    {
        lock (_gate) return list.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
    }

    private T? GetItem<T>(List<T> list, int id) where T : class, IContentItem
    {
        lock (_gate) return list.FirstOrDefault(x => x.Id == id);
    }

    private bool DeleteItem<T>(List<T> list, int id) where T : IContentItem
    {
        lock (_gate)
        {
            var e = list.FirstOrDefault(x => x.Id == id);
            if (e is null) return false;
            list.Remove(e);
            return true;
        }
    }

    // hero slides

    public IReadOnlyList<HeroSlide> GetHeroSlides() => GetItems(_heroSlides);
    public HeroSlide? GetHeroSlide(int id) => GetItem(_heroSlides, id);

    public HeroSlide AddHeroSlide(HeroSlide s)
    {
        lock (_gate) { s.Id = ++_heroSeq; _heroSlides.Add(s); return s; }
    }

    public bool UpdateHeroSlide(HeroSlide s)
    {
        lock (_gate)
        {
            var e = _heroSlides.FirstOrDefault(x => x.Id == s.Id);
            if (e is null) return false;
            e.Title = s.Title;
            e.Description = s.Description;
            e.Image = s.Image;
            e.Logo = s.Logo;
            e.ButtonText = s.ButtonText;
            e.ButtonLink = s.ButtonLink;
            e.Eyebrow = s.Eyebrow;
            e.Badge = s.Badge;
            e.PriceFrom = s.PriceFrom;
            e.OldPrice = s.OldPrice;
            e.SecondaryButtonText = s.SecondaryButtonText;
            e.SecondaryButtonLink = s.SecondaryButtonLink;
            e.AccentColor = s.AccentColor;
            e.AccentScale = s.AccentScale;
            e.Trust = s.Trust;
            e.TrustColor = s.TrustColor;
            e.SortOrder = s.SortOrder;
            e.IsActive = s.IsActive;
            return true;
        }
    }

    public bool DeleteHeroSlide(int id) => DeleteItem(_heroSlides, id);

    // home categories

    public IReadOnlyList<HomeCategory> GetHomeCategories() => GetItems(_homeCategories);
    public HomeCategory? GetHomeCategory(int id) => GetItem(_homeCategories, id);

    public HomeCategory AddHomeCategory(HomeCategory c)
    {
        lock (_gate) { c.Id = ++_homeCatSeq; _homeCategories.Add(c); return c; }
    }

    public bool UpdateHomeCategory(HomeCategory c)
    {
        lock (_gate)
        {
            var e = _homeCategories.FirstOrDefault(x => x.Id == c.Id);
            if (e is null) return false;
            e.Title = c.Title;
            e.Icon = c.Icon;
            e.Href = c.Href;
            e.IconClass = c.IconClass;
            e.SortOrder = c.SortOrder;
            e.IsActive = c.IsActive;
            return true;
        }
    }

    public bool DeleteHomeCategory(int id) => DeleteItem(_homeCategories, id);

    // showcase (best sellers cards)

    public IReadOnlyList<Showcase> GetShowcase() => GetItems(_showcase);
    public Showcase? GetShowcaseItem(int id) => GetItem(_showcase, id);

    public Showcase AddShowcase(Showcase s)
    {
        lock (_gate) { s.Id = ++_showcaseSeq; _showcase.Add(s); return s; }
    }

    public bool UpdateShowcase(Showcase s)
    {
        lock (_gate)
        {
            var e = _showcase.FirstOrDefault(x => x.Id == s.Id);
            if (e is null) return false;
            e.Name = s.Name;
            e.Image = s.Image;
            e.Logo = s.Logo;
            e.Href = s.Href;
            e.SortOrder = s.SortOrder;
            e.IsActive = s.IsActive;
            return true;
        }
    }

    public bool DeleteShowcase(int id) => DeleteItem(_showcase, id);

    // blog posts

    public IReadOnlyList<BlogPost> GetBlogPosts() => GetItems(_blogPosts);
    public BlogPost? GetBlogPost(int id) => GetItem(_blogPosts, id);

    public BlogPost AddBlogPost(BlogPost p)
    {
        lock (_gate) { p.Id = ++_blogSeq; _blogPosts.Add(p); return p; }
    }

    public bool UpdateBlogPost(BlogPost p)
    {
        lock (_gate)
        {
            var e = _blogPosts.FirstOrDefault(x => x.Id == p.Id);
            if (e is null) return false;
            e.Slug = p.Slug;
            e.Tag = p.Tag;
            e.Title = p.Title;
            e.Excerpt = p.Excerpt;
            e.Content = p.Content;
            e.Date = p.Date;
            e.Image = p.Image;
            e.FeaturedOnHome = p.FeaturedOnHome;
            e.SortOrder = p.SortOrder;
            e.IsActive = p.IsActive;
            return true;
        }
    }

    public bool DeleteBlogPost(int id) => DeleteItem(_blogPosts, id);

    // site content singleton

    public SiteContent GetSiteContent()
    {
        lock (_gate) return _siteContent;
    }

    public void UpdateSiteContent(SiteContent c)
    {
        lock (_gate) _siteContent = c;
    }

    public AdvancedSettings GetAdvancedSettings()
    {
        lock (_gate) return _advancedSettings;
    }

    public void UpdateAdvancedSettings(AdvancedSettings s)
    {
        lock (_gate) _advancedSettings = s;
    }

    private void SeedContent()
    {
        AddHeroSlide(new HeroSlide
        {
            Title = "اکانت نتفلیکس ۴K Ultra HD",
            Description =
                "از ۲۰۰۷ که نتفلیکس با پیشرفت ارتباطات در دنیا تبدیل به نتفلیکس امروزی شده پیوسته در حال " +
                "پیشرفت و بهتر کردن تجربه تماشا و امکانات خود بوده است. ساخت سریال‌های موفق بزرگی چون " +
                "چیزهای عجیب (Stranger Things)، تاریک (Dark)، ویچر (The Witcher)، خانه کاغذی (Money " +
                "Heist)، بازی مرکب (Squid Games) و… گوشه‌ای از فعالیت‌های خود کمپانی بوده. این را نیز " +
                "بگوییم که فعالیت این سرویس فقط در سریال نیست و فیلم‌های معروفی نظیر تصنیف باستر اسکراگز " +
                "(The Ballad of Buster Scruggs)، داستان ازدواج (Marriage Story)، شازده کوچولو (The " +
                "Little Prince) و… هم در کارنامه این کمپانی به چشم می‌آید.",
            Image = "/figma/hero-tv.png",
            Logo = "/figma/hero-netflix-n.png",
            Eyebrow = "اکانت اوریجینال · گارانتی کامل",
            Badge = "۲۰٪ تخفیف",
            PriceFrom = 99000,
            OldPrice = 125000,
            ButtonText = "خرید اشتراک",
            ButtonLink = "#",
            SecondaryButtonText = "مشاهده پلن‌ها",
            SecondaryButtonLink = "#",
            AccentColor = "#e60053",
            Trust = new List<TrustItem>
            {
                new() { Icon = "bolt", Label = "تحویل آنی" },
                new() { Icon = "shield", Label = "گارانتی کامل" },
                new() { Icon = "lock", Label = "پرداخت امن" },
                new() { Icon = "headset", Label = "پشتیبانی ۲۴/۷" },
            },
            SortOrder = 1,
        });
        AddHeroSlide(new HeroSlide
        {
            Title = "اشتراک پریمیوم اسپاتیفای",
            Description =
                "موزیک بدون تبلیغات، دانلود آفلاین و کیفیت بالا؛ فعال‌سازی روی اکانت خودتان با " +
                "دسترسی به میلیون‌ها آهنگ و پادکست اختصاصی.",
            Image = "/figma/prod-spotify.png",
            Logo = "",
            Eyebrow = "فعال‌سازی روی اکانت خودتان",
            Badge = "۲۲٪ تخفیف",
            PriceFrom = 69000,
            OldPrice = 89000,
            ButtonText = "خرید اشتراک",
            ButtonLink = "#",
            SecondaryButtonText = "مشاهده پلن‌ها",
            SecondaryButtonLink = "#",
            AccentColor = "#1db954",
            Trust = new List<TrustItem>
            {
                new() { Icon = "bolt", Label = "فعال‌سازی فوری" },
                new() { Icon = "check", Label = "بدون تبلیغات" },
                new() { Icon = "lock", Label = "پرداخت امن" },
                new() { Icon = "headset", Label = "پشتیبانی ۲۴/۷" },
            },
            SortOrder = 2,
        });

        var cats = new (string Title, string Icon, string IconClass)[]
        {
            ("کارت های اعتباری", "/figma/e67d98d153b9caf9a7453da98a1c85ae776bd4bb.png", "translate-y-3 translate-x-4"),
            ("گرافیک طراحی و تدوین", "/figma/cat-graphic.png", ""),
            ("فیلم سریال استریم ویدئویی", "/figma/cat-film.png", ""),
            ("موسیقی", "/figma/cat-music.png", "scale-125 translate-y-4"),
            ("محصولات بیشتر", "/figma/cat-more.png", ""),
            ("شبکه های اجتماعی و ارتباطات", "/figma/cat-social.png", ""),
            ("بازی و سرگرمی", "/figma/cat-games.png", ""),
            ("صرافی ارز دیجیتال", "/figma/cat-exchange.png", ""),
        };
        for (var i = 0; i < cats.Length; i++)
            AddHomeCategory(new HomeCategory { Title = cats[i].Title, Icon = cats[i].Icon, Href = "/products", IconClass = cats[i].IconClass, SortOrder = i + 1 });

        var shows = new (string Name, string Image, string? Logo)[]
        {
            ("Wise", "/figma/prod-wise.png", "/figma/logo-wise.png"),
            ("Freelancer", "/figma/prod-freelancer.png", "/figma/logo-freelancer.png"),
            ("Binance", "/figma/prod-binance.png", "/figma/logo-binance.png"),
            ("Spotify", "/figma/prod-spotify.png", null),
            ("Bybit", "/figma/prod-bybit.png", "/figma/logo-bybit.png"),
            ("Apple Music", "/figma/prod-applemusic.png", "/figma/logo-applemusic.png"),
            ("Canva", "/figma/prod-canva.png", "/figma/logo-canva.png"),
            ("Netflix", "/figma/prod-netflix.png", "/figma/logo-netflix.png"),
        };
        for (var i = 0; i < shows.Length; i++)
            AddShowcase(new Showcase { Name = shows[i].Name, Image = shows[i].Image, Logo = shows[i].Logo, Href = "#", SortOrder = i + 1 });

        var posts = new (string Slug, string Title, string Tag, string Excerpt)[]
        {
            ("secure-online-shopping", "راهنمای خرید امن اکانت‌های پریمیوم", "امنیت | ۵ دقیقه مطالعه", "چطور بدون نگرانی اکانت وریفای‌شده بخریم و از کلاهبرداری در امان بمانیم."),
            ("verify-accounts-explained", "اکانت وریفای‌شده چیست و چه مزایایی دارد؟", "آموزش | ۷ دقیقه مطالعه", "هرآنچه باید درباره حساب‌های احرازشده و کاربرد آن‌ها بدانید."),
            ("best-streaming-2024", "بهترین سرویس‌های استریم در سال جدید", "معرفی | ۶ دقیقه مطالعه", "مقایسه نتفلیکس، اسپاتیفای و اپل موزیک برای انتخاب بهتر."),
        };
        for (var i = 0; i < posts.Length; i++)
            AddBlogPost(new BlogPost
            {
                Slug = posts[i].Slug,
                Tag = posts[i].Tag,
                Title = posts[i].Title,
                Excerpt = posts[i].Excerpt,
                Content = posts[i].Excerpt + "\n\nاین متن نمونه است و می‌توانید آن را از پنل مدیریت ویرایش کنید. تیم فونیکس وریفای همواره در تلاش است تا بهترین و امن‌ترین خدمات را ارائه دهد.\n\nبرای اطلاعات بیشتر با پشتیبانی در ارتباط باشید.",
                Date = "۱۴۰۳/۰۳/۲۰",
                Image = $"/figma/blog-{i + 1}.png",
                FeaturedOnHome = true,
                SortOrder = i + 1,
            });

        _siteContent = new SiteContent
        {
            Brand = new BrandInfo { SiteName = "Phoenix Verify", LogoLine1 = "Phoenix", LogoLine2 = "Verify", Logo = "/figma/logo-phoenix.png" },
            Header = new HeaderContent
            {
                SearchPlaceholder = "جست و جو ...",
                CartLabel = "سبد خرید",
                CartLink = "#",
                AccountLabel = "حساب کاربری",
                AccountLink = "/login",
                NavLinks = new List<NavLink>
                {
                    new() { Label = "خانه", Href = "/" },
                    new() { Label = "محصولات", Href = "/products", HasMenu = true },
                },
            },
            Stats = new List<StatItem>
            {
                new() { Value = null, Label = "پرداخت امن", Icon = "/figma/icon-secure.png" },
                new() { Value = null, Label = "پشتیبانی آنلاین", Icon = "/figma/icon-support.png" },
                new() { Value = "+10,000", Label = "خرید ثبت شده", Icon = null },
            },
            Sections = new SectionTitles
            {
                CategoriesTitle = "لیست محصولات",
                BestSellersTitle = "محصولات پر فروش",
                BlogTitle = "مطالب وبلاگ",
            },
            Footer = new FooterContent
            {
                AboutTitle = "فونیکس وریفای",
                AboutText = "مرجع حساب‌های وریفای‌شده‌ی پلتفرم‌های محبوب، با ضمانت اصالت و پشتیبانی واقعی.",
                LinksTitle = "لینک های مهم",
                Links = new List<NavLink>
                {
                    new() { Label = "فروشگاه", Href = "/products" },
                    new() { Label = "سبد خرید", Href = "#" },
                    new() { Label = "تماس با ما", Href = "#" },
                    new() { Label = "قوانین و مقررات", Href = "#" },
                    new() { Label = "حساب کاربری من", Href = "/account" },
                },
                Columns = new List<FooterColumn>
                {
                    new()
                    {
                        Title = "دسترسی سریع",
                        Links = new List<NavLink>
                        {
                            new() { Label = "فروشگاه", Href = "/products" },
                            new() { Label = "محصولات پرفروش", Href = "/products" },
                            new() { Label = "وبلاگ", Href = "/blog" },
                            new() { Label = "قوانین و مقررات", Href = "#" },
                        },
                    },
                    new()
                    {
                        Title = "خدمات مشتریان",
                        Links = new List<NavLink>
                        {
                            new() { Label = "حساب کاربری من", Href = "/account" },
                            new() { Label = "پیگیری سفارش", Href = "/account/orders" },
                            new() { Label = "سؤالات متداول", Href = "#" },
                            new() { Label = "تماس با ما", Href = "#" },
                        },
                    },
                },
                Contact = new FooterContact
                {
                    Phone = "۰۲۱-۱۲۳۴۵۶۷۸",
                    Email = "support@phonix.ir",
                    Hours = "هر روز ۹ تا ۲۴",
                },
                TrustSeals = new List<TrustSeal>
                {
                    new() { Title = "نماد اعتماد", Subtitle = "eNamad", Link = "#", Enabled = true },
                    new() { Title = "ساماندهی", Subtitle = "ارشاد", Link = "#", Enabled = true },
                },
                Socials = new List<SocialLink>
                {
                    new() { Label = "twitter", Icon = "twitter", Href = "#" },
                    new() { Label = "Telegram", Icon = "telegram", Href = "#" },
                    new() { Label = "instagram", Icon = "instagram", Href = "#" },
                },
                Copyright = "تمام حقوق برای فونیکس وریفای محفوظ است",
            },
        };

        _advancedSettings = new AdvancedSettings
        {
            MetaTitle = "فونیکس وریفای | Phoenix Verify",
            MetaDescription = "بزرگ‌ترین مرجع ارائه حساب‌های وریفای‌شده پلتفرم‌های محبوب. خرید امن، پشتیبانی آنلاین و بهترین تجربه خرید دیجیتال.",
            MetaKeywords = "وریفای, اکانت پریمیوم, نتفلیکس, اسپاتیفای",
            MaintenanceMode = false,
            MaintenanceTitle = "سایت در حال به‌روزرسانی است",
            MaintenanceMessage = "در حال ارتقای سرویس برای تجربه‌ای بهتر هستیم. لطفاً کمی بعد دوباره سر بزنید — به‌زودی برمی‌گردیم.",
            AnalyticsId = "",
            CustomHeadScript = "",
            Terms = "قوانین و مقررات استفاده از سرویس را از پنل مدیریت اینجا بنویسید.",
        };
    }
}
