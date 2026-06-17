"use client";

import { useSiteContent } from "@/components/admin/useSiteContent";
import { Card, PageHeader, Spinner, Toggle, Field, inputCls } from "@/components/admin/ui";
import ImageField from "@/components/admin/ImageField";
import AdminIcon from "@/components/admin/AdminIcon";

export default function AdminHeaderPage() {
  const { content, setContent, loading, error, saving, saved, save } = useSiteContent();

  const setBrand = <K extends keyof NonNullable<typeof content>["brand"]>(key: K, value: string) =>
    setContent((c) => (c ? { ...c, brand: { ...c.brand, [key]: value } } : c));

  const setHeader = (key: "searchPlaceholder" | "cartLabel" | "cartLink" | "accountLabel" | "accountLink", value: string) =>
    setContent((c) => (c ? { ...c, header: { ...c.header, [key]: value } } : c));

  const setNav = (i: number, key: "label" | "href" | "hasMenu", value: string | boolean) =>
    setContent((c) =>
      c ? { ...c, header: { ...c.header, navLinks: c.header.navLinks.map((l, idx) => (idx === i ? { ...l, [key]: value } : l)) } } : c,
    );

  const addNav = () =>
    setContent((c) => (c ? { ...c, header: { ...c.header, navLinks: [...c.header.navLinks, { label: "آیتم جدید", href: "#", hasMenu: false }] } } : c));

  const removeNav = (i: number) =>
    setContent((c) => (c ? { ...c, header: { ...c.header, navLinks: c.header.navLinks.filter((_, idx) => idx !== i) } } : c));

  return (
    <div>
      <PageHeader
        title="هدر و منو"
        desc="لوگو، نام برند، لینک‌های منو و دکمه‌های هدر"
        action={
          content && (
            <div className="flex items-center gap-3">
              {saved && <span className="text-sm font-medium text-emerald-400">✓ ذخیره شد</span>}
              <button
                onClick={save}
                disabled={saving}
                className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110"
              >
                {saving ? <Spinner /> : "ذخیره تغییرات"}
              </button>
            </div>
          )
        }
      />

      {loading ? (
        <div className="grid place-items-center py-24">
          <Spinner className="h-8 w-8" />
        </div>
      ) : error || !content ? (
        <Card className="p-8 text-center text-rose-400">{error || "محتوا یافت نشد"}</Card>
      ) : (
        <div className="grid gap-6 lg:grid-cols-3">
          <Card className="p-6">
            <h3 className="mb-5 text-lg font-bold text-white">برند و لوگو</h3>
            <div className="flex items-center gap-3 rounded-xl bg-white/[0.03] p-4">
              <img src={content.brand.logo} alt="logo" className="h-14 w-auto" />
              <span className="font-bigshot text-base leading-tight text-white">
                {content.brand.logoLine1}
                <br />
                {content.brand.logoLine2}
              </span>
            </div>
            <div className="mt-5 grid gap-4">
              <Field label="نام سایت">
                <input value={content.brand.siteName} onChange={(e) => setBrand("siteName", e.target.value)} className={inputCls} />
              </Field>
              <ImageField label="لوگو" aspect="logo" value={content.brand.logo} onChange={(v) => setBrand("logo", v)} />
              <div className="grid grid-cols-2 gap-4">
                <Field label="خط اول لوگو">
                  <input value={content.brand.logoLine1} onChange={(e) => setBrand("logoLine1", e.target.value)} className={inputCls} />
                </Field>
                <Field label="خط دوم لوگو">
                  <input value={content.brand.logoLine2} onChange={(e) => setBrand("logoLine2", e.target.value)} className={inputCls} />
                </Field>
              </div>
            </div>
          </Card>

          <Card className="p-6">
            <h3 className="mb-5 text-lg font-bold text-white">دکمه‌ها و جستجو</h3>
            <div className="grid gap-4">
              <Field label="متن جستجو (placeholder)">
                <input value={content.header.searchPlaceholder} onChange={(e) => setHeader("searchPlaceholder", e.target.value)} className={inputCls} />
              </Field>
              <div className="grid grid-cols-2 gap-4">
                <Field label="متن دکمه سبد خرید">
                  <input value={content.header.cartLabel} onChange={(e) => setHeader("cartLabel", e.target.value)} className={inputCls} />
                </Field>
                <Field label="لینک سبد خرید">
                  <input value={content.header.cartLink} onChange={(e) => setHeader("cartLink", e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
                </Field>
                <Field label="متن دکمه حساب">
                  <input value={content.header.accountLabel} onChange={(e) => setHeader("accountLabel", e.target.value)} className={inputCls} />
                </Field>
                <Field label="لینک حساب">
                  <input value={content.header.accountLink} onChange={(e) => setHeader("accountLink", e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
                </Field>
              </div>
            </div>
          </Card>

          <Card className="p-6">
            <div className="mb-4 flex items-center justify-between">
              <h3 className="text-lg font-bold text-white">لینک‌های منو</h3>
              <button onClick={addNav} className="flex items-center gap-1.5 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/80 transition hover:bg-white/5">
                <AdminIcon name="plus" className="h-3.5 w-3.5" /> افزودن
              </button>
            </div>
            <div className="space-y-3">
              {content.header.navLinks.map((l, i) => (
                <div key={i} className="rounded-xl bg-white/[0.03] p-3">
                  <div className="flex items-center gap-2">
                    <input value={l.label} onChange={(e) => setNav(i, "label", e.target.value)} className="h-10 flex-1 rounded-lg border border-white/10 bg-[#0d0d15] px-3 text-sm text-white outline-none focus:border-[#3a64f2]" placeholder="عنوان" />
                    <button onClick={() => removeNav(i)} className="grid h-10 w-9 shrink-0 place-items-center rounded-lg border border-white/10 text-white/50 transition hover:border-rose-500/50 hover:text-rose-400">
                      <AdminIcon name="trash" className="h-4 w-4" />
                    </button>
                  </div>
                  <input value={l.href} onChange={(e) => setNav(i, "href", e.target.value)} dir="ltr" className="mt-2 h-10 w-full rounded-lg border border-white/10 bg-[#0d0d15] px-3 text-left text-sm text-white/70 outline-none focus:border-[#3a64f2]" placeholder="/link" />
                  <label className="mt-2 flex cursor-pointer items-center justify-between px-1">
                    <span className="text-xs text-white/60">نمایش فلش منو</span>
                    <Toggle checked={!!l.hasMenu} onChange={(v) => setNav(i, "hasMenu", v)} />
                  </label>
                </div>
              ))}
            </div>
          </Card>
        </div>
      )}
    </div>
  );
}
