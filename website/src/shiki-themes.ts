// Two minimal Shiki themes from the Mire palette: neutral grays carry the code,
// emerald is the one accent (keywords/types). Shared by astro.config.mjs (markdown
// code blocks) and the <Code> component (per-widget signatures/examples), so all
// highlighting on the site is identical and dual-theme (light/dark).

const scopes = (dark: boolean) => [
  { scope: ["comment", "punctuation.definition.comment"], settings: { foreground: "#868686", fontStyle: "italic" } },
  { scope: ["keyword", "storage", "storage.type", "keyword.control", "constant.language"], settings: { foreground: dark ? "#6bcdb2" : "#006750" } },
  { scope: ["string", "string.quoted", "constant.character", "string.regexp"], settings: { foreground: dark ? "#afafaf" : "#606060" } },
  { scope: ["constant.numeric", "constant.language.boolean"], settings: { foreground: dark ? "#d4d4d4" : "#464646" } },
  { scope: ["entity.name.function", "support.function", "meta.function"], settings: { foreground: dark ? "#fafafa" : "#121212" } },
  { scope: ["entity.name.type", "support.type", "entity.name.class", "support.class", "entity.name.namespace"], settings: { foreground: dark ? "#7dd4b8" : "#006f56" } },
  { scope: ["variable", "variable.other", "meta.parameter", "entity.name.label"], settings: { foreground: dark ? "#e6e6e6" : "#292929" } },
  { scope: ["punctuation", "meta.brace", "keyword.operator"], settings: { foreground: dark ? "#afafaf" : "#606060" } },
];

export const mireDark = {
  name: "mire-dark",
  type: "dark" as const,
  colors: { "editor.background": "#0d0d0d", "editor.foreground": "#fafafa" },
  settings: scopes(true),
};

export const mireLight = {
  name: "mire-light",
  type: "light" as const,
  colors: { "editor.background": "#f3f3f3", "editor.foreground": "#121212" },
  settings: scopes(false),
};

export const themes = { light: mireLight, dark: mireDark };
