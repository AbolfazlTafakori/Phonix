import type { Metadata } from "next";
import WireGuardView from "./WireGuardView";

// A private link handed to whoever the service is for — never something a search engine should hold.
export const metadata: Metadata = {
  title: "مشخصات سرویس",
  robots: { index: false, follow: false },
};

export default async function WireGuardPage({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  return (
    <div className="home-light min-h-screen" style={{ background: "var(--ac-page-bg)" }}>
      <WireGuardView token={token} />
    </div>
  );
}
