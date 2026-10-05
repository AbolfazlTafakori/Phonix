import type { PromoBar } from "./types";

// The promo strip's text before anyone edits it — the same defaults as the API's PromoBarContent. Kept in its
// own module so the admin editor (a client component) can use it without pulling in the server-side content
// loader.
export const defaultPromoBar: PromoBar = {
  enabled: true,
  emoji: "🎉",
  text: "جشنواره تابستانی فونیکس وریفای! تخفیف‌های ویژه تا ۳۰ درصد روی آیتم‌های محبوب خدمات",
  buttonLabel: "مشاهده تخفیف‌ها",
  buttonLink: "/products",
};
