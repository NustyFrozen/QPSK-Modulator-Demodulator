using MathNet.Numerics.IntegralTransforms;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace QPSK.Models;

public class ComplexFIRFilter
{
   
    public readonly float[] taps;

    // Pre-reversed taps split into planar arrays for SIMD-friendly complex MAC
    private readonly float[] _tapsIRev;
    private readonly float[] _tapsQRev;

    // Double-length planar delay buffers (pre-buffering) so window is always contiguous
    private readonly float[] _delayI2N;
    private readonly float[] _delayQ2N;

    private readonly int _nTaps;   // N
    private int _pos;              // next write position in [0..N-1]

    // FFT cache
    private int _fftSizeCached;
    private Complex[]? _fftH;      // frequency-domain taps (cached)
    private Complex[]? _fftX;      // work buffer

    public ComplexFIRFilter(float[] tapsInterleavedIQ)
    {
        if (tapsInterleavedIQ == null) throw new ArgumentNullException(nameof(tapsInterleavedIQ));
        if ((tapsInterleavedIQ.Length & 1) != 0) throw new ArgumentException("Taps must be interleaved IQ with even length.", nameof(tapsInterleavedIQ));
        if (tapsInterleavedIQ.Length == 0) throw new ArgumentException("Taps cannot be empty.", nameof(tapsInterleavedIQ));

        taps = (float[])tapsInterleavedIQ.Clone();
        _nTaps = taps.Length >> 1;

        _tapsIRev = new float[_nTaps];
        _tapsQRev = new float[_nTaps];

        // Reverse taps so we can dot() with a chronological contiguous window
        // tapsRev[k] = taps[N-1-k]
        for (int k = 0; k < _nTaps; k++)
        {
            int src = (_nTaps - 1 - k) << 1;
            _tapsIRev[k] = taps[src];
            _tapsQRev[k] = taps[src + 1];
        }

        _delayI2N = new float[_nTaps * 2];
        _delayQ2N = new float[_nTaps * 2];
        _pos = 0;
    }

    /// <summary>
    /// Filter one interleaved IQ sample.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Filter(float inI, float inQ, out float outI, out float outQ)
    {
        int p = _pos;
        int pN = p + _nTaps;

        _delayI2N[p] = inI;
        _delayQ2N[p] = inQ;
        _delayI2N[pN] = inI;
        _delayQ2N[pN] = inQ;

        int start = p + 1; 
        if (start >= _nTaps) start -= _nTaps;

        ComplexDotWindow(start, out outI, out outQ);

        p++;
        if (p == _nTaps) p = 0;
        _pos = p;
    }

    
    public void Filter(ReadOnlySpan<float> iqIn, Span<float> iqOut)
    {
        if ((iqIn.Length & 1) != 0) throw new ArgumentException("Input must be interleaved IQ with even length.", nameof(iqIn));
        if (iqOut.Length < iqIn.Length) throw new ArgumentException("Output span is too small.", nameof(iqOut));

        for (int s = 0; s < iqIn.Length; s += 2)
        {
            Filter(iqIn[s], iqIn[s + 1], out float yI, out float yQ);
            iqOut[s] = yI;
            iqOut[s + 1] = yQ;
        }
    }

    /// <summary>
    /// FFT-based convolution over a whole buffer (interleaved IQ). Caches FFT of taps per fftSize.
    /// </summary>
    public float[] fftFilter(float[] iqData)
    {
        if (iqData == null) throw new ArgumentNullException(nameof(iqData));
        if ((iqData.Length & 1) != 0) throw new ArgumentException("Data must be interleaved IQ with even length.", nameof(iqData));
        if (iqData.Length == 0) return Array.Empty<float>();

        int nData = iqData.Length >> 1; // complex samples
        int nTaps = _nTaps;
        int nConv = nData + nTaps - 1;

        int fftSize = 1;
        while (fftSize < nConv) fftSize <<= 1;

        EnsureFftCache(fftSize);

        // Prepare X
        Array.Clear(_fftX!, 0, fftSize);
        for (int i = 0; i < nData; i++)
        {
            int s = i << 1;
            _fftX![i] = new Complex(iqData[s], iqData[s + 1]);
        }

        Fourier.Forward(_fftX!, FourierOptions.Matlab);

        // X *= H (H is cached in frequency domain)
        var H = _fftH!;
        var X = _fftX!;
        for (int i = 0; i < fftSize; i++)
            X[i] *= H[i];

        Fourier.Inverse(X, FourierOptions.Matlab);

        // Return "filter-like" output same length as input, aligned like the original code
        int offset = nTaps - 1;
        var y = new float[iqData.Length];
        for (int i = 0; i < nData; i++)
        {
            Complex c = X[offset + i];
            int s = i << 1;
            y[s] = (float)c.Real;
            y[s + 1] = (float)c.Imaginary;
        }

        return y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ComplexDotWindow(int start, out float outI, out float outQ)
    {
        // Dot over k=0..N-1:
        // yI += hI * xI - hQ * xQ
        // yQ += hI * xQ + hQ * xI
        // where h are reversed taps, x is chronological window [start..start+N-1]
        float accI = 0f, accQ = 0f;

        int N = _nTaps;
        float[] xI = _delayI2N;
        float[] xQ = _delayQ2N;

        if (Vector.IsHardwareAccelerated)
        {
            int w = Vector<float>.Count;
            int nVec = N - (N % w);

            var vAccI = Vector<float>.Zero;
            var vAccQ = Vector<float>.Zero;

            int baseIdx = start; 
            for (int i = 0; i < nVec; i += w)
            {
                var vXI = new Vector<float>(xI, baseIdx + i);
                var vXQ = new Vector<float>(xQ, baseIdx + i);
                var vHI = new Vector<float>(_tapsIRev, i);
                var vHQ = new Vector<float>(_tapsQRev, i);

                vAccI += (vHI * vXI) - (vHQ * vXQ);
                vAccQ += (vHI * vXQ) + (vHQ * vXI);
            }

            for (int lane = 0; lane < Vector<float>.Count; lane++)
            {
                accI += vAccI[lane];
                accQ += vAccQ[lane];
            }

            // tail
            for (int i = nVec; i < N; i++)
            {
                float xi = xI[baseIdx + i];
                float xq = xQ[baseIdx + i];
                float hi = _tapsIRev[i];
                float hq = _tapsQRev[i];

                accI += (hi * xi) - (hq * xq);
                accQ += (hi * xq) + (hq * xi);
            }
        }
        else
        {
            int baseIdx = start;
            for (int i = 0; i < N; i++)
            {
                float xi = xI[baseIdx + i];
                float xq = xQ[baseIdx + i];
                float hi = _tapsIRev[i];
                float hq = _tapsQRev[i];

                accI += (hi * xi) - (hq * xq);
                accQ += (hi * xq) + (hq * xi);
            }
        }

        outI = accI;
        outQ = accQ;
    }

    private void EnsureFftCache(int fftSize)
    {
        if (_fftSizeCached == fftSize && _fftH != null && _fftX != null)
            return;

        _fftSizeCached = fftSize;
        _fftH = new Complex[fftSize];
        _fftX = new Complex[fftSize];

        // Build H from float taps
        Array.Clear(_fftH, 0, fftSize);
        for (int i = 0; i < _nTaps; i++)
        {
            int t = i << 1;
            _fftH[i] = new Complex(taps[t], taps[t + 1]);
        }

        Fourier.Forward(_fftH, FourierOptions.Matlab); // cache frequency-domain taps
    }
}

public class RealFIRFilter
{
    private readonly double[] _tapsRev;
    private readonly double[] _delay2N;
    private readonly int _nTaps;
    private int _pos;

    public RealFIRFilter(double[] taps)
    {
        if (taps == null) throw new ArgumentNullException(nameof(taps));
        if (taps.Length == 0) throw new ArgumentException("Taps cannot be empty.", nameof(taps));

        _nTaps = taps.Length;
        _tapsRev = new double[_nTaps];
        for (int i = 0; i < _nTaps; i++)
            _tapsRev[i] = taps[_nTaps - 1 - i];

        _delay2N = new double[_nTaps * 2];
        _pos = 0;
    }

    public double Filter(double x)
    {
        int p = _pos;
        int pN = p + _nTaps;
        _delay2N[p] = x;
        _delay2N[pN] = x;

        int start = p + 1;
        if (start >= _nTaps) start -= _nTaps;

        double acc = 0.0;
        int baseIdx = start;

        if (Vector.IsHardwareAccelerated)
        {
            int w = Vector<double>.Count;
            int nVec = _nTaps - (_nTaps % w);
            var vAcc = Vector<double>.Zero;

            for (int i = 0; i < nVec; i += w)
            {
                var vx = new Vector<double>(_delay2N, baseIdx + i);
                var vh = new Vector<double>(_tapsRev, i);
                vAcc += vx * vh;
            }

            for (int lane = 0; lane < Vector<double>.Count; lane++)
                acc += vAcc[lane];

            for (int i = nVec; i < _nTaps; i++)
                acc += _delay2N[baseIdx + i] * _tapsRev[i];
        }
        else
        {
            for (int i = 0; i < _nTaps; i++)
                acc += _delay2N[baseIdx + i] * _tapsRev[i];
        }

        p++;
        if (p == _nTaps) p = 0;
        _pos = p;

        return acc;
    }

    public void Filter(ReadOnlySpan<double> x, Span<double> y)
    {
        if (y.Length < x.Length) throw new ArgumentException("Output span is too small.", nameof(y));
        for (int i = 0; i < x.Length; i++)
            y[i] = Filter(x[i]);
    }
}
