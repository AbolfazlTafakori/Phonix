export type AdminMenuItem = { label: string; href: string; icon: string };

export type AdminMenuGroup = { title: string; items: AdminMenuItem[] };

export const adminMenuGroups: AdminMenuGroup[] = [
  { title: "", items: [{ label: "داشبورد", href: "/admin", icon: "dashboard" }] },
  {
    title: "فروشگاه",
    items: [
      { label: "محصولات", href: "/admin/products", icon: "box" },
      { label: "دسته‌بندی‌ها", href: "/admin/categories", icon: "grid" },
      { label: "نوع سرویس (پلن‌ها)", href: "/admin/plan-types", icon: "tag" },
      { label: "سفارش‌ها", href: "/admin/orders", icon: "cart" },
    ],
  },
  {
    title: "مالی",
    items: [
      { label: "قیمت‌گذاری", href: "/admin/pricing", icon: "tag" },
      { label: "کدهای تخفیف", href: "/admin/discounts", icon: "tag" },
      { label: "روش‌های پرداخت", href: "/admin/payments", icon: "card" },
      { label: "تراکنش‌ها", href: "/admin/transactions", icon: "wallet" },
      { label: "گزارش‌ها", href: "/admin/reports", icon: "chart" },
    ],
  },
  {
    title: "کاربران",
    items: [
      { label: "مدیریت کاربران", href: "/admin/users", icon: "users" },
      { label: "احراز هویت", href: "/admin/kyc", icon: "shield" },
      { label: "نظرات و امتیازها", href: "/admin/comments", icon: "chat" },
      { label: "تیکت‌های پشتیبانی", href: "/admin/tickets", icon: "ticket" },
    ],
  },
  {
    title: "محتوای سایت",
    items: [
      { label: "هدر و منو", href: "/admin/header", icon: "layout" },
      { label: "اسلایدر اصلی", href: "/admin/banners", icon: "image" },
      { label: "بخش‌های صفحه اصلی", href: "/admin/home", icon: "home" },
      { label: "بلاگ", href: "/admin/blog", icon: "news" },
      { label: "فوتر", href: "/admin/footer", icon: "columns" },
    ],
  },
  {
    title: "تنظیمات",
    items: [
      { label: "تنظیمات عمومی", href: "/admin/settings", icon: "settings" },
      { label: "قوانین و مقررات", href: "/admin/rules", icon: "news" },
      { label: "تنظیمات ایمیل", href: "/admin/settings/email", icon: "bell" },
      { label: "پشتیبان‌گیری و بازیابی", href: "/admin/backup", icon: "disk" },
      { label: "تنظیمات پیشرفته", href: "/admin/settings/advanced", icon: "cpu" },
    ],
  },
];

export const adminMenu: AdminMenuItem[] = adminMenuGroups.flatMap((g) => g.items);

export type Resource = { label: string; used: number; detail: string; accent: string; icon: string };

export const serverResources: Resource[] = [
  { label: "پردازنده (CPU)", used: 38, detail: "۸ هسته · ۲.۴GHz", accent: "#3a64f2", icon: "cpu" },
  { label: "حافظه (RAM)", used: 62, detail: "۹.۹ از ۱۶ گیگابایت", accent: "#a855f7", icon: "ram" },
  { label: "دیسک (SSD)", used: 47, detail: "۲۳۵ از ۵۰۰ گیگابایت", accent: "#22c55e", icon: "disk" },
  { label: "پهنای باند", used: 71, detail: "۱.۴ از ۲ ترابایت", accent: "#e60053", icon: "activity" },
];

export const serverInfo = {
  status: "آنلاین",
  uptime: "۲۳ روز و ۷ ساعت",
  load: "۰.۸۴",
  requests: "۱.۲M / امروز",
};

export type Banner = {
  id: string;
  title: string;
  subtitle: string;
  image: string;
  position: number;
  status: "فعال" | "غیرفعال";
};

export const banners: Banner[] = [
  { id: "1", title: "نتفلیکس", subtitle: "اشتراک پریمیوم با تحویل آنی", image: "/figma/prod-netflix.png", position: 1, status: "فعال" },
  { id: "2", title: "اسپاتیفای", subtitle: "موسیقی بدون محدودیت", image: "/figma/prod-spotify.png", position: 2, status: "فعال" },
  { id: "3", title: "صرافی ارز دیجیتال", subtitle: "وریفای بایننس و بای‌بیت", image: "/figma/prod-binance.png", position: 3, status: "غیرفعال" },
];

// reports per period: chart data + summary
export type ReportPeriod = {
  chart: { label: string; value: number }[];
  total: string;
  orders: string;
  avg: string;
};

export const reports: Record<"day" | "week" | "month" | "year", ReportPeriod> = {
  day: {
    chart: [
      { label: "۶", value: 18 }, { label: "۹", value: 34 }, { label: "۱۲", value: 52 },
      { label: "۱۵", value: 71 }, { label: "۱۸", value: 88 }, { label: "۲۱", value: 64 }, { label: "۲۴", value: 41 },
    ],
    total: "۱۲,۴۵۰,۰۰۰ ت", orders: "۳۴۸", avg: "۳۵,۸۰۰ ت",
  },
  week: {
    chart: [
      { label: "شنبه", value: 58 }, { label: "یکشنبه", value: 72 }, { label: "دوشنبه", value: 65 },
      { label: "سه‌شنبه", value: 80 }, { label: "چهارشنبه", value: 91 }, { label: "پنجشنبه", value: 100 }, { label: "جمعه", value: 76 },
    ],
    total: "۸۴,۲۰۰,۰۰۰ ت", orders: "۲,۱۴۰", avg: "۳۹,۳۰۰ ت",
  },
  month: {
    chart: [
      { label: "هفته ۱", value: 64 }, { label: "هفته ۲", value: 78 }, { label: "هفته ۳", value: 92 }, { label: "هفته ۴", value: 85 },
    ],
    total: "۲۸۴,۰۰۰,۰۰۰ ت", orders: "۷,۸۹۰", avg: "۳۶,۰۰۰ ت",
  },
  year: {
    chart: [
      { label: "فرو", value: 42 }, { label: "ارد", value: 55 }, { label: "خرد", value: 38 }, { label: "تیر", value: 68 },
      { label: "مرد", value: 74 }, { label: "شهر", value: 61 }, { label: "مهر", value: 88 }, { label: "آبا", value: 79 },
      { label: "آذر", value: 96 }, { label: "دی", value: 84 }, { label: "بهم", value: 100 }, { label: "اسف", value: 92 },
    ],
    total: "۳.۲ میلیارد ت", orders: "۸۴,۳۰۰", avg: "۳۸,۰۰۰ ت",
  },
};

export type Kpi = {
  label: string;
  value: string;
  delta: string;
  up: boolean;
  icon: string;
  accent: string;
};

export const kpis: Kpi[] = [
  { label: "فروش امروز", value: "۱۲,۴۵۰,۰۰۰ ت", delta: "۸.۲٪", up: true, icon: "wallet", accent: "#22c55e" },
  { label: "سفارشات", value: "۳۴۸", delta: "۴.۱٪", up: true, icon: "cart", accent: "#3a64f2" },
  { label: "کاربران جدید", value: "۱۲۶", delta: "۲.۳٪", up: false, icon: "users", accent: "#e60053" },
  { label: "درآمد ماه", value: "۲۸۴,۰۰۰,۰۰۰ ت", delta: "۱۲.۹٪", up: true, icon: "chart", accent: "#a855f7" },
];

// monthly sales for the chart (relative values)
export const salesData = [
  { label: "فروردین", value: 42 },
  { label: "اردیبهشت", value: 55 },
  { label: "خرداد", value: 38 },
  { label: "تیر", value: 68 },
  { label: "مرداد", value: 74 },
  { label: "شهریور", value: 61 },
  { label: "مهر", value: 88 },
  { label: "آبان", value: 79 },
  { label: "آذر", value: 96 },
  { label: "دی", value: 84 },
  { label: "بهمن", value: 100 },
  { label: "اسفند", value: 92 },
];

export type AdminOrder = {
  id: string;
  customer: string;
  product: string;
  amount: string;
  status: "پرداخت شده" | "در انتظار" | "لغو شده";
  date: string;
};

export const adminOrders: AdminOrder[] = [
  { id: "PX-100245", customer: "علی محمدی", product: "اشتراک نتفلیکس ۱ ماهه", amount: "۲۹۰,۰۰۰ ت", status: "پرداخت شده", date: "۱۴۰۳/۰۳/۲۲" },
  { id: "PX-100244", customer: "زهرا کریمی", product: "اسپاتیفای پریمیوم", amount: "۱۸۵,۰۰۰ ت", status: "در انتظار", date: "۱۴۰۳/۰۳/۲۲" },
  { id: "PX-100243", customer: "محمد رضایی", product: "کانوا پرو سالانه", amount: "۲۱۰,۰۰۰ ت", status: "پرداخت شده", date: "۱۴۰۳/۰۳/۲۱" },
  { id: "PX-100242", customer: "سارا احمدی", product: "اپل موزیک ۳ ماهه", amount: "۴۹۵,۰۰۰ ت", status: "لغو شده", date: "۱۴۰۳/۰۳/۲۱" },
  { id: "PX-100241", customer: "رضا نوری", product: "بایننس وریفای", amount: "۸۵۰,۰۰۰ ت", status: "پرداخت شده", date: "۱۴۰۳/۰۳/۲۰" },
  { id: "PX-100240", customer: "نگار شریفی", product: "فری‌لنسر اکانت", amount: "۳۲۰,۰۰۰ ت", status: "در انتظار", date: "۱۴۰۳/۰۳/۲۰" },
];

export type AdminProduct = {
  id: string;
  name: string;
  category: string;
  price: string;
  stock: number;
  status: "فعال" | "ناموجود";
  image: string;
};

export const adminProducts: AdminProduct[] = [
  { id: "1", name: "اشتراک نتفلیکس", category: "فیلم و سریال", price: "۲۹۰,۰۰۰ ت", stock: 142, status: "فعال", image: "/figma/prod-netflix.png" },
  { id: "2", name: "اسپاتیفای پریمیوم", category: "موسیقی", price: "۱۸۵,۰۰۰ ت", stock: 88, status: "فعال", image: "/figma/prod-spotify.png" },
  { id: "3", name: "کانوا پرو", category: "گرافیک و طراحی", price: "۲۱۰,۰۰۰ ت", stock: 53, status: "فعال", image: "/figma/prod-canva.png" },
  { id: "4", name: "بایننس وریفای", category: "صرافی ارز دیجیتال", price: "۸۵۰,۰۰۰ ت", stock: 0, status: "ناموجود", image: "/figma/prod-binance.png" },
  { id: "5", name: "اپل موزیک", category: "موسیقی", price: "۱۶۵,۰۰۰ ت", stock: 67, status: "فعال", image: "/figma/prod-applemusic.png" },
  { id: "6", name: "فری‌لنسر اکانت", category: "کارت اعتباری", price: "۳۲۰,۰۰۰ ت", stock: 24, status: "فعال", image: "/figma/prod-freelancer.png" },
];

export type UserRole = "مدیر" | "پشتیبانی" | "کاربر";
export type AdminUser = {
  id: string;
  name: string;
  email: string;
  phone: string;
  role: UserRole;
  orders: number;
  spent: string;
  wallet: string;
  verified: boolean;
  status: "فعال" | "مسدود";
  joined: string;
};

export const adminUsers: AdminUser[] = [
  { id: "U-1024", name: "علی محمدی", email: "ali@example.com", phone: "۰۹۱۲۱۱۱۲۲۳۳", role: "کاربر", orders: 12, spent: "۳,۲۰۰,۰۰۰ ت", wallet: "۱۸۰,۰۰۰ ت", verified: true, status: "فعال", joined: "۱۴۰۳/۰۱/۱۵" },
  { id: "U-1023", name: "زهرا کریمی", email: "zahra@example.com", phone: "۰۹۱۲۳۳۳۴۴۵۵", role: "کاربر", orders: 8, spent: "۱,۸۵۰,۰۰۰ ت", wallet: "۵۴,۰۰۰ ت", verified: true, status: "فعال", joined: "۱۴۰۳/۰۲/۰۳" },
  { id: "U-1022", name: "محمد رضایی", email: "mohammad@example.com", phone: "۰۹۳۵۱۲۳۴۵۶۷", role: "پشتیبانی", orders: 5, spent: "۹۸۰,۰۰۰ ت", wallet: "۰ ت", verified: true, status: "فعال", joined: "۱۴۰۳/۰۲/۱۱" },
  { id: "U-1021", name: "سارا احمدی", email: "sara@example.com", phone: "۰۹۹۰۸۷۶۵۴۳۲", role: "کاربر", orders: 2, spent: "۴۵۰,۰۰۰ ت", wallet: "۱۲,۰۰۰ ت", verified: false, status: "مسدود", joined: "۱۴۰۳/۰۲/۲۸" },
  { id: "U-1020", name: "رضا نوری", email: "reza@example.com", phone: "۰۹۱۰۵۵۵۶۶۷۷", role: "مدیر", orders: 19, spent: "۵,۶۴۰,۰۰۰ ت", wallet: "۹۲۰,۰۰۰ ت", verified: true, status: "فعال", joined: "۱۴۰۲/۱۲/۰۵" },
  { id: "U-1019", name: "نگار شریفی", email: "negar@example.com", phone: "۰۹۳۸۴۴۴۳۳۲۲", role: "کاربر", orders: 0, spent: "۰ ت", wallet: "۰ ت", verified: false, status: "فعال", joined: "۱۴۰۳/۰۳/۲۰" },
];

export type Category = { id: string; name: string; slug: string; products: number; status: "فعال" | "غیرفعال"; icon: string };

export const adminCategories: Category[] = [
  { id: "1", name: "فیلم و سریال", slug: "films", products: 24, status: "فعال", icon: "/figma/cat-film.png" },
  { id: "2", name: "موسیقی", slug: "music", products: 12, status: "فعال", icon: "/figma/cat-music.png" },
  { id: "3", name: "گرافیک و طراحی", slug: "graphic", products: 9, status: "فعال", icon: "/figma/cat-graphic.png" },
  { id: "4", name: "کارت اعتباری", slug: "credit", products: 7, status: "فعال", icon: "/figma/e67d98d153b9caf9a7453da98a1c85ae776bd4bb.png" },
  { id: "5", name: "شبکه‌های اجتماعی", slug: "social", products: 15, status: "فعال", icon: "/figma/cat-social.png" },
  { id: "6", name: "بازی و سرگرمی", slug: "games", products: 6, status: "غیرفعال", icon: "/figma/cat-games.png" },
  { id: "7", name: "صرافی ارز دیجیتال", slug: "exchange", products: 4, status: "فعال", icon: "/figma/cat-exchange.png" },
];

// global pricing controls
export type PricePlan = { id: string; label: string; price: string; discount: string };
export const subscriptionPlans: PricePlan[] = [
  { id: "1", label: "۱ ماهه", price: "۲۹۰,۰۰۰", discount: "۰" },
  { id: "2", label: "۳ ماهه", price: "۷۹۰,۰۰۰", discount: "۱۰" },
  { id: "3", label: "۶ ماهه", price: "۱,۵۰۰,۰۰۰", discount: "۱۵" },
  { id: "4", label: "۱۲ ماهه", price: "۲,۷۰۰,۰۰۰", discount: "۲۵" },
];

export type FeeRow = { id: string; label: string; value: string; unit: string; hint: string };
export const pricingSettings: FeeRow[] = [
  { id: "commission", label: "پورسانت معرف", value: "۱۰", unit: "٪", hint: "درصد پورسانت از خرید زیرمجموعه" },
  { id: "tax", label: "مالیات بر ارزش افزوده", value: "۹", unit: "٪", hint: "روی قیمت نهایی اعمال می‌شود" },
  { id: "min-charge", label: "حداقل شارژ کیف پول", value: "۵۰,۰۰۰", unit: "ت", hint: "کمترین مبلغ مجاز برای شارژ" },
  { id: "min-withdraw", label: "حداقل برداشت", value: "۱۰۰,۰۰۰", unit: "ت", hint: "کمترین مبلغ مجاز برای برداشت" },
  { id: "gateway-fee", label: "کارمزد درگاه", value: "۱.۵", unit: "٪", hint: "کارمزد درگاه پرداخت" },
];

export type AdminTicket = {
  id: string;
  user: string;
  subject: string;
  department: "فنی" | "مالی" | "فروش";
  status: "باز" | "پاسخ داده شده" | "بسته شده";
  date: string;
};

export const adminTickets: AdminTicket[] = [
  { id: "T-5821", user: "علی محمدی", subject: "مشکل در فعال‌سازی اکانت نتفلیکس", department: "فنی", status: "باز", date: "۱۴۰۳/۰۳/۲۲" },
  { id: "T-5820", user: "زهرا کریمی", subject: "درخواست بازگشت وجه", department: "مالی", status: "پاسخ داده شده", date: "۱۴۰۳/۰۳/۲۱" },
  { id: "T-5819", user: "رضا نوری", subject: "سوال درباره تمدید اشتراک", department: "فروش", status: "باز", date: "۱۴۰۳/۰۳/۲۱" },
  { id: "T-5818", user: "سارا احمدی", subject: "اکانت کار نمی‌کند", department: "فنی", status: "بسته شده", date: "۱۴۰۳/۰۳/۱۹" },
];

export type AdminTx = {
  id: string;
  user: string;
  type: "شارژ کیف پول" | "خرید" | "پورسانت" | "برداشت";
  amount: string;
  positive: boolean;
  date: string;
};

export const adminTx: AdminTx[] = [
  { id: "TX-9912", user: "علی محمدی", type: "شارژ کیف پول", amount: "+۵۰۰,۰۰۰ ت", positive: true, date: "۱۴۰۳/۰۳/۲۲" },
  { id: "TX-9911", user: "زهرا کریمی", type: "خرید", amount: "−۱۸۵,۰۰۰ ت", positive: false, date: "۱۴۰۳/۰۳/۲۲" },
  { id: "TX-9910", user: "رضا نوری", type: "پورسانت", amount: "+۸۵,۰۰۰ ت", positive: true, date: "۱۴۰۳/۰۳/۲۱" },
  { id: "TX-9909", user: "محمد رضایی", type: "برداشت", amount: "−۳۰۰,۰۰۰ ت", positive: false, date: "۱۴۰۳/۰۳/۲۰" },
];
