// The cart page itself is a client component, so its metadata has to live here.
export const metadata = {
  title: "سبد خرید",
  description: "سبد خرید شما در فونیکس وریفای — بررسی اقلام، تعداد و مبلغ نهایی پیش از پرداخت.",
  robots: { index: false, follow: true },
};

export default function CartLayout({ children }: { children: React.ReactNode }) {
  return children;
}
