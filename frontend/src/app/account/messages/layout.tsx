// This page is a client component, so its metadata has to live here.
export const metadata = {
  title: "پیام‌ها",
  robots: { index: false, follow: false },
};

export default function MessagesLayout({ children }: { children: React.ReactNode }) {
  return children;
}
