import { getSiteContent } from "@/lib/content";
import TopBar from "./TopBar";
import HomeHeader from "./HomeHeader";

// The home-page header (top promo bar + main header), reusable on any page so the whole site
// follows the home design. Wrapped in `.home-light` so its theme tokens resolve everywhere.
export default async function SiteHeader() {
  const content = await getSiteContent();
  return (
    // `contents` makes this wrapper generate no box, so the sticky <header> inside HomeHeader is bounded by
    // the page-tall layout column (not this short banner+header group) and actually pins while scrolling.
    // The .home-light custom properties still cascade to the children via inheritance.
    <div className="home-light contents">
      <TopBar />
      <HomeHeader brand={content.brand} searchPlaceholder="جستجو در بین هزاران محصول..." />
    </div>
  );
}
