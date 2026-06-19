// Generate the social/OG image (1200×630) from the brand: a `└` corner mark in
// emerald, the wordmark, and the tagline on the dark surface. Rasterized with sharp
// (committed as public/og.png; not part of the per-build pipeline). Run from website/:
//   node scripts/gen-og.mjs
import sharp from "sharp";

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630">
  <rect width="1200" height="630" fill="#0d0d0d"/>
  <rect x="0.5" y="0.5" width="1199" height="629" fill="none" stroke="#292929"/>
  <!-- the └ mark -->
  <path d="M150 150 V330 H330" fill="none" stroke="#1a9a7e" stroke-width="18" stroke-linecap="square" stroke-linejoin="miter"/>
  <text x="372" y="322" font-family="ui-monospace, 'SF Mono', Menlo, monospace" font-weight="700" font-size="150" fill="#fafafa">mire</text>
  <text x="156" y="452" font-family="ui-monospace, 'SF Mono', Menlo, monospace" font-weight="700" font-size="56" fill="#fafafa">Elmish for the terminal.</text>
  <text x="156" y="520" font-family="ui-monospace, 'SF Mono', Menlo, monospace" font-size="34" fill="#868686">A retained-mode F# TUI runtime · .NET 10</text>
</svg>`;

await sharp(Buffer.from(svg)).png().toFile("public/og.png");
console.log("wrote public/og.png");
