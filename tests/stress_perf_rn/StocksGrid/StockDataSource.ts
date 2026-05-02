// Mirror of tests/stress_perf/StressPerf.Shared/StockDataSource.cs.
// Keep the constants and algorithm in sync — both apps must produce the
// identical symbols and prices for any given seed so the comparison is fair.

export type StockItem = {
  symbol: string;
  prevPrice: number;
  currentPrice: number;
  isUp: boolean;
};

export const COLUMNS = 70;
export const ROWS = 70;
export const TOTAL_ITEMS = COLUMNS * ROWS;

const A = 'A'.charCodeAt(0);

// Deterministic LCG seeded with 42 — same values C#'s `new Random(42)` would
// produce in NextDouble() are not byte-identical, but we don't actually need
// byte-identical floats: the test runs the same algorithm on the same shape
// of data, and the visual + perf characteristics are the comparison target.
class Lcg {
  private state: number;
  constructor(seed: number) {
    this.state = seed >>> 0;
  }
  // Returns a double in [0, 1) using the same Numerical Recipes constants
  // we use in C#'s ListItemSource for the likes hash.
  next(): number {
    this.state = (Math.imul(this.state, 1664525) + 1013904223) >>> 0;
    return this.state / 0x1_0000_0000;
  }
  nextInt(maxExclusive: number): number {
    return Math.floor(this.next() * maxExclusive);
  }
}

function symbolFor(index: number): string {
  const row = Math.floor(index / COLUMNS);
  const col = index % COLUMNS;
  const c1 = String.fromCharCode(A + (row % 26));
  const c2 = String.fromCharCode(A + (Math.floor(col / 3) % 26));
  const c3 = String.fromCharCode(A + (col % 26));
  return c1 + c2 + c3;
}

export class StockDataSource {
  readonly items: StockItem[];
  private readonly rng: Lcg;

  constructor(seed: number = 42) {
    this.rng = new Lcg(seed);
    this.items = new Array(TOTAL_ITEMS);
    for (let i = 0; i < TOTAL_ITEMS; i++) {
      const symbol = symbolFor(i);
      const price = Math.round((10 + this.rng.next() * 990) * 100) / 100;
      this.items[i] = { symbol, prevPrice: price, currentPrice: price, isUp: true };
    }
  }

  /**
   * Mutate `percent` % of items in place. Returns the indices that changed.
   * Mirrors StockDataSource.Update in C# (delta = (rng - 0.48) * 2 * price * 0.02).
   */
  update(percent: number): number[] {
    const count = Math.max(1, Math.floor((TOTAL_ITEMS * percent) / 100));
    const changed: number[] = new Array(count);
    for (let i = 0; i < count; i++) {
      const idx = this.rng.nextInt(TOTAL_ITEMS);
      const item = this.items[idx];
      const delta = (this.rng.next() - 0.48) * 2 * item.currentPrice * 0.02;
      const newPrice = Math.max(0.01, Math.round((item.currentPrice + delta) * 100) / 100);
      item.prevPrice = item.currentPrice;
      item.currentPrice = newPrice;
      item.isUp = newPrice >= item.prevPrice;
      changed[i] = idx;
    }
    return changed;
  }
}

export function formatCell(item: StockItem): string {
  return `${item.symbol} ${item.currentPrice.toFixed(2)}`;
}
