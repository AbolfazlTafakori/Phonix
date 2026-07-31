// This page is a client component, so its metadata has to live here.
export const metadata = {
  title: "دیدگاه‌ها و پرسش‌ها",
  robots: { index: false, follow: false },
};

export default function CommentsLayout({ children }: { children: React.ReactNode }) {
  return children;
}
