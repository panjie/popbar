using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace PopBar;

// Port of dsh-plugin-speech's Edge v2 token implementation (MIT).
internal static class EdgeSpeechToken
{
    internal const string ClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    internal const string BrowserVersion = "143.0.3650.75";

    static readonly uint[] InitialState = {
        0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19
    };
    static readonly uint[] PrefixState = {
        0xfd9fbea2, 0x91243ce7, 0xbe499094, 0xcd2e1e0a, 0x6c006f49, 0x04bd35ab, 0xfeb0a7e4, 0x03effbd2
    };
    static readonly uint[] PrefixSchedule = {
        0xeb24bf9e, 0x1558691f, 0xa0c09ee2, 0x0cfa32ba, 0xa622ee8c, 0xd1dd9491, 0xc0dd25a0,
        0xf9e70ab6, 0xf0c31573, 0x7058ce4e, 0x7188e606, 0x00e5ab33, 0xd258b060, 0x72766564
    };
    static readonly uint[] RoundConstants = {
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
    };
    static readonly (uint Coefficient, uint Offset)[] ChecksumCoefficients = {
        (0xe82e91b1, 0xb32700d0), (0x44de24c6, 0x88dff51d), (0x3beb65bc, 0x29514038),
        (0xc89c5933, 0xb328cbdf), (0x8d1ec211, 0xd405b4d5), (0x314ef5d5, 0x26e55f3c),
        (0xe92491b1, 0x607801c6), (0xf0a5b9d5, 0x82b2c1fb), (0x06064cf3, 0xfa51a3da),
        (0xda79fa60, 0x207cc464), (0xbed20560, 0x3c3c74f3), (0x89f9c9f8, 0xd2d14b61),
        (0x35fa38c6, 0xbb04a2c1), (0xcb49e3c6, 0xc5626167)
    };
    const uint PrefixRoundConstant = 0xc9dc06a7;

    internal static string Create(DateTimeOffset time)
    {
        var ticks = time.ToFileTime();
        ticks -= ticks % TimeSpan.FromMinutes(5).Ticks;
        var data = Encoding.ASCII.GetBytes(ticks.ToString(CultureInfo.InvariantCulture) + ClientToken);
        var digest = Digest(data);
        var checksum = Checksum(data, digest);
        return Convert.ToHexString(checksum) + "-" + Convert.ToHexString(digest);
    }

    static byte[] Digest(ReadOnlySpan<byte> data)
    {
        // The service modifies the first SHA-256 block and supplies a precomputed state for its first 14 rounds.
        Span<uint> words = stackalloc uint[64];
        words.Clear();
        words[14] = BinaryPrimitives.ReadUInt32BigEndian(data) >> 8;
        words[15] = BinaryPrimitives.ReadUInt32BigEndian(data[3..]);
        for (int i = 16; i < 30; i++)
        {
            uint sigma1 = SmallSigma1(words[i - 2]);
            uint sigma0 = SmallSigma0(words[i - 15]);
            if (i == 16) sigma1 ^= 0x000b92c0;
            if (i == 29) sigma0 ^= 0x059c0b80;
            words[i] = unchecked(PrefixSchedule[i - 16] + sigma1 + words[i - 7] + sigma0);
        }
        words[30] = unchecked(NextWord(words, 30) + PrefixRoundConstant - RoundConstants[14]);
        ExpandSchedule(words, 31);
        var state = (uint[])InitialState.Clone();
        Compress(state, PrefixState, words, firstRound: 14);

        int messageLength = 57 + data.Length;
        int paddedLength = ((messageLength + 9 + 63) / 64 - 1) * 64;
        var tail = new byte[paddedLength];
        data[7..].CopyTo(tail);
        tail[data.Length - 7] = 0x80;
        BinaryPrimitives.WriteUInt64BigEndian(tail.AsSpan(^8), (ulong)messageLength * 8);
        for (int offset = 0; offset < tail.Length; offset += 64)
        {
            for (int i = 0; i < 16; i++)
                words[i] = BinaryPrimitives.ReadUInt32BigEndian(tail.AsSpan(offset + i * 4));
            ExpandSchedule(words, 16);
            Compress(state, state, words, firstRound: 0);
        }
        var digest = new byte[32];
        for (int i = 0; i < state.Length; i++)
            BinaryPrimitives.WriteUInt32BigEndian(digest.AsSpan(i * 4), state[i]);
        return digest;
    }

    static void ExpandSchedule(Span<uint> words, int start)
    {
        for (int i = start; i < words.Length; i++) words[i] = NextWord(words, i);
    }

    static uint NextWord(ReadOnlySpan<uint> words, int i) =>
        unchecked(SmallSigma1(words[i - 2]) + words[i - 7] + SmallSigma0(words[i - 15]) + words[i - 16]);

    static uint SmallSigma0(uint word) => BitOperations.RotateRight(word, 7) ^ BitOperations.RotateRight(word, 18) ^ (word >> 3);
    static uint SmallSigma1(uint word) => BitOperations.RotateRight(word, 17) ^ BitOperations.RotateRight(word, 19) ^ (word >> 10);

    static void Compress(Span<uint> state, ReadOnlySpan<uint> working, ReadOnlySpan<uint> words, int firstRound)
    {
        uint a = working[0], b = working[1], c = working[2], d = working[3];
        uint e = working[4], f = working[5], g = working[6], h = working[7];
        unchecked
        {
            for (int i = firstRound; i < words.Length; i++)
            {
                uint sigma1 = BitOperations.RotateRight(e, 6) ^ BitOperations.RotateRight(e, 11) ^ BitOperations.RotateRight(e, 25);
                uint sigma0 = BitOperations.RotateRight(a, 2) ^ BitOperations.RotateRight(a, 13) ^ BitOperations.RotateRight(a, 22);
                uint constant = firstRound == 14 && i == 14 ? PrefixRoundConstant : RoundConstants[i];
                uint first = h + sigma1 + (g ^ (e & (f ^ g))) + constant + words[i];
                uint second = sigma0 + ((a & b) | (c & (a | b)));
                (a, b, c, d, e, f, g, h) = (first + second, a, b, c, d + first, e, f, g);
            }
            state[0] += a;
            state[1] += b;
            state[2] += c;
            state[3] += d;
            state[4] += e;
            state[5] += f;
            state[6] += g;
            state[7] += h;
        }
    }

    static byte[] Checksum(ReadOnlySpan<byte> data, ReadOnlySpan<byte> digest)
    {
        unchecked
        {
            const uint factor = 0x82893933;
            uint evenKey = BinaryPrimitives.ReadUInt32LittleEndian(digest) | 1;
            uint oddKey = BinaryPrimitives.ReadUInt32LittleEndian(digest[4..]) | 1;
            var padded = new byte[((data.Length + 1 + 7) / 8) * 8];
            data.CopyTo(padded.AsSpan(1));
            var words = new (uint Term, uint Offset)[ChecksumCoefficients.Length + padded.Length / 4];
            for (int i = 0; i < ChecksumCoefficients.Length; i++)
            {
                var (coefficient, offset) = ChecksumCoefficients[i];
                words[i] = (coefficient * factor * (i % 2 == 0 ? evenKey : oddKey), offset);
            }
            for (int i = 0; i < padded.Length; i += 8)
            {
                uint even = BinaryPrimitives.ReadUInt32LittleEndian(padded.AsSpan(i));
                uint odd = BinaryPrimitives.ReadUInt32LittleEndian(padded.AsSpan(i + 4));
                int index = ChecksumCoefficients.Length + i / 4;
                words[index] = (even * evenKey, Rotate16(even) * 0xef0569fb);
                words[index + 1] = (odd * oddKey, Rotate16(odd) * 0xc31713db);
            }
            words[ChecksumCoefficients.Length].Term += 0xf2f90b1a * factor * evenKey;
            words[ChecksumCoefficients.Length].Offset += 0x0b1a0000;

            uint swappedSum = 0, swappedLast = 0, state = 0, sum = 0;
            for (int i = 0; i < words.Length; i += 2)
            {
                var (evenTerm, evenOffset) = words[i];
                var (oddTerm, oddOffset) = words[i + 1];
                uint evenProduct = Rotate16(evenOffset + evenTerm) * 0x76f50f34;
                uint oddProduct = Rotate16(oddOffset + oddTerm) * 0x1881ce3c;
                swappedLast = Rotate16(oddProduct) * 0x35bd1ec9 + oddProduct;
                swappedSum += Rotate16(evenProduct) * 0xc3efea97 + evenProduct + swappedLast;

                state = Rotate16(state * evenKey + evenTerm) * 0xcf98b111;
                state = Rotate16(state) * 0x87085b9f;
                state = Rotate16(state) * 0x12ceb96d;
                state = Rotate16(state) * 0x257e1d83;
                sum += state;
                state = Rotate16(state * oddKey + oddTerm) * 0xa27416f5;
                state = Rotate16(state) * 0xd38396ff;
                state = Rotate16(state) * 0x7c932b89;
                state = Rotate16(state) * 0xbfa49f69;
                sum += state;
            }
            var result = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(result, swappedLast ^ state);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), swappedSum ^ sum);
            return result;
        }
    }

    static uint Rotate16(uint word) => BitOperations.RotateLeft(word, 16);
}
