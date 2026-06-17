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

export function parseNumber(input: string): number {
  const digits = toEn(input).replace(/[^\d]/g, "");
  return digits ? parseInt(digits, 10) : 0;
}
