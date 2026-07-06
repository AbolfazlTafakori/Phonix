const Plane = ({ className }: { className?: string }) => (
  <svg viewBox="0 0 24 24" className={className} fill="currentColor">
    <path d="M21.5 2.5L2 10.3l6.4 2.3 2.3 6.4 3-4.4 4.6 3.4L21.5 2.5zM8.9 12.1l7.7-5-6 6.1z" />
  </svg>
);

export default function HomeNewsletter() {
  return (
    <section className="mx-auto max-w-[1840px] px-16 py-10">
      <div
        className="flex items-center justify-between gap-8 rounded-[26px] px-9 py-6"
        style={{ background: "linear-gradient(95deg, #F0392C 0%, #FF7A2E 100%)" }}
      >
        {/* right: logo */}
        <div className="hidden shrink-0 items-center gap-2.5 border-l border-white/25 pl-6 text-white xl:flex">
          <span className="text-[16px] font-extrabold leading-[1.1]">Phoenix<br />Verify</span>
          <img src="/figma/logo-phoenix.png" alt="Phoenix Verify" className="h-12 w-auto" />
        </div>

        {/* right-middle: copy */}
        <div className="shrink-0 text-right text-white">
          <h2 className="text-[22px] font-black">از تخفیف‌ها و محصولات جدید باخبر شوید!</h2>
          <p className="mt-1.5 text-[13px] text-white/85">ایمیل خود را وارد کنید تا از جدیدترین تخفیف‌ها و اخبار مطلع شوید.</p>
        </div>

        {/* middle: form */}
        <form className="flex flex-1 items-center gap-3">
          <input
            dir="rtl"
            type="email"
            placeholder="ایمیل شما..."
            style={{ background: "#fff", color: "#151515" }}
            className="h-12 flex-1 rounded-xl px-4 text-[14px] font-medium placeholder:text-[#8a8f99] focus:outline-none"
          />
          <button type="submit" style={{ background: "#fff" }} className="h-12 shrink-0 rounded-xl px-6 text-[14px] font-bold text-[#F0392C] transition hover:brightness-95">
            عضویت در خبرنامه
          </button>
        </form>

        {/* left: telegram */}
        <div className="hidden shrink-0 flex-col items-center gap-2 text-white lg:flex">
          <Plane className="h-11 w-11" />
          <span className="text-[14px] font-bold">در تلگرام همراه ما باشید!</span>
        </div>
      </div>
    </section>
  );
}
