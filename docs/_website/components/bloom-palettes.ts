export type BloomVariant = "blue" | "violet" | "teal" | "amber" | "rose" | "green";

export interface BloomPalette {
  /** Top-left and bottom-right of the background. */
  background: [string, string];
  /** Three petal tones, each [light tip, deep base]. */
  petals: [[string, string], [string, string], [string, string]];
  /** The light at the flower's center. */
  glow: string;
  /** A deep tone for icons drawn on a white tile. */
  ink: string;
}

export const BLOOMS: Record<BloomVariant, BloomPalette> = {
  blue: {
    background: ["#06102a", "#0b2457"],
    petals: [
      ["#9fd3ff", "#1f6fe0"],
      ["#5cb4ff", "#0045b8"],
      ["#c6b5ff", "#4a3fd1"],
    ],
    glow: "#4aa8ff",
    ink: "#0050c8",
  },
  violet: {
    background: ["#0f0a26", "#26145a"],
    petals: [
      ["#dcc2ff", "#6a35e8"],
      ["#a98bff", "#3b1fb8"],
      ["#ffb0e0", "#8a2bd0"],
    ],
    glow: "#a77bff",
    ink: "#5b2ee0",
  },
  teal: {
    background: ["#031619", "#063c42"],
    petals: [
      ["#a3f5e9", "#0a8f94"],
      ["#5fdccf", "#056a78"],
      ["#aeeaff", "#1a78b8"],
    ],
    glow: "#4fd8c8",
    ink: "#07707a",
  },
  amber: {
    background: ["#1c0d06", "#40190a"],
    petals: [
      ["#ffdaa6", "#e0662a"],
      ["#ffbb78", "#b8401c"],
      ["#ffa79a", "#c03a4a"],
    ],
    glow: "#ff9d55",
    ink: "#b8401c",
  },
  rose: {
    background: ["#1c0915", "#420f30"],
    petals: [
      ["#ffbfd8", "#d0306f"],
      ["#ff9cc2", "#a01a55"],
      ["#dcb0ff", "#7a2fc0"],
    ],
    glow: "#ff7fb0",
    ink: "#b0245f",
  },
  green: {
    background: ["#051710", "#0c3622"],
    petals: [
      ["#c4f7b4", "#2a9d4a"],
      ["#86e6aa", "#127a46"],
      ["#d2f39a", "#5aa02a"],
    ],
    glow: "#6fe39a",
    ink: "#127a46",
  },
};
