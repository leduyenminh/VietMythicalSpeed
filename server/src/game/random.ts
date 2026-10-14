import { randomInt } from 'node:crypto';

/** Same contract as VMS.Core.IRandomSource. */
export interface RandomSource {
  nextUInt(): number;
  nextDouble(): number;
  nextInt(maxExclusive: number): number;
}

/**
 * xoshiro128** seeded through SplitMix32 – bit-for-bit identical to VMS.Core.Xoshiro128 (C#).
 * Uint32Array storage gives the same wrap-around as C# uint arithmetic.
 */
export class Xoshiro128 implements RandomSource {
  private readonly s = new Uint32Array(4);

  constructor(seed: number) {
    const sm = new Uint32Array([seed >>> 0]);
    for (let i = 0; i < 4; i++) this.s[i] = splitMix32(sm);
    if ((this.s[0] | this.s[1] | this.s[2] | this.s[3]) === 0) this.s[0] = 1;
  }

  static randomlySeeded(): { rng: Xoshiro128; seed: number } {
    const seed = randomInt(0, 0x1_0000_0000) >>> 0;
    return { rng: new Xoshiro128(seed), seed };
  }

  nextUInt(): number {
    const s = this.s;
    const result = Math.imul(rotl(Math.imul(s[1], 5) >>> 0, 7), 9) >>> 0;
    const t = (s[1] << 9) >>> 0;
    s[2] ^= s[0];
    s[3] ^= s[1];
    s[1] ^= s[2];
    s[0] ^= s[3];
    s[2] ^= t;
    s[3] = rotl(s[3], 11);
    return result;
  }

  nextDouble(): number {
    return this.nextUInt() / 4294967296;
  }

  nextInt(maxExclusive: number): number {
    if (maxExclusive <= 0) throw new RangeError('maxExclusive must be positive');
    const v = Math.floor(this.nextDouble() * maxExclusive);
    return v >= maxExclusive ? maxExclusive - 1 : v;
  }
}

function rotl(x: number, k: number): number {
  return ((x << k) | (x >>> (32 - k))) >>> 0;
}

function splitMix32(state: Uint32Array): number {
  state[0] = (state[0] + 0x9e3779b9) >>> 0;
  let z = state[0];
  z = Math.imul(z ^ (z >>> 16), 0x85ebca6b) >>> 0;
  z = Math.imul(z ^ (z >>> 13), 0xc2b2ae35) >>> 0;
  return (z ^ (z >>> 16)) >>> 0;
}
