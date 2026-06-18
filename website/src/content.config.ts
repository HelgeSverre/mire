import { defineCollection } from "astro:content";
import { z } from "astro:schema";
import { glob } from "astro/loaders";

// Diátaxis quadrants. The sidebar renders them in this order.
const docs = defineCollection({
  loader: glob({ pattern: "**/*.{md,mdx}", base: "./src/content/docs" }),
  schema: z.object({
    title: z.string(),
    description: z.string(),
    category: z.enum(["tutorials", "how-to", "reference", "explanation"]),
    order: z.number().default(99),
  }),
});

export const collections = { docs };
