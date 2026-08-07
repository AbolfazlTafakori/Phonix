import type { Category } from "./types";

// Admin-entered category slugs are free text, so they arrive with stray casing and (in at least one
// case) trailing non-breaking spaces. Everything that builds or resolves a category URL goes through
// here so a link and the route that answers it can never disagree.
export function categorySlug(c: { slug: string; id: number }): string {
  const s = (c.slug ?? "")
    .replace(/[ ‌\s]+/g, "-")
    .replace(/[^a-zA-Z0-9-]/g, "")
    .replace(/-+/g, "-")
    .replace(/^-+|-+$/g, "")
    .toLowerCase();
  return s || String(c.id);
}

export function categoryPath(c: { slug: string; id: number }): string {
  return `/category/${categorySlug(c)}`;
}

// A category page has to say something a filtered product list doesn't, or it is a thin duplicate of
// /products. The admin can write that intro per category (it is the same field the category cards use);
// these are the fallbacks for the ones that have none yet, written around the term each page targets.
const introFallbacks: Record<string, { title: string; intro: string }> = {
  "فیلم و استریم": {
    title: "خرید اکانت سرویس‌های فیلم و سریال",
    intro:
      "سرویس‌های پخش آنلاین مثل نتفلیکس و یوتیوب پرمیوم پرداخت ریالی قبول نمی‌کنند و ساخت اکانت مستقیم از ایران عملاً ممکن نیست. در این دسته اکانت‌های فعال و قانونی این سرویس‌ها را با پرداخت ریالی، تحویل سریع و گارانتی دوره تهیه می‌کنید.",
  },
  "موسیقی": {
    title: "خرید اکانت سرویس‌های موسیقی",
    intro:
      "اسپاتیفای و اپل موزیک بزرگ‌ترین کتابخانه‌های موسیقی جهان را دارند، اما هیچ‌کدام از کارت بانکی ایران پشتیبانی نمی‌کنند. اشتراک‌های این دسته روی اکانت خودتان یا اکانت آماده فعال می‌شود؛ بدون مسترکارت و بدون دردسر تمدید ارزی.",
  },
  "گرافیک و طراحی": {
    title: "خرید اکانت ابزارهای گرافیک و طراحی",
    intro:
      "از کانوا پرو تا کپ‌کات و پیکس‌آرت، ابزارهای طراحی و ویرایش ویدیو نسخه‌ی پولی‌شان قفل‌های مهمی را باز می‌کند: قالب‌های پریمیوم، حذف واترمارک و خروجی با کیفیت بالا. اشتراک‌های این دسته قانونی فعال می‌شوند و در طول دوره گارانتی دارند.",
  },
  "شبکه‌های اجتماعی": {
    title: "خرید اشتراک شبکه‌های اجتماعی",
    intro:
      "اشتراک‌های پولی شبکه‌های اجتماعی مثل تلگرام پرمیوم و دیسکورد نیترو محدودیت‌های حساب رایگان را برمی‌دارند. در این دسته این اشتراک‌ها را با پرداخت ریالی و فعال‌سازی روی حساب خودتان می‌گیرید.",
  },
  "بازی و سرگرمی": {
    title: "خرید اکانت بازی و سرگرمی",
    intro:
      "اشتراک‌های گیمینگ و سرگرمی — از پرایم گیمینگ تا چس‌دات‌کام — مزایایی می‌دهند که حساب رایگان ندارد. همه‌ی محصولات این دسته از مسیر پرداخت معتبر تهیه می‌شوند تا وسط دوره مسدود نشوند.",
  },
  "آموزشی": {
    title: "خرید اکانت اپلیکیشن‌های آموزشی",
    intro:
      "دولینگو، گرامرلی، السا اسپیک و ماندلی در نسخه‌ی پولی درس‌های نامحدود، بازخورد پیشرفته و حذف تبلیغات می‌دهند. اشتراک‌های این دسته را ریالی می‌خرید و روی حساب خودتان فعال می‌کنید.",
  },
  "هوش مصنوعی": {
    title: "خرید اکانت سرویس‌های هوش مصنوعی",
    intro:
      "دسترسی به مدل‌های پیشرفته‌ی ChatGPT، Claude، Gemini و Grok برای کاربر ایرانی هم مسئله‌ی پرداخت دارد و هم مسئله‌ی تحریم. اکانت‌های این دسته روی سرویس رسمی فعال‌اند و بدون نیاز به مسترکارت یا شماره‌ی خارجی تحویل داده می‌شوند.",
  },
  "فیلترشکن": {
    title: "خرید اکانت فیلترشکن و وی‌پی‌ان",
    intro:
      "انتخاب وی‌پی‌ان درست به سه چیز برمی‌گردد: سرعت پایدار، سیاست عدم ثبت لاگ و تعداد دستگاه‌های هم‌زمان. در این دسته سرویس‌های شناخته‌شده‌ی جهانی و همچنین کانفیگ‌های V2Ray را با اشتراک قانونی و پشتیبانی فارسی می‌گیرید.",
  },
  "کارت اعتباری": {
    title: "خرید گیفت کارت و کارت اعتباری",
    intro: "گیفت کارت‌ها و کارت‌های اعتباری بین‌المللی برای پرداخت در فروشگاه‌های خارجی، با تحویل کد به‌صورت آنی.",
  },
  "صرافی ارز دیجیتال": {
    title: "خدمات صرافی ارز دیجیتال",
    intro: "خدمات مربوط به صرافی‌های ارز دیجیتال و احراز هویت حساب‌های بین‌المللی.",
  },
};

export function categoryHeading(c: Category): string {
  return introFallbacks[c.name]?.title ?? `خرید ${c.name}`;
}

export function categoryIntro(c: Category): string {
  const own = (c.description ?? "").trim();
  return own || introFallbacks[c.name]?.intro || "";
}

// The card grid on /categories reads the same copy, so a category the admin has written an intro for
// stops showing the generic «محصولات و خدمات متنوع» there too.
export function categoryCardBlurb(c: Category): string {
  const text = categoryIntro(c);
  if (!text) return "محصولات و خدمات متنوع";
  const firstSentence = text.split(/(?<=[.؟!])\s/)[0] ?? text;
  return firstSentence.length > 110 ? `${firstSentence.slice(0, 109).trimEnd()}…` : firstSentence;
}
