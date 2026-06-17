import { getSiteContent } from "@/lib/content";
import NavbarClient from "./NavbarClient";

export default async function Navbar() {
  const content = await getSiteContent();
  return <NavbarClient brand={content.brand} header={content.header} />;
}
