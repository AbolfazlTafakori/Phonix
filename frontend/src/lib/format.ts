const faDigits = "۰۱۲۳۴۵۶۷۸۹";

export function toFa(input: string | number): string {
  return String(input).replace(/\d/g, (d) => faDigits[Number(d)]);
}

export function toEn(input: string): string {
  return input.replace(/[۰-۹]/g, (d) => String(faDigits.indexOf(d)));
}

export function formatNumber(n: number): string {
  return toFa(Math.round(n).toLocaleString("en-US"));
}

export function formatToman(n: number, unit = "تومان"): string {
  return `${formatNumber(n)} ${unit}`;
}

// The price a product card should advertise: the cheapest active plan's final price. Products
// usually keep their base price at 0 and price everything per plan, so falling back to
// product.finalPrice is only correct when there are no active plans.
export function productDisplayPrice(p: {
  finalPrice: number;
  plans?: { isActive: boolean; finalPrice: number }[];
}): number {
  const active = (p.plans ?? []).filter((pl) => pl.isActive).map((pl) => pl.finalPrice);
  return active.length ? Math.min(...active) : p.finalPrice;
}

export function parseNumber(input: string): number {
  const digits = toEn(input).replace(/[^\d]/g, "");
  return digits ? parseInt(digits, 10) : 0;
}
