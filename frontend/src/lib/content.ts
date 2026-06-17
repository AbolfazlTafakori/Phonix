import { cache } from "react";
import { api } from "./api";
import type { HeroSlide, HomeCategory, Showcase, BlogPost, SiteContent, AdvancedSettings } from "./types";
import {
  categories as homeCats,
  products as homeProducts,
  blogPosts as homeBlog,
  navLinks as homeNav,
  footerLinks as homeFooter,
} from "@/data/home";

function sortActive<T extends { sortOrder: number; isActive: boolean }>(items: T[]): T[] {
  return items.filter((i) => i.isActive).sort((a, b) => a.sortOrder - b.sortOrder);
}

export const defaultHeroSlides: HeroSlide[] = [
  {
    id: 1,
    title: "نتفلیکس",
    description:
      "از ۲۰۰۷ که نتفلیکس با پیشرفت ارتباطات در دنیا تبدیل به نتفلیکس امروزی شده پیوسته در حال پیشرفت و بهتر کردن تجربه تماشا و امکانات خود بوده است. ساخت سریال‌های موفق بزرگی چون چیزهای عجیب (Stranger Things)، تاریک (Dark)، ویچر (The Witcher)، خانه کاغذی (Money Heist)، بازی مرکب (Squid Games) و… گوشه‌ای از فعالیت‌های خود کمپانی بوده.",
    image: "/figma/hero-tv.png",
    logo: "/figma/hero-netflix-n.png",
    buttonText: "مطالعه بیشتر",
    buttonLink: "#",
    sortOrder: 1,
    isActive: true,
  },
];

export const defaultSiteContent: SiteContent = {
  brand: { siteName: "Phoenix Verify", logoLine1: "Phoenix", logoLine2: "Verify", logo: "/figma/logo-phoenix.png" },
  header: {
    searchPlaceholder: "جست و جو ...",
    cartLabel: "سبد خرید",
    cartLink: "#",
    accountLabel: "حساب کاربری",
    accountLink: "/login",
    navLinks: homeNav.map((l) => ({ label: l.label, href: l.href, hasMenu: l.hasMenu })),
  },
  stats: [
    { value: null, label: "پرداخت امن", icon: "/figma/icon-secure.png" },
    { value: null, label: "پشتیبانی آنلاین", icon: "/figma/icon-support.png" },
    { value: "+10,000", label: "خرید ثبت شده", icon: null },
  ],
  sections: { categoriesTitle: "لیست محصولات", bestSellersTitle: "محصولات پر فروش", blogTitle: "مطالب وبلاگ" },
  footer: {
    aboutTitle: "فونیکس ورفای چیست؟",
    aboutText:
      "به بزرگ‌ترین مرجع ارائه حساب‌های وریفای‌شده پلتفرم‌های محبوب خوش آمدید! ما با افتخار بهترین و مطمئن‌ترین خدمات را برای شما فراهم می‌کنیم. ما متعهد به ارائه بهترین کیفیت و پشتیبانی به مشتریان خود هستیم. با ما، بهترین تجربه خرید آنلاین را داشته باشید.",
    linksTitle: "لینک های مهم",
    links: homeFooter.map((l) => ({ label: l.label, href: l.href })),
    socials: [
      { label: "twitter", icon: "twitter", href: "#" },
      { label: "Telegram", icon: "telegram", href: "#" },
      { label: "instagram", icon: "instagram", href: "#" },
    ],
    copyright: "تمام حقوق برای فونیکس ورفای محفوظ است",
  },
};

const defaultHomeCategories: HomeCategory[] = homeCats.map((c, i) => ({
  id: i + 1,
  title: c.title,
  icon: c.icon,
  href: c.href,
  iconClass: c.iconClass ?? "",
  sortOrder: i + 1,
  isActive: true,
}));

const defaultShowcase: Showcase[] = homeProducts.map((p, i) => ({
  id: i + 1,
  name: p.name,
  image: p.image,
  logo: p.logo,
  href: p.href,
  sortOrder: i + 1,
  isActive: true,
}));

const defaultBlogPosts: BlogPost[] = homeBlog.map((b, i) => ({
  id: i + 1,
  slug: `post-${i + 1}`,
  tag: b.tag,
  title: b.title,
  excerpt: "",
  content: "",
  date: b.date,
  image: b.image,
  sortOrder: i + 1,
  isActive: true,
}));

export const getSiteContent = cache(async (): Promise<SiteContent> => {
  try {
    return await api.siteContent.get();
  } catch {
    return defaultSiteContent;
  }
});

export const getHeroSlides = cache(async (): Promise<HeroSlide[]> => {
  try {
    return sortActive(await api.hero.list());
  } catch {
    return defaultHeroSlides;
  }
});

export const getHomeCategories = cache(async (): Promise<HomeCategory[]> => {
  try {
    return sortActive(await api.homeCategories.list());
  } catch {
    return defaultHomeCategories;
  }
});

export const getShowcase = cache(async (): Promise<Showcase[]> => {
  try {
    return sortActive(await api.showcase.list());
  } catch {
    return defaultShowcase;
  }
});

export const getBlogPosts = cache(async (): Promise<BlogPost[]> => {
  try {
    return sortActive(await api.blog.list());
  } catch {
    return defaultBlogPosts;
  }
});

export const defaultAdvanced: AdvancedSettings = {
  metaTitle: "فونیکس ورفای | Phoenix Verify",
  metaDescription: "بزرگ‌ترین مرجع ارائه حساب‌های وریفای‌شده پلتفرم‌های محبوب.",
  metaKeywords: "",
  maintenanceMode: false,
  maintenanceTitle: "سایت در حال به‌روزرسانی است",
  maintenanceMessage: "در حال ارتقای سرویس برای تجربه‌ای بهتر هستیم. لطفاً کمی بعد دوباره سر بزنید.",
  analyticsId: "",
  customHeadScript: "",
};

export const getAdvancedSettings = cache(async (): Promise<AdvancedSettings> => {
  try {
    return await api.advancedSettings.get();
  } catch {
    return defaultAdvanced;
  }
});
