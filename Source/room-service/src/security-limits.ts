// Bounded, in-memory request window limiter. Never stores raw session tokens.
// An excess of unique client addresses fails closed for non-emergency endpoints.
export class WindowRateLimiter {
  private readonly slots = new Map<string, {start: number; count: number}>();
  constructor(private readonly maximum: number, private readonly windowMs: number,
              private readonly maxKeys = 4096, private readonly now: () => number = Date.now) {
    if (maximum <= 0 || windowMs <= 0 || maxKeys <= 0) throw new RangeError('limiter bounds');
  }
  allow(key: string): boolean {
    const current = this.now();
    const old = this.slots.get(key);
    if (old && current >= old.start && current - old.start < this.windowMs) {
      if (old.count >= this.maximum) return false;
      ++old.count; return true;
    }
    if (!old && this.slots.size >= this.maxKeys) {
      this.prune();
      if (this.slots.size >= this.maxKeys) return false;
    }
    this.slots.set(key, {start: current, count: 1});
    return true;
  }
  prune(): void {
    const current = this.now();
    for (const [key, value] of this.slots) {
      if (current < value.start || current - value.start >= this.windowMs)
        this.slots.delete(key);
    }
  }
  get keyCount(): number { return this.slots.size; }
}
