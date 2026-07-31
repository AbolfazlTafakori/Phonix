import AuthTabs from "@/components/auth/AuthTabs";

export const metadata = {
  title: "ورود به حساب کاربری",
  description: "ورود به حساب کاربری فونیکس وریفای.",
  robots: { index: false, follow: true },
};

export default function LoginPage() {
  return <AuthTabs initial="login" />;
}
