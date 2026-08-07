export type HomeFaqItem = { q: string; a: string };

// Kept out of HomeFaq.tsx on purpose: that file is a client module, and a plain value imported from a
// client module into a server component arrives as a client reference, not the array itself. The home
// page needs the real data to emit FAQPage JSON-LD, so both sides import it from here.
export const faqItems: HomeFaqItem[] = [
  { q: "چطور محصول یا شماره را بعد از خرید دریافت می‌کنم؟", a: "بعد از پرداخت موفق، محصول یا شماره بلافاصله در پنل کاربری یا ایمیل شما ارسال می‌شود." },
  { q: "زمان تحویل محصولات چقدر است؟", a: "بیشتر محصولات آنی تحویل داده می‌شوند و در موارد خاص در کوتاه‌ترین زمان ممکن." },
  { q: "آیا محصولات شما قانونی و اورجینال هستند؟", a: "بله، تمام محصولات از منابع معتبر و کاملاً اورجینال تهیه و ارائه می‌شوند." },
  { q: "آیا امکان بازگشت وجه وجود دارد؟", a: "در صورت بروز مشکل در تحویل، مبلغ پرداختی طبق قوانین سایت به شما بازگردانده می‌شود." },
  { q: "در صورت بروز مشکل چگونه پشتیبانی دریافت کنم؟", a: "از طریق تیکت، چت آنلاین یا کانال تلگرام به‌صورت شبانه‌روزی پاسخگوی شما هستیم." },
  { q: "آیا برای خرید نیاز به ثبت‌نام دارم؟", a: "برای پیگیری سفارش و دریافت پشتیبانی بهتر، ثبت‌نام توصیه می‌شود و بسیار سریع است." },
];
