import type { Product } from "./types";

/** The subset of a product the catalogue grid actually renders.
 *
 *  The grid is a client component, so whatever it receives is serialised into the HTML for hydration.
 *  Handing it whole products shipped every description and FAQ — tens of thousands of words the grid
 *  never displays — to every visitor of /products and /category/*. This keeps the fields the cards and
 *  filters read, and nothing else. */
export type ProductCardData = {
  id: number;
  name: string;
  categoryId: number;
  categoryName: string;
  price: number;
  discountPercent: number;
  finalPrice: number;
  stock: number;
  featured: boolean;
  image: string;
  listImage: string;
  logo: string;
  // Only the cheapest active plan's price drives the displayed price, and only the first plan's label
  // is shown under the name — so that is all the grid needs of the plan list.
  planLabel: string;
  plans: { isActive: boolean; finalPrice: number }[];
};

export function toCardData(p: Product): ProductCardData {
  return {
    id: p.id,
    name: p.name,
    categoryId: p.categoryId,
    categoryName: p.categoryName,
    price: p.price,
    discountPercent: p.discountPercent,
    finalPrice: p.finalPrice,
    stock: p.stock,
    featured: p.featured,
    image: p.image,
    listImage: p.listImage ?? "",
    logo: p.logo,
    planLabel: p.plans?.[0]?.type ?? "",
    plans: (p.plans ?? []).map((pl) => ({ isActive: pl.isActive, finalPrice: pl.finalPrice })),
  };
}
