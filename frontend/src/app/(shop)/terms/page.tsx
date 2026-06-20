import { api } from "@/lib/api";

export const dynamic = "force-dynamic";
export const metadata = { title: "قوانین و مقررات | Phoenix Verify" };

export default async function TermsPage() {
  let terms = "";
  try {
    terms = (await api.advancedSettings.get()).terms;
  } catch {
    // keep empty
  }

  return (
    <div className="mx-auto max-w-[820px] px-5 pb-20 pt-10">
      <h1 className="mb-6 text-2xl font-bold text-white">قوانین و مقررات</h1>
      <div className="rounded-2xl border border-white/8 bg-[#15151f]/80 p-8">
        {terms.trim() ? (
          <p className="whitespace-pre-wrap text-sm leading-8 text-white/80">{terms}</p>
        ) : (
          <p className="text-sm text-white/50">قوانینی ثبت نشده است.</p>
        )}
      </div>
    </div>
  );
}
