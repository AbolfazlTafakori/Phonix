import Link from "next/link";
import AuthCard from "@/components/auth/AuthCard";
import AuthField from "@/components/auth/AuthField";
import AuthButton from "@/components/auth/AuthButton";

export const metadata = { title: "عضویت در سایت | Phoenix Verify" };

export default function SignupPage() {
  return (
    <AuthCard title="عضویت در سایت">
      <form>
        <AuthField label="شماره موبایل یا ایمیل خود را وارد کنید" placeholder="" />
        <AuthField label="گذرواژه" type="password" />

        <label className="mb-6 mt-1 flex items-start gap-3 text-sm leading-7 text-white/70">
          <input
            type="checkbox"
            className="mt-1 h-4 w-4 shrink-0 rounded border-white/20 bg-[#0d0d15] accent-[#e60053]"
          />
          <span>
            تایید کردن این فرم به منزله‌ی{" "}
            <span className="font-bold text-[#e60053]">تایید</span> تمامی سیاست حفظ حریم خصوصی می‌باشد.
          </span>
        </label>

        <AuthButton>عضویت</AuthButton>

        <p className="mt-6 text-center text-sm text-white/60">
          قبلاً ثبت‌نام کرده‌اید؟{" "}
          <Link href="/login" className="font-bold text-[#e60053] hover:underline">
            ورود
          </Link>
        </p>
      </form>
    </AuthCard>
  );
}
