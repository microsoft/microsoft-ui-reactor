export type BloomVariant = "blue" | "violet" | "teal" | "amber" | "rose" | "green";

export interface BloomPalette {
  /** The dark corner, where text sits. */
  deep: string;
  /** The vivid color that fills most of the box. */
  mid: string;
  /** The bright corner the light comes from. */
  light: string;
  /** A second hue washed over the top. */
  accent: string;
  /** A darker hue along the bottom edge. */
  side: string;
  /** A deep tone for icons drawn on a white tile. */
  ink: string;
}

export const BLOOMS: Record<BloomVariant, BloomPalette> = {
  blue: { deep: "#051447", mid: "#1656e8", light: "#46b4ff", accent: "#6c4dff", side: "#0b3fb8", ink: "#0050c8" },
  violet: { deep: "#1a0b4d", mid: "#6a35f0", light: "#e07bff", accent: "#3f5bff", side: "#a02ab8", ink: "#5b2ee0" },
  teal: { deep: "#032a33", mid: "#0b8c88", light: "#5ff0d0", accent: "#1f7ae0", side: "#0a5f55", ink: "#07707a" },
  amber: { deep: "#3a0d05", mid: "#d9501a", light: "#ffc061", accent: "#e8366a", side: "#a82a18", ink: "#b8401c" },
  rose: { deep: "#3a0624", mid: "#d92c6c", light: "#ff9ac0", accent: "#9a4dff", side: "#a01f58", ink: "#b0245f" },
  green: { deep: "#042e1b", mid: "#119c5a", light: "#b8f04a", accent: "#00a3bd", side: "#0a6f42", ink: "#127a46" },
};