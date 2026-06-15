import { PageTitle } from "@/components/account/Panel";
import { products } from "@/data/home";

export const metadata = { title: "محصولات موردعلاقه | Phoenix Verify" };

export default function FavoritesPage() {
  const favorites = products.slice(0, 4);
  return (
    <div>
      <PageTitle title="محصولات موردعلاقه" desc="محصولاتی که برای خرید بعدی ذخیره کرده‌اید." />

      <div className="grid grid-cols-2 gap-5 lg:grid-cols-3">
        {favorites.map((product) => (
          <div
            key={product.name}
            className="group relative overflow-hidden rounded-2xl border border-white/8 bg-[#0d0d14]"
          >
            <div className="relative aspect-[3/4]">
              <img src={product.image} alt={product.name} className="h-full w-full object-cover" />
              <div className="absolute inset-0 bg-gradient-to-t from-black/85 via-black/20 to-transparent" />
              <button
                aria-label="حذف از علاقه‌مندی"
                className="absolute left-3 top-3 grid h-9 w-9 place-items-center rounded-full bg-black/50 text-[#e60053] transition hover:bg-black/70"
              >
                ♥
              </button>
              <div className="absolute inset-x-0 bottom-0 flex flex-col items-center gap-1.5 p-4">
                {product.logo ? (
                  <img src={product.logo} alt={product.name} className="max-h-9 w-auto max-w-[70%] object-contain" />
                ) : (
                  <span className="text-xl font-extrabold text-[#1db954]">Spotify</span>
                )}
                <span className="font-unna text-[11px] tracking-wide text-white/70">Phoenix Verify</span>
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
