import AuthCard from "@/components/auth/AuthCard";
import AuthField from "@/components/auth/AuthField";
import AuthButton from "@/components/auth/AuthButton";

export const metadata = { title: "تغییر گذرواژه | Phoenix Verify" };

export default function ChangePasswordPage() {
  return (
    <AuthCard title="تغییر گذرواژه">
      <p className="mb-7 text-center text-sm leading-7 text-white/70">
        گذرواژه‌ی جدید خود را وارد کرده و برای اطمینان دوباره تکرار کنید.
      </p>

      <form>
        <AuthField label="گذرواژه‌ی جدید" type="password" placeholder="" />
        <AuthField label="تکرار گذرواژه‌ی جدید" type="password" placeholder="" />
        <AuthButton>تغییر گذرواژه</AuthButton>
      </form>
    </AuthCard>
  );
}
