// The checkout page itself is a client component, so its metadata has to live here.
export const metadata = {
  title: "تکمیل خرید",
  description: "تکمیل سفارش و پرداخت امن در فونیکس وریفای.",
  robots: { index: false, follow: false },
};

export default function CheckoutLayout({ children }: { children: React.ReactNode }) {
  return children;
}
