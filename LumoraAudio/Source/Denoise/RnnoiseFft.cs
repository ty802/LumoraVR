// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

// Based on RNNoise (https://gitlab.xiph.org/xiph/rnnoise), BSD-3-Clause.
// kiss_fft.c: Copyright (c) 2003-2004, Mark Borgerding. Lots of modifications by Jean-Marc Valin.
// Copyright (c) 2005-2007, Xiph.Org Foundation. Copyright (c) 2008, Xiph.Org Foundation, CSIRO.
// See LICENSE-RNNoise.txt.

using System;
using System.IO;

namespace Lumora.Audio;

internal struct KissCpx
{
    internal float R;
    internal float I;
}

// kiss_fft as RNNoise builds it: one 960-point mixed radix plan (5, 3, 4, 4, 4), forward only,
// input scaled by 1/960 during the bit-reversed copy. Twiddles and the bit-reverse table come
// from rnnoise_tables.c through the model resource, so no trig runs here.
internal sealed class KissFft
{
    internal const int Size = 960;

    // rnn_kfft.scale as printed in rnnoise_tables.c.
    private const float Scale = 0.0010416667f;

    // kf_factor(960): radix, then the remaining length after that stage.
    private static ReadOnlySpan<short> Factors => [5, 192, 3, 64, 4, 16, 4, 4, 4, 1];

    private readonly KissCpx[] _twiddles;
    private readonly int[] _bitrev;

    internal KissFft(float[] twiddles, int[] bitrev)
    {
        if (twiddles.Length != 2 * Size || bitrev.Length != Size)
            throw new InvalidDataException("FFT tables do not describe 960 points");
        var seen = new bool[Size];
        foreach (int b in bitrev)
        {
            if ((uint)b >= Size || seen[b])
                throw new InvalidDataException("fft_bitrev is not a permutation of 0..959");
            seen[b] = true;
        }
        _twiddles = new KissCpx[Size];
        for (int i = 0; i < Size; i++)
        {
            _twiddles[i].R = twiddles[2 * i];
            _twiddles[i].I = twiddles[2 * i + 1];
        }
        _bitrev = bitrev;
    }

    // rnn_fft_c. input and output must not overlap.
    internal unsafe void Forward(ReadOnlySpan<KissCpx> input, Span<KissCpx> output)
    {
        input = input.Slice(0, Size);
        output = output.Slice(0, Size);
        for (int i = 0; i < Size; i++)
        {
            KissCpx x = input[i];
            ref KissCpx o = ref output[_bitrev[i]];
            o.R = Scale * x.R;
            o.I = Scale * x.I;
        }
        fixed (KissCpx* fout = output)
        fixed (KissCpx* tw = _twiddles)
            Transform(fout, tw);
    }

    // rnn_fft_impl.
    private static unsafe void Transform(KissCpx* fout, KissCpx* tw)
    {
        int* fstride = stackalloc int[8];
        ReadOnlySpan<short> factors = Factors;
        fstride[0] = 1;
        int stages = 0;
        int m;
        do
        {
            int p = factors[2 * stages];
            m = factors[2 * stages + 1];
            fstride[stages + 1] = fstride[stages] * p;
            stages++;
        } while (m != 1);
        m = factors[2 * stages - 1];
        for (int i = stages - 1; i >= 0; i--)
        {
            int m2 = i != 0 ? factors[2 * i - 1] : 1;
            switch (factors[2 * i])
            {
                case 4:
                    Butterfly4(fout, tw, fstride[i], m, fstride[i], m2);
                    break;
                case 3:
                    Butterfly3(fout, tw, fstride[i], m, fstride[i], m2);
                    break;
                case 5:
                    Butterfly5(fout, tw, fstride[i], m, fstride[i], m2);
                    break;
            }
            m = m2;
        }
    }

    private static KissCpx Mul(KissCpx a, KissCpx b)
    {
        KissCpx r;
        r.R = a.R * b.R - a.I * b.I;
        r.I = a.R * b.I + a.I * b.R;
        return r;
    }

    private static unsafe void Butterfly4(KissCpx* fout, KissCpx* tw, int fstride, int m, int n, int mm)
    {
        if (m == 1)
        {
            // Every twiddle is 1.
            for (int i = 0; i < n; i++)
            {
                KissCpx s0, s1;
                s0.R = fout[0].R - fout[2].R;
                s0.I = fout[0].I - fout[2].I;
                fout[0].R += fout[2].R;
                fout[0].I += fout[2].I;
                s1.R = fout[1].R + fout[3].R;
                s1.I = fout[1].I + fout[3].I;
                fout[2].R = fout[0].R - s1.R;
                fout[2].I = fout[0].I - s1.I;
                fout[0].R += s1.R;
                fout[0].I += s1.I;
                s1.R = fout[1].R - fout[3].R;
                s1.I = fout[1].I - fout[3].I;
                fout[1].R = s0.R + s1.I;
                fout[1].I = s0.I - s1.R;
                fout[3].R = s0.R - s1.I;
                fout[3].I = s0.I + s1.R;
                fout += 4;
            }
            return;
        }

        int m2 = 2 * m;
        int m3 = 3 * m;
        KissCpx* begin = fout;
        for (int i = 0; i < n; i++)
        {
            fout = begin + i * mm;
            KissCpx* tw1 = tw;
            KissCpx* tw2 = tw;
            KissCpx* tw3 = tw;
            for (int j = 0; j < m; j++)
            {
                KissCpx s0 = Mul(fout[m], *tw1);
                KissCpx s1 = Mul(fout[m2], *tw2);
                KissCpx s2 = Mul(fout[m3], *tw3);
                KissCpx s3, s4, s5;
                s5.R = fout[0].R - s1.R;
                s5.I = fout[0].I - s1.I;
                fout[0].R += s1.R;
                fout[0].I += s1.I;
                s3.R = s0.R + s2.R;
                s3.I = s0.I + s2.I;
                s4.R = s0.R - s2.R;
                s4.I = s0.I - s2.I;
                fout[m2].R = fout[0].R - s3.R;
                fout[m2].I = fout[0].I - s3.I;
                tw1 += fstride;
                tw2 += fstride * 2;
                tw3 += fstride * 3;
                fout[0].R += s3.R;
                fout[0].I += s3.I;
                fout[m].R = s5.R + s4.I;
                fout[m].I = s5.I - s4.R;
                fout[m3].R = s5.R - s4.I;
                fout[m3].I = s5.I + s4.R;
                fout++;
            }
        }
    }

    private static unsafe void Butterfly3(KissCpx* fout, KissCpx* tw, int fstride, int m, int n, int mm)
    {
        int m2 = 2 * m;
        KissCpx epi3 = tw[fstride * m];
        KissCpx* begin = fout;
        for (int i = 0; i < n; i++)
        {
            fout = begin + i * mm;
            KissCpx* tw1 = tw;
            KissCpx* tw2 = tw;
            for (int k = m; k > 0; k--)
            {
                KissCpx s1 = Mul(fout[m], *tw1);
                KissCpx s2 = Mul(fout[m2], *tw2);
                KissCpx s0, s3;
                s3.R = s1.R + s2.R;
                s3.I = s1.I + s2.I;
                s0.R = s1.R - s2.R;
                s0.I = s1.I - s2.I;
                tw1 += fstride;
                tw2 += fstride * 2;
                fout[m].R = fout[0].R - s3.R * .5f;
                fout[m].I = fout[0].I - s3.I * .5f;
                s0.R *= epi3.I;
                s0.I *= epi3.I;
                fout[0].R += s3.R;
                fout[0].I += s3.I;
                fout[m2].R = fout[m].R + s0.I;
                fout[m2].I = fout[m].I - s0.R;
                fout[m].R = fout[m].R - s0.I;
                fout[m].I = fout[m].I + s0.R;
                fout++;
            }
        }
    }

    private static unsafe void Butterfly5(KissCpx* fout, KissCpx* tw, int fstride, int m, int n, int mm)
    {
        KissCpx ya = tw[fstride * m];
        KissCpx yb = tw[fstride * 2 * m];
        KissCpx* begin = fout;
        for (int i = 0; i < n; i++)
        {
            fout = begin + i * mm;
            KissCpx* f0 = fout;
            KissCpx* f1 = fout + m;
            KissCpx* f2 = fout + 2 * m;
            KissCpx* f3 = fout + 3 * m;
            KissCpx* f4 = fout + 4 * m;
            for (int u = 0; u < m; u++)
            {
                KissCpx s0 = *f0;
                KissCpx s1 = Mul(*f1, tw[u * fstride]);
                KissCpx s2 = Mul(*f2, tw[2 * u * fstride]);
                KissCpx s3 = Mul(*f3, tw[3 * u * fstride]);
                KissCpx s4 = Mul(*f4, tw[4 * u * fstride]);
                KissCpx s5, s6, s7, s8, s9, s10, s11, s12;

                s7.R = s1.R + s4.R;
                s7.I = s1.I + s4.I;
                s10.R = s1.R - s4.R;
                s10.I = s1.I - s4.I;
                s8.R = s2.R + s3.R;
                s8.I = s2.I + s3.I;
                s9.R = s2.R - s3.R;
                s9.I = s2.I - s3.I;

                f0->R = f0->R + (s7.R + s8.R);
                f0->I = f0->I + (s7.I + s8.I);

                s5.R = s0.R + (s7.R * ya.R + s8.R * yb.R);
                s5.I = s0.I + (s7.I * ya.R + s8.I * yb.R);
                s6.R = s10.I * ya.I + s9.I * yb.I;
                s6.I = -(s10.R * ya.I + s9.R * yb.I);

                f1->R = s5.R - s6.R;
                f1->I = s5.I - s6.I;
                f4->R = s5.R + s6.R;
                f4->I = s5.I + s6.I;

                s11.R = s0.R + (s7.R * yb.R + s8.R * ya.R);
                s11.I = s0.I + (s7.I * yb.R + s8.I * ya.R);
                s12.R = s9.I * ya.I - s10.I * yb.I;
                s12.I = s10.R * yb.I - s9.R * ya.I;

                f2->R = s11.R + s12.R;
                f2->I = s11.I + s12.I;
                f3->R = s11.R - s12.R;
                f3->I = s11.I - s12.I;

                f0++;
                f1++;
                f2++;
                f3++;
                f4++;
            }
        }
    }
}
