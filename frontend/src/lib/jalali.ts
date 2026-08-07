// Blog post dates are authored as free-form Jalali labels ("۱۴ مرداد ۱۴۰۵") because that is what
// readers see on the page. Search engines need a Gregorian timestamp, so parse the label back into
// a real Date for <lastmod> and schema.org dates.

const MONTHS = [
  "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
  "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
];

// Persian and Arabic-Indic digits both show up in admin-entered text.
function toLatinDigits(s: string): string {
  return s.replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
          .replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660));
}

function jalaliToGregorian(jy: number, jm: number, jd: number): [number, number, number] {
  jy += 1595;
  let days = -355668 + 365 * jy + Math.floor(jy / 33) * 8 + Math.floor(((jy % 33) + 3) / 4)
    + jd + (jm < 7 ? (jm - 1) * 31 : (jm - 7) * 30 + 186);
  let gy = 400 * Math.floor(days / 146097);
  days %= 146097;
  if (days > 36524) {
    days--;
    gy += 100 * Math.floor(days / 36524);
    days %= 36524;
    if (days >= 365) days++;
  }
  gy += 4 * Math.floor(days / 1461);
  days %= 1461;
  if (days > 365) {
    gy += Math.floor((days - 1) / 365);
    days = (days - 1) % 365;
  }
  let gd = days + 1;
  const leap = (gy % 4 === 0 && gy % 100 !== 0) || gy % 400 === 0;
  const monthLengths = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  let gm = 0;
  while (gm < 12 && gd > monthLengths[gm]) {
    gd -= monthLengths[gm];
    gm++;
  }
  return [gy, gm + 1, gd];
}

// Accepts an ISO prefix ("2026-08-05…") or a Jalali label ("۱۴ مرداد ۱۴۰۵"). Returns null when the
// text is neither, so callers can omit the field rather than emit a wrong date.
export function parsePostDate(raw: string): Date | null {
  if (!raw) return null;

  const iso = raw.match(/^(\d{4})-(\d{2})-(\d{2})/);
  if (iso) {
    const d = new Date(Date.UTC(+iso[1], +iso[2] - 1, +iso[3]));
    return Number.isNaN(d.getTime()) ? null : d;
  }

  const text = toLatinDigits(raw).trim();
  const match = text.match(/(\d{1,2})\s+(\S+)\s+(\d{4})/);
  if (!match) return null;

  const monthIndex = MONTHS.indexOf(match[2]);
  if (monthIndex === -1) return null;

  const jd = +match[1];
  const jy = +match[3];
  if (jd < 1 || jd > 31 || jy < 1000 || jy > 1600) return null;

  const [gy, gm, gd] = jalaliToGregorian(jy, monthIndex + 1, jd);
  const date = new Date(Date.UTC(gy, gm - 1, gd));
  return Number.isNaN(date.getTime()) ? null : date;
}
