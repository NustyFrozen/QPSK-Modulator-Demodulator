using MathNet.Numerics.IntegralTransforms;
using System;
using System.IO;
using System.Linq;
using System.Numerics;

namespace QPSK.Models;

public static class HelperFunctions
{
    public static class BitPacker
    {
        // MSB-first within each byte.
        public static string BytesToBitString(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0) return string.Empty;

            var chars = new char[data.Length * 8];
            int k = 0;

            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                for (int bit = 7; bit >= 0; bit--)
                    chars[k++] = ((b >> bit) & 1) == 0 ? '0' : '1';
            }

            return new string(chars);
        }

        // Convert bits->bytes starting at a bit offset (0..7). Ignores trailing incomplete byte.
        public static byte[] BitsToBytes(string bits, int bitOffset)
        {
            if (bits is null) throw new ArgumentNullException(nameof(bits));
            if ((uint)bitOffset > 7) throw new ArgumentOutOfRangeException(nameof(bitOffset));

            int usableBits = bits.Length - bitOffset;
            if (usableBits < 8) return Array.Empty<byte>();

            int nBytes = usableBits / 8;
            var bytes = new byte[nBytes];

            int p = bitOffset;
            for (int i = 0; i < nBytes; i++)
            {
                byte v = 0;
                for (int j = 0; j < 8; j++)
                {
                    v <<= 1;
                    char c = bits[p++];
                    if (c == '1') v |= 1;
                    else if (c != '0') throw new FormatException("Bit string must contain only '0'/'1'.");
                }
                bytes[i] = v;
            }
            return bytes;
        }

        public static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
        {
            if (needle.Length == 0) return 0;
            if (needle.Length > haystack.Length) return -1;

            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
                    return i;
            }
            return -1;
        }
    }
    /// <summary>
    /// Saves Complex[] IQ data as raw CS16 (interleaved int16: I,Q,I,Q,...)
    /// </summary>
    public static void SaveAsCs16(this Complex[] iq, string filePath)
    {
        if (iq == null || iq.Length == 0)
            throw new ArgumentException("IQ array is empty.");

        // find normalization factor
        double maxVal = 0;
        foreach (var c in iq)
        {
            double ar = Math.Abs(c.Real);
            double ai = Math.Abs(c.Imaginary);
            if (ar > maxVal) maxVal = ar;
            if (ai > maxVal) maxVal = ai;
        }

        if (maxVal < 1e-12) maxVal = 1.0; // avoid div0

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        foreach (var c in iq)
        {
            short I = (short)Math.Max(short.MinValue,
                Math.Min(short.MaxValue, c.Real / maxVal * short.MaxValue));

            short Q = (short)Math.Max(short.MinValue,
                Math.Min(short.MaxValue, c.Imaginary / maxVal * short.MaxValue));

            bw.Write(I);
            bw.Write(Q);
        }
    }
    /// <summary>
    /// Simple Convolution between complex and real signals
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public static Complex[] Convolve(this Complex[] a, double[] b)
    {
        int M = a.Length + b.Length - 1;

        var results = new Complex[M];
        var resultSpan = results.AsSpan();

        for (int n = 0; n < M; n++)
        {
            // clamp k range so that both k and (n - k) are in bounds
            int kmin = Math.Max(0, n - (a.Length - 1));
            int kmax = Math.Min(n, b.Length - 1);
            for (int k = kmin; k <= kmax; k++)
            {
                resultSpan[n]  += a[n - k] * b[k];   // b is real, so this is just scaling a[n-k]
            }
        }

        return results;
    }
    public static double[] Derivative(double[] h, double Ts = 1.0)
    {
        int n = h.Length;
        var hd = new double[n];

        if (n < 2) return hd;

        // interior points � central difference
        for (int i = 1; i < n - 1; i++)
        {
            hd[i] = (h[i + 1] - h[i - 1]) / (2.0 * Ts);
        }

        // simple forward/backward at the edges
        hd[0] = (h[1] - h[0]) / Ts;
        hd[n - 1] = (h[n - 1] - h[n - 2]) / Ts;

        return hd;
    }
    public static void Multiply(this Complex[] a, double[] b)
    {
        for (int i = 0; i < Math.Min(a.Length,b.Length); i++)
        {
            a[i] *= b[i];
        }
    }
    public static Complex[] Multiply(this Complex[] a, Complex[] b)
    {
        Complex[] results = new Complex[Math.Max(a.Length, b.Length)];
        if (a.Length <= b.Length)
            results = a.Select((x, i) =>(i >= b.Length) ? x:x * b[i]).ToArray();
        else
            results = b.Select((x, i) => (i >= a.Length) ? x : x * a[i]).ToArray();
        return results;
    }
    public static Complex[] FftConvolve(this Complex[] x, double[] h)
    {
        int L = x.Length + h.Length - 1;
        int N = 1;
        while (N < L) N <<= 1;  // next power of two

        var X = new Complex[N];
        var H = new Complex[N];

        // copy input
        for (int i = 0; i < x.Length; i++)
            X[i] = x[i];

        // copy real filter
        for (int i = 0; i < h.Length; i++)
            H[i] = new Complex(h[i], 0.0);

        // forward FFT
        Fourier.Forward(X, FourierOptions.Matlab);
        Fourier.Forward(H, FourierOptions.Matlab);

        // pointwise multiply
        for (int i = 0; i < N; i++)
            X[i] *= H[i];

        // inverse FFT
        Fourier.Inverse(X, FourierOptions.Matlab);

        // trim to linear convolution length
        var y = new Complex[L];
        for (int i = 0; i < L; i++)
            y[i] = X[i];

        return y;
    }
}