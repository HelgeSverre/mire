import { getCollection, type CollectionEntry } from "astro:content";

export type Doc = CollectionEntry<"docs">;

export const CATEGORY_ORDER = ["tutorials", "how-to", "reference", "explanation"] as const;

export const CATEGORY_LABEL: Record<string, string> = {
  tutorials: "Tutorials",
  "how-to": "How-to guides",
  reference: "Reference",
  explanation: "Explanation",
};

const rank = (c: string) => {
  const i = (CATEGORY_ORDER as readonly string[]).indexOf(c);
  return i === -1 ? 99 : i;
};

const byOrder = (a: Doc, b: Doc) =>
  rank(a.data.category) - rank(b.data.category) ||
  a.data.order - b.data.order ||
  a.data.title.localeCompare(b.data.title);

/** All docs, sorted into Diátaxis order then by `order`. */
export async function allDocsSorted(): Promise<Doc[]> {
  const docs = await getCollection("docs");
  return docs.sort(byOrder);
}

/** Grouped by category, in Diátaxis order, for the sidebar. */
export async function docsByCategory(): Promise<{ category: string; label: string; items: Doc[] }[]> {
  const docs = await allDocsSorted();
  return CATEGORY_ORDER.map((category) => ({
    category,
    label: CATEGORY_LABEL[category],
    items: docs.filter((d) => d.data.category === category),
  })).filter((g) => g.items.length > 0);
}

export const docHref = (d: Doc) => `/docs/${d.id}/`;
