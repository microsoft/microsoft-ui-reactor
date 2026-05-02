// Mirror of tests/stress_perf/StressPerf.Shared/ListItemSource.cs.
// Keep pools and formulas identical so RN and Reactor render identical content.

export const NAMES = [
  'Alex Carter', 'Bailey Nguyen', 'Casey Wu', 'Devon Patel',
  'Erin Sato', 'Finley Brooks', 'Gray Romero', 'Harper Lin',
  'Indra Khan', 'Jules Vega', 'Kai Holm', 'Lane Park',
  'Morgan Diaz', 'Nico Tran', 'Owen Reyes', 'Parker Yates',
];

export const CATEGORIES = [
  'Engineering', 'Design', 'Marketing', 'Sales',
  'Support', 'Operations', 'Research', 'Finance',
];

export const ADJECTIVES = [
  'quick', 'lazy', 'eager', 'calm', 'bright', 'rough',
  'smooth', 'sharp', 'dim', 'bold', 'shy', 'warm',
];

export const NOUNS = [
  'report', 'thread', 'ticket', 'review', 'draft', 'sync',
  'build', 'deploy', 'spike', 'demo', 'pitch', 'audit',
];

export const TAGS = [
  'ux', 'perf', 'infra', 'client', 'api', 'css',
  'shipit', 'frontend', 'backend', 'release', 'hotfix', 'wip',
];

export const ROW_HEIGHT = 76;
export const AVATAR_SIZE = 48;

// 2026-01-01T09:00:00Z. Matches BaseDate in C#.
const BASE_DATE_MS = Date.UTC(2026, 0, 1, 9, 0, 0);

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

export type ListItem = {
  id: number;
  name: string;
  category: string;
  message: string;
  timestamp: string;
  tag: string;
  likes: number;
  avatarHue: number;   // 0..359
  initial: string;     // single uppercase letter
};

function mod(x: number, n: number): number {
  const r = x % n;
  return r < 0 ? r + n : r;
}

function pad2(n: number): string {
  return n < 10 ? '0' + n : '' + n;
}

function formatTimestamp(ms: number): string {
  // C# uses ToString("MMM dd HH:mm") which formats in local time.
  // Use UTC to keep RN deterministic across machine TZ.
  const d = new Date(ms);
  return `${MONTHS[d.getUTCMonth()]} ${pad2(d.getUTCDate())} ${pad2(d.getUTCHours())}:${pad2(d.getUTCMinutes())}`;
}

export function itemAt(i: number): ListItem {
  const name = NAMES[mod(i, NAMES.length)];
  const category = CATEGORIES[mod(Math.floor(i / 7), CATEGORIES.length)];
  const adjective = ADJECTIVES[mod(i * 3, ADJECTIVES.length)];
  const noun = NOUNS[mod(i * 5 + 2, NOUNS.length)];
  const message = `${adjective} ${noun} #${i}`;
  const timestamp = formatTimestamp(BASE_DATE_MS + i * 60_000);
  const tag = TAGS[mod(i * 31, TAGS.length)];
  const h = (Math.imul(i, 1664525) + 1013904223) >>> 0;
  const likes = h % 999;
  const avatarHue = mod(i * 137, 360);
  const initial = name.charAt(0);
  return { id: i, name, category, message, timestamp, tag, likes, avatarHue, initial };
}

export function generate(count: number): ListItem[] {
  const out: ListItem[] = new Array(count);
  for (let i = 0; i < count; i++) out[i] = itemAt(i);
  return out;
}

// HSL → RGB (matches the C# helper). h is 0..1 here; pass hueDeg/360.
export function hslToHex(hueDeg: number, s: number, l: number): string {
  const h = (hueDeg % 360) / 360;
  const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
  const p = 2 * l - q;
  const hue2rgb = (t: number) => {
    if (t < 0) t += 1;
    if (t > 1) t -= 1;
    if (t < 1 / 6) return p + (q - p) * 6 * t;
    if (t < 0.5) return q;
    if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6;
    return p;
  };
  const r = Math.round(hue2rgb(h + 1 / 3) * 255);
  const g = Math.round(hue2rgb(h) * 255);
  const b = Math.round(hue2rgb(h - 1 / 3) * 255);
  const hex = (n: number) => n.toString(16).padStart(2, '0');
  return `#${hex(r)}${hex(g)}${hex(b)}`;
}
