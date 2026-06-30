import { cache } from "react";
import { api } from "./api";
import type { HeroSlide, HomeCategory, Showcase, BlogPost, SiteContent, AdvancedSettings, Comment } from "./types";
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
    title: "اکانت نتفلیکس ۴K Ultra HD",
    description:
      "تحویل آنی روی پروفایل اختصاصی شما، با کیفیت Ultra HD و گارانتی کامل دورهٔ اشتراک. دسترسی به هزاران فیلم و سریال محبوب نتفلیکس بدون محدودیت.",
    image: "/figma/hero-tv.png",
    logo: "/figma/hero-netflix-n.png",
    eyebrow: "اکانت اوریجینال · گارانتی کامل",
    badge: "۲۰٪ تخفیف",
    priceFrom: 99000,
    oldPrice: 125000,
    buttonText: "خرید اشتراک",
    buttonLink: "#",
    secondaryButtonText: "مشاهده پلن‌ها",
    secondaryButtonLink: "#",
    accentColor: "#e60053",
    accentScale: 1,
    trust: [
      { icon: "bolt", label: "تحویل آنی" },
      { icon: "shield", label: "گارانتی کامل" },
      { icon: "lock", label: "پرداخت امن" },
      { icon: "headset", label: "پشتیبانی ۲۴/۷" },
    ],
    trustColor: "",
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
    aboutTitle: "فونیکس ورفای",
    aboutText: "مرجع حساب‌های وریفای‌شده‌ی پلتفرم‌های محبوب، با ضمانت اصالت و پشتیبانی واقعی.",
    linksTitle: "لینک های مهم",
    links: homeFooter.map((l) => ({ label: l.label, href: l.href })),
    columns: [
      {
        title: "دسترسی سریع",
        links: [
          { label: "فروشگاه", href: "/films" },
          { label: "محصولات پرفروش", href: "/products" },
          { label: "وبلاگ", href: "/blog" },
          { label: "قوانین و مقررات", href: "#" },
        ],
      },
      {
        title: "خدمات مشتریان",
        links: [
          { label: "حساب کاربری من", href: "/account" },
          { label: "پیگیری سفارش", href: "/account/orders" },
          { label: "سؤالات متداول", href: "#" },
          { label: "تماس با ما", href: "#" },
        ],
      },
    ],
    contact: { phone: "۰۲۱-۱۲۳۴۵۶۷۸", email: "support@phonix.ir", hours: "هر روز ۹ تا ۲۴", address: "" },
    trustSeals: [
      { title: "نماد اعتماد", subtitle: "eNamad", link: "#", enabled: true },
      { title: "ساماندهی", subtitle: "ارشاد", link: "#", enabled: true },
    ],
    socials: [
      { label: "twitter", icon: "twitter", href: "#" },
      { label: "Telegram", icon: "telegram", href: "#" },
      { label: "instagram", icon: "instagram", href: "#" },
    ],
    copyright: "تمام حقوق برای فونیکس ورفای محفوظ است",
  },
  blogAutoplaySeconds: 5,
  testimonialsEnabled: false,
  testimonialsAutoplaySeconds: 5,
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
  featuredOnHome: true,
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

// Approved reviews the admin flagged for the home page. Empty (or a failed fetch) hides the section.
export const getTestimonials = cache(async (): Promise<Comment[]> => {
  try {
    return await api.testimonials.list();
  } catch {
    return [];
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
  terms: "",
};

export const getAdvancedSettings = cache(async (): Promise<AdvancedSettings> => {
  try {
    return await api.advancedSettings.get();
  } catch {
    return defaultAdvanced;
  }
});
