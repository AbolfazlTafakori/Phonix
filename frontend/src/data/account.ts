export type MenuItem = {
  label: string;
  href: string;
  icon: string;
};

export const accountMenu: MenuItem[] = [
  { label: "پروفایل من", href: "/account", icon: "user" },
  { label: "کیف پول", href: "/account/wallet", icon: "wallet" },
  { label: "سفارشات من", href: "/account/orders", icon: "orders" },
  { label: "محصولات موردعلاقه", href: "/account/favorites", icon: "heart" },
  { label: "گزارش درآمد معرف", href: "/account/referral", icon: "chart" },
  { label: "دعوت دوستان", href: "/account/invite", icon: "gift" },
  { label: "تیکت پشتیبانی", href: "/account/tickets", icon: "ticket" },
  { label: "احراز هویت", href: "/account/kyc", icon: "shield" },
];

export type Order = {
  id: string;
  product: string;
  amount: string;
  status: "پرداخت شده" | "در انتظار" | "لغو شده";
  date: string;
};

export const orders: Order[] = [
  { id: "PX-100245", product: "اشتراک نتفلیکس ۱ ماهه", amount: "۲۹۰,۰۰۰ تومان", status: "پرداخت شده", date: "۱۴۰۳/۰۳/۲۲" },
  { id: "PX-100231", product: "اکانت Spotify Premium", amount: "۱۸۵,۰۰۰ تومان", status: "پرداخت شده", date: "۱۴۰۳/۰۳/۱۸" },
  { id: "PX-100210", product: "اشتراک Canva Pro", amount: "۲۱۰,۰۰۰ تومان", status: "در انتظار", date: "۱۴۰۳/۰۳/۱۵" },
  { id: "PX-100198", product: "اکانت Apple Music", amount: "۱۶۵,۰۰۰ تومان", status: "لغو شده", date: "۱۴۰۳/۰۳/۱۰" },
];

export type ReferralRow = {
  user: string;
  orderId: string;
  amount: string;
  commission: string;
  date: string;
};

export const referralRows: ReferralRow[] = [
  { user: "علی محمدی", orderId: "PX-100245", amount: "۲۹۰,۰۰۰ تومان", commission: "۲۹,۰۰۰ تومان", date: "۱۴۰۳/۰۳/۲۲" },
  { user: "زهرا کریمی", orderId: "PX-100231", amount: "۱۸۵,۰۰۰ تومان", commission: "۱۸,۵۰۰ تومان", date: "۱۴۰۳/۰۳/۱۸" },
  { user: "محمد رضایی", orderId: "PX-100210", amount: "۲۱۰,۰۰۰ تومان", commission: "۲۱,۰۰۰ تومان", date: "۱۴۰۳/۰۳/۱۵" },
  { user: "سارا احمدی", orderId: "PX-100198", amount: "۱۶۵,۰۰۰ تومان", commission: "۱۶,۵۰۰ تومان", date: "۱۴۰۳/۰۳/۱۰" },
];

export type Ticket = {
  id: string;
  subject: string;
  department: string;
  status: "باز" | "پاسخ داده شده" | "بسته شده";
  date: string;
};

export const tickets: Ticket[] = [
  { id: "T-5821", subject: "مشکل در فعال‌سازی اکانت", department: "پشتیبانی فنی", status: "پاسخ داده شده", date: "۱۴۰۳/۰۳/۲۱" },
  { id: "T-5790", subject: "سوال درباره تمدید اشتراک", department: "فروش", status: "باز", date: "۱۴۰۳/۰۳/۱۹" },
  { id: "T-5743", subject: "درخواست بازگشت وجه", department: "مالی", status: "بسته شده", date: "۱۴۰۳/۰۳/۱۲" },
];
