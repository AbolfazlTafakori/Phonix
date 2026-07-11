import { api } from "@/lib/api";

export const dynamic = "force-dynamic";
export const metadata = { title: "قوانین و مقررات" };

export default async function TermsPage() {
  let terms = "";
  try {
    terms = (await api.advancedSettings.get()).terms;
  } catch {
    // keep empty
  }

  return (
    <div className="mx-auto max-w-[820px] px-5 pb-20 pt-10">
      <h1 className="mb-6 text-2xl font-bold text-[var(--hl-ink)]">قوانین و مقررات</h1>
      <div className="hl-card rounded-2xl p-8">
        {terms.trim() ? (
          <p className="whitespace-pre-wrap text-sm leading-8 text-[var(--hl-ink-2)]">{terms}</p>
        ) : (
          <p className="text-sm text-[var(--hl-muted)]">قوانینی ثبت نشده است.</p>
        )}
      </div>
    </div>
  );
}
