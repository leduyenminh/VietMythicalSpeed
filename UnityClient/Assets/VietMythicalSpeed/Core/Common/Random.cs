using System;

namespace VMS.Core
{
    /// <summary>Source of randomness used by gameplay rules. Abstracted so rules are deterministic in tests.</summary>
    public interface IRandomSource
    {
        uint NextUInt();

        /// <summary>Uniform double in [0, 1) with 32 bits of resolution.</summary>
        double NextDouble();

        /// <summary>Uniform integer in [0, maxExclusive). Always consumes exactly one draw.</summary>
        int NextInt(int maxExclusive);
    }

    /// <summary>
    /// xoshiro128** seeded through SplitMix32.
    /// This generator is mirrored bit-for-bit in server/src/game/random.ts so the offline (client) and
    /// online (server) gacha produce identical sequences for the same seed. Do not change one without the other.
    /// </summary>
    public sealed class Xoshiro128 : IRandomSource
    {
        uint _s0, _s1, _s2, _s3;

        public Xoshiro128(uint seed)
        {
            uint sm = seed;
            _s0 = SplitMix32(ref sm);
            _s1 = SplitMix32(ref sm);
            _s2 = SplitMix32(ref sm);
            _s3 = SplitMix32(ref sm);
            if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
        }

        /// <summary>Seeds from the OS cryptographic RNG.</summary>
        public static Xoshiro128 CreateRandomlySeeded()
        {
            var bytes = new byte[4];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return new Xoshiro128(BitConverter.ToUInt32(bytes, 0));
        }

        static uint SplitMix32(ref uint state)
        {
            unchecked
            {
                state += 0x9E3779B9u;
                uint z = state;
                z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
                z = (z ^ (z >> 13)) * 0xC2B2AE35u;
                return z ^ (z >> 16);
            }
        }

        static uint Rotl(uint x, int k) => (x << k) | (x >> (32 - k));

        public uint NextUInt()
        {
            unchecked
            {
                uint result = Rotl(_s1 * 5u, 7) * 9u;
                uint t = _s1 << 9;
                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = Rotl(_s3, 11);
                return result;
            }
        }

        public double NextDouble() => NextUInt() / 4294967296.0;

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            int v = (int)(NextDouble() * maxExclusive);
            return v >= maxExclusive ? maxExclusive - 1 : v;
        }
    }

    public static class StableHash
    {
        /// <summary>32-bit FNV-1a over UTF-16 code units. Stable across platforms and runs.</summary>
        public static uint Fnv1a(string text)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (text == null) return h;
                for (int i = 0; i < text.Length; i++)
                {
                    h ^= text[i];
                    h *= 16777619u;
                }
                return h;
            }
        }
    }

    /// <summary>Smooth 1D value noise in [-1, 1], used to shape the track.</summary>
    public sealed class ValueNoise1D
    {
        readonly float[] _lattice;

        public ValueNoise1D(uint seed, int size = 512)
        {
            var rng = new Xoshiro128(seed);
            _lattice = new float[size];
            for (int i = 0; i < size; i++) _lattice[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        public float Sample(float x)
        {
            int n = _lattice.Length;
            float fx = (float)Math.Floor(x);
            int i0 = (int)fx;
            float f = x - fx;
            int a = ((i0 % n) + n) % n;
            int b = (a + 1) % n;
            float u = f * f * f * (f * (f * 6f - 15f) + 10f);
            return _lattice[a] + (_lattice[b] - _lattice[a]) * u;
        }
    }
}
