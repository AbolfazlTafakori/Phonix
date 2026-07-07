import { getSiteContent } from "@/lib/content";
import TopBar from "./TopBar";
import HomeHeader from "./HomeHeader";

// The home-page header (top promo bar + main header), reusable on any page so the whole site
// follows the home design. Wrapped in `.home-light` so its theme tokens resolve everywhere.
export default async function SiteHeader() {
  const content = await getSiteContent();
  return (
    <div className="home-light">
      <TopBar />
      <HomeHeader brand={content.brand} searchPlaceholder="جستجو در بین هزاران محصول..." />
    </div>
  );
}
