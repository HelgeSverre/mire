import { defineConfig } from "astro/config";
import mdx from "@astrojs/mdx";

// Two minimal Shiki themes built from the Mire palette: neutral grays carry the
// code, emerald is the one accent (keywords/types). No rainbow — as close to the
// brand's "one accent" rule as syntax highlighting allows. Astro bundles every
// Shiki grammar (incl. F#), so no `langs` list is needed for fsharp/bash/json/xml.
const scopes = (dark) => [
  { scope: ["comment", "punctuation.definition.comment"], settings: { foreground: "#868686", fontStyle: "italic" } },
  { scope: ["keyword", "storage", "storage.type", "keyword.control", "constant.language"], settings: { foreground: dark ? "#6bcdb2" : "#006750" } },
  { scope: ["string", "string.quoted", "constant.character", "string.regexp"], settings: { foreground: dark ? "#afafaf" : "#606060" } },
  { scope: ["constant.numeric", "constant.language.boolean"], settings: { foreground: dark ? "#d4d4d4" : "#464646" } },
  { scope: ["entity.name.function", "support.function", "meta.function"], settings: { foreground: dark ? "#fafafa" : "#121212" } },
  { scope: ["entity.name.type", "support.type", "entity.name.class", "support.class", "entity.name.namespace"], settings: { foreground: dark ? "#7dd4b8" : "#006f56" } },
  { scope: ["variable", "variable.other", "meta.parameter", "entity.name.label"], settings: { foreground: dark ? "#e6e6e6" : "#292929" } },
  { scope: ["punctuation", "meta.brace", "keyword.operator"], settings: { foreground: dark ? "#afafaf" : "#606060" } },
];

const mireDark = {
  name: "mire-dark",
  type: "dark",
  colors: { "editor.background": "#0d0d0d", "editor.foreground": "#fafafa" },
  settings: scopes(true),
};

const mireLight = {
  name: "mire-light",
  type: "light",
  colors: { "editor.background": "#f3f3f3", "editor.foreground": "#121212" },
  settings: scopes(false),
};

export default defineConfig({
  site: "https://mire.helge.dev",
  integrations: [mdx()],
  markdown: {
    shikiConfig: {
      themes: { light: mireLight, dark: mireDark },
      wrap: false,
    },
  },
});
