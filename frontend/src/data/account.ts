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
