using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace QPSK.Models;

/// <summary>
/// 
/// </summary>
/// <param name="sampleRate">sample rate</param>
/// <param name="loopBandwith">cutoff LPF of the error</param>
/// <param name="FIRTaps">how strong you want the smoothing Small smoothing 11-31, strong 51-201  must be odd number</param>
public class CostasLoopQpsk
{
    readonly double sampleRate;
    readonly double loopBandwidthHz;
    readonly double damping;

    double alpha, beta;   // loop gains from BW + damping
    double theta;         // NCO phase [rad]
    double freq;          // NCO frequency state [rad/sample]

    public CostasLoopQpsk(
        double sampleRate,
        double loopBandwidthHz,
        double damping = 0.707)
    {
        this.sampleRate = sampleRate;
        this.loopBandwidthHz = loopBandwidthHz;
        this.damping = damping;

        // 1) normalize loop BW to rad/sample
        double bw = 2.0 * Math.PI * loopBandwidthHz / sampleRate;

        // 2) compute alpha, beta (Tom Rondeau / GNU Radio style)
        double d = 1.0 + 2.0 * damping * bw + bw * bw;
        alpha = (4.0 * damping * bw) / d;
        beta = (4.0 * bw * bw) / d;
    }

    // Hard limiter for QPSK
    public static Complex GetSign(Complex sample) =>
        new Complex(sample.Real > 0 ? 1.0 : -1.0,
                    sample.Imaginary > 0 ? 1.0 : -1.0);

    public Complex Process(Complex sample)
    {
        // NCO
        var nco = Complex.FromPolarCoordinates(1.0, -theta);
        var mixed = sample * nco;

        // QPSK decision
        var est = GetSign(mixed);

        // QPSK Costas phase detector
        double phaseError = est.Real * mixed.Imaginary
                          - est.Imaginary * mixed.Real;

        // 2nd order loop:
        // freq[n+1]  = freq[n]  + beta  * e[n]
        // theta[n+1] = theta[n] + freq[n+1] + alpha * e[n]
        freq += beta * phaseError;
        theta += freq + alpha * phaseError;

        // keep theta bounded (optional but good practice)
        if (theta > Math.PI)
            theta -= 2.0 * Math.PI;
        else if (theta < -Math.PI)
            theta += 2.0 * Math.PI;

        return mixed;
    }
}
