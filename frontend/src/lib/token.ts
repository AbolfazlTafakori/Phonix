// The session token lives in an httpOnly cookie set by the server (not readable here).
// Only the CSRF token is JS-readable, and we echo it back in a header (double-submit).
const CSRF_COOKIE = "ppx_csrf";

export function getCsrfToken(): string | null {
  if (typeof document === "undefined") return null;
  const match = document.cookie.match(new RegExp(`(?:^|;\\s*)${CSRF_COOKIE}=([^;]+)`));
  return match ? decodeURIComponent(match[1]) : null;
}
