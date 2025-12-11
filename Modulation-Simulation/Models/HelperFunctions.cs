using MathNet.Numerics.IntegralTransforms;
using System;
using System.IO;
using System.Linq;
using System.Numerics;

namespace QPSK.Models;

public static class HelperFunctions
{
    
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
public static class WavExtensions
{
    /// <summary>
    /// Save real-valued signal as 16-bit PCM WAV (mono).
    /// samples: double[] assumed roughly in [-1, 1].
    /// Automatically normalizes if needed to avoid clipping.
    /// </summary>
    public static void SaveAsWav(this double[] samples, string path, int sampleRate)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));
        if (samples.Length == 0) throw new ArgumentException("Signal is empty.", nameof(samples));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        // Find max abs to normalize (avoid clipping)
        double max = 0.0;
        for (int i = 0; i < samples.Length; i++)
        {
            double v = Math.Abs(samples[i]);
            if (v > max) max = v;
        }
        double norm = max > 1.0 ? max : 1.0; // if already within [-1,1], keep

        short[] pcm = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            double scaled = samples[i] / norm;                 // now in roughly [-1,1]
            int val = (int)Math.Round(scaled * short.MaxValue);
            if (val > short.MaxValue) val = short.MaxValue;
            if (val < short.MinValue) val = short.MinValue;
            pcm[i] = (short)val;
        }

        WritePcm16WavMono(path, sampleRate, pcm);
    }

    /// <summary>
    /// Save complex IQ as 16-bit "complex16" WAV.
    /// Stored as stereo 16-bit PCM: Left = I, Right = Q.
    /// samples: Complex[] assumed roughly in [-1, 1] for both I and Q.
    /// Automatically normalizes based on max(|I|, |Q|).
    /// </summary>
    public static void SaveAsComplex16Wav(this Complex[] iqSamples, string path, int sampleRate)
    {
        if (iqSamples == null) throw new ArgumentNullException(nameof(iqSamples));
        if (iqSamples.Length == 0) throw new ArgumentException("Signal is empty.", nameof(iqSamples));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        // Find max magnitude across I and Q for normalization
        double max = 0.0;
        for (int i = 0; i < iqSamples.Length; i++)
        {
            double ar = Math.Abs(iqSamples[i].Real);
            double ai = Math.Abs(iqSamples[i].Imaginary);
            if (ar > max) max = ar;
            if (ai > max) max = ai;
        }
        double norm = max > 1.0 ? max : 1.0;

        // Interleave I,Q as stereo: [I0, Q0, I1, Q1, ...]
        short[] pcm = new short[iqSamples.Length * 2];
        int idx = 0;
        for (int i = 0; i < iqSamples.Length; i++)
        {
            double iVal = iqSamples[i].Real / norm;
            double qVal = iqSamples[i].Imaginary / norm;

            int iInt = (int)Math.Round(iVal * short.MaxValue);
            int qInt = (int)Math.Round(qVal * short.MaxValue);

            if (iInt > short.MaxValue) iInt = short.MaxValue;
            if (iInt < short.MinValue) iInt = short.MinValue;
            if (qInt > short.MaxValue) qInt = short.MaxValue;
            if (qInt < short.MinValue) qInt = short.MinValue;

            pcm[idx++] = (short)iInt; // Left  = I
            pcm[idx++] = (short)qInt; // Right = Q
        }

        WritePcm16WavStereo(path, sampleRate, pcm);
    }

    // --- Internal helpers: write WAV headers + data ---

    private static void WritePcm16WavMono(string path, int sampleRate, short[] pcm)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        int numChannels = 1;
        int bitsPerSample = 16;
        int byteRate = sampleRate * numChannels * bitsPerSample / 8;
        short blockAlign = (short)(numChannels * bitsPerSample / 8);
        int dataSize = pcm.Length * blockAlign;
        int fmtChunkSize = 16;
        int riffChunkSize = 4 + (8 + fmtChunkSize) + (8 + dataSize);

        // RIFF header
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(riffChunkSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(fmtChunkSize);
        bw.Write((short)1);             // PCM format
        bw.Write((short)numChannels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)bitsPerSample);

        // data chunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);

        // PCM data
        for (int i = 0; i < pcm.Length; i++)
            bw.Write(pcm[i]);
    }

    private static void WritePcm16WavStereo(string path, int sampleRate, short[] interleavedStereo)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        int numChannels = 2;
        int bitsPerSample = 16;
        int byteRate = sampleRate * numChannels * bitsPerSample / 8;
        short blockAlign = (short)(numChannels * bitsPerSample / 8);
        int dataSize = interleavedStereo.Length * 2; // each short = 2 bytes
        int fmtChunkSize = 16;
        int riffChunkSize = 4 + (8 + fmtChunkSize) + (8 + dataSize);

        // RIFF header
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(riffChunkSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(fmtChunkSize);
        bw.Write((short)1);             // PCM format
        bw.Write((short)numChannels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)bitsPerSample);

        // data chunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);

        // PCM data
        for (int i = 0; i < interleavedStereo.Length; i++)
            bw.Write(interleavedStereo[i]);
    }
}