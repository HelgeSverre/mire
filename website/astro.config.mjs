import { defineConfig } from "astro/config";
import mdx from "@astrojs/mdx";
import { themes } from "./src/shiki-themes.ts";

// Astro bundles every Shiki grammar (incl. F#), so no `langs` list is needed.
// Themes are shared with the <Code> component via src/shiki-themes.ts.
export default defineConfig({
  site: "https://mire.helge.dev",
  integrations: [mdx()],
  markdown: {
    shikiConfig: {
      themes,
      wrap: false,
    },
  },
});
