import { getTestimonials, getSiteContent } from "@/lib/content";
import TestimonialsCoverflow from "./TestimonialsCoverflow";

export default async function Testimonials() {
  const [comments, content] = await Promise.all([getTestimonials(), getSiteContent()]);

  // Hidden when the admin has the section switched off, or when no review is flagged for the home page.
  if (!content.testimonialsEnabled || comments.length === 0) return null;

  return (
    <TestimonialsCoverflow
      comments={comments}
      autoplaySeconds={content.testimonialsAutoplaySeconds ?? 5}
      title="نظرات کاربران"
    />
  );
}
