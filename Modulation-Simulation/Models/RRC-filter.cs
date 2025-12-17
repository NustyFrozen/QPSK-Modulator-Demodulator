using System;
using System.Linq;
using System.Numerics;

namespace QPSK.Models;

public class RRCFilter
{
    /// <summary>
    /// Generate Root Raised Cosine (RRC) filter coefficients.
    /// spanSymbols: total filter span in symbols (e.g., 6, 8, 10)
    /// beta: rolloff (0 < beta <= 1), typical 0.2–0.35
    /// sampleRate: sampling rate in Hz
    /// SymbolRate: symbol rate in symbols per second
    /// </summary>
    public static double[] generateCoefficents(
        double spanSymbols,
        double beta,
        int sampleRate,
        int SymbolRate)
    {
        // samples per symbol (sps)
        double spsExact = (double)sampleRate / SymbolRate;
        int sps = (int)Math.Round(spsExact);

        int spanSymInt = (int)Math.Round(spanSymbols);
        int taps = spanSymInt * sps + 1;

        double[] h = new double[taps];

        int mid = (taps - 1) / 2;
        double pi = Math.PI;
        double eps = 1e-8;

        for (int n = 0; n < taps; n++)
        {
            // time in symbol periods (T = 1)
            double t = (n - mid) / (double)sps;
            double val;

            if (Math.Abs(t) < eps)
            {
                // t = 0
                val = 1.0 + beta * (4.0 / pi - 1.0);
            }
            else if (Math.Abs(Math.Abs(t) - 1.0 / (4.0 * beta)) < eps)
            {
                // t = ± 1/(4β)
                val = (beta / Math.Sqrt(2.0)) *
                      ((1.0 + 2.0 / pi) * Math.Sin(pi / (4.0 * beta)) +
                       (1.0 - 2.0 / pi) * Math.Cos(pi / (4.0 * beta)));
            }
            else
            {
                // general case
                double num = Math.Sin(pi * t * (1.0 - beta)) +
                             4.0 * beta * t * Math.Cos(pi * t * (1.0 + beta));
                double den = pi * t * (1.0 - Math.Pow(4.0 * beta * t, 2.0));
                val = num / den;
            }

            h[n] = val;
        }

        // normalize to unit energy: sum |h|^2 = 1
        double energy = 0.0;
        for (int i = 0; i < taps; i++)
            energy += h[i] * h[i];

        double norm = Math.Sqrt(energy);
        for (int i = 0; i < taps; i++)
            h[i] /= norm;

        return h;
    }
}
