using MathNet.Numerics.IntegralTransforms;
using System;
using System.Numerics;

namespace QPSK.Models;

public class ComplexFIRFilter
    {
       public readonly Complex[] taps;
        readonly Complex[] delay;
        int index;

        public ComplexFIRFilter(Complex[] taps)
        {
            this.taps = taps;
            delay = new Complex[taps.Length];
            index = 0;
        }
    /// <summary>
    /// FFT-based convolution: filters the whole buffer at once.
    /// </summary>
    public Complex[] fftFilter(Complex[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Length == 0) return Array.Empty<Complex>();

        int nData = data.Length;
        int nTaps = taps.Length;
        int nConv = nData + nTaps - 1;

        // Choose FFT size as next power-of-two >= nConv
        int fftSize = 1;
        while (fftSize < nConv) fftSize <<= 1;

        // Prepare FFT buffers
        var X = new Complex[fftSize];
        var H = new Complex[fftSize];

        Array.Copy(data, X, nData);
        Array.Copy(taps, H, nTaps);

        // Forward FFT
        Fourier.Forward(X, FourierOptions.Matlab);
        Fourier.Forward(H, FourierOptions.Matlab);

        // Pointwise multiply in frequency domain
        for (int i = 0; i < fftSize; i++)
        {
            X[i] *= H[i];
        }

        // Inverse FFT (MathNet's Matlab option does 1/N scaling in the inverse)
        Fourier.Inverse(X, FourierOptions.Matlab);

        // X now contains the linear convolution result of length nConv
        var y = new Complex[nData]; // if you want "filter-like" output same length as input

        // Standard causal FIR: y[k] = sum h[i]*x[k-i]
        // The raw linear convolution has length nData+nTaps-1; 
        // the "aligned" part for x[0..nData-1] is indices (nTaps-1 .. nTaps-1 + nData-1)
        int offset = nTaps - 1;
        for (int i = 0; i < nData; i++)
        {
            y[i] = X[offset + i];
        }

        return y;
    }
    public Complex Filter(Complex x)
        {
            delay[index] = x;
            Complex acc = Complex.Zero;

            int j = index;
            for (int i = 0; i < taps.Length; i++)
            {
                acc += taps[i] * delay[j];
                j--;
                if (j < 0) j = delay.Length - 1;
            }

            index++;
            if (index >= delay.Length) index = 0;

            return acc;
        }
    public Complex[] Filter(Complex[] data)
    {
        var results = new Complex[data.Length].AsSpan();
        for (int i = 0; i < data.Length; i++)
            results[i] = Filter(data[i]);
        return results.ToArray();
    }

}
public class RealFIRFilter
{
    readonly double[] taps;
    readonly double[] delay;
    int index;

    public RealFIRFilter(double[] taps)
    {
        this.taps = taps;
        delay = new double[taps.Length];
        index = 0;
    }

    public double Filter(double x)
    {
        delay[index] = x;
        double acc = 0.0;

        int j = index;
        for (int i = 0; i < taps.Length; i++)
        {
            acc += taps[i] * delay[j];
            j--;
            if (j < 0) j = delay.Length - 1;
        }

        index++;
        if (index >= delay.Length) index = 0;

        return acc;
    }
}
