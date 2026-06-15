import Link from "next/link";
import AuthCard from "@/components/auth/AuthCard";
import AuthField from "@/components/auth/AuthField";
import AuthButton from "@/components/auth/AuthButton";

export const metadata = { title: "ورود به حساب کاربری | Phoenix Verify" };

export default function LoginPage() {
  return (
    <AuthCard title="ورود به حساب کاربری">
      <form>
        <AuthField label="شماره موبایل یا ایمیل خود را وارد کنید" placeholder="" />

        <AuthField
          label="گذرواژه"
          type="password"
          aside={
            <Link href="/forgot-password" className="text-xs text-white/55 transition hover:text-white">
              کلمه عبور خود را فراموش کرده‌اید؟
            </Link>
          }
        />

        <p className="mb-6 mt-1 text-sm text-white/70">
          اگر حساب کاربری ندارید روی{" "}
          <Link href="/signup" className="font-bold text-[#e60053] hover:underline">
            ثبت نام
          </Link>{" "}
          کلیک کنید.
        </p>

        <AuthButton>ورود</AuthButton>
      </form>
    </AuthCard>
  );
}
