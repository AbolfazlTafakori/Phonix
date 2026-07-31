import AuthTabs from "@/components/auth/AuthTabs";

export const metadata = {
  title: "ثبت‌نام",
  description: "ساخت حساب کاربری در فونیکس وریفای.",
  robots: { index: false, follow: true },
};

export default function SignupPage() {
  return <AuthTabs initial="register" />;
}
