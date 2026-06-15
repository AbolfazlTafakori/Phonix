import AuthCard from "@/components/auth/AuthCard";
import AuthField from "@/components/auth/AuthField";
import AuthButton from "@/components/auth/AuthButton";

export const metadata = { title: "فراموشی رمز ورود | Phoenix Verify" };

export default function ForgotPasswordPage() {
  return (
    <AuthCard title="فراموشی رمز ورود">
      <p className="mb-7 text-center text-sm leading-7 text-white/70">
        گذرواژه خود را فراموش کرده‌اید؟ شماره تلفن یا ایمیل خود را وارد کنید، سپس روی دکمه‌ی{" "}
        <span className="font-bold text-[#e60053]">بازگردانی گذرواژه</span> کلیک کنید تا یک لینک برای
        ساختن گذرواژه‌ی جدید برایتان ارسال شود.
      </p>

      <form>
        <AuthField label="شماره موبایل یا ایمیل خود را وارد کنید" placeholder="" />
        <AuthButton>بازگردانی گذرواژه</AuthButton>
      </form>
    </AuthCard>
  );
}
