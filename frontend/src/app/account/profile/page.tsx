import AccountDashboard from "@/components/account/AccountDashboard";

// Dedicated route for the "پروفایل من" menu entry. `/account` itself doubles as the mobile hub
// (profile card + menu) and hides its dashboard content on small screens (see AccountShell), so
// without this route tapping "My Profile" on mobile just re-showed the same hub with nothing new —
// every other menu entry opens its own full-screen page, this one didn't. Renders the identical
// dashboard content; on desktop it looks the same as visiting /account directly.
export default function AccountProfilePage() {
  return <AccountDashboard />;
}
