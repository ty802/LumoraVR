// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Audio;

// Lengths made only of twos, threes and fives run through the fastest butterflies, so anything free to
// pick its own transform length (a zero-padded correlation, a chirp convolution) rounds up to one.
public static class FftSizes
{
    public static int NextFast(int n)
    {
        if (n <= 1)
            return 1;
        long limit = 2L * n;
        long best = long.MaxValue;
        for (long five = 1; five < limit; five *= 5)
        {
            for (long three = five; three < limit; three *= 3)
            {
                long value = three;
                while (value < n)
                    value *= 2;
                if (value < best)
                    best = value;
            }
        }
        return (int)best;
    }

    public static bool IsFast(int n)
    {
        if (n < 1)
            return false;
        foreach (int p in new[] { 2, 3, 5 })
        {
            while (n % p == 0)
                n /= p;
        }
        return n == 1;
    }
}
