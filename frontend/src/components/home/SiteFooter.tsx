import { getSiteContent } from "@/lib/content";
import HomeFooter from "./HomeFooter";

// The home-page footer, reusable on any page so the whole site follows the home design.
// Wrapped in `.home-light` so its theme tokens resolve everywhere.
export default async function SiteFooter() {
  const content = await getSiteContent();
  return (
    <div className="home-light">
      <HomeFooter brand={content.brand} />
    </div>
  );
}
