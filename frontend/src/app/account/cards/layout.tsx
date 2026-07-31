// This page is a client component, so its metadata has to live here.
export const metadata = {
  title: "کارت‌های من",
  robots: { index: false, follow: false },
};

export default function CardsLayout({ children }: { children: React.ReactNode }) {
  return children;
}
