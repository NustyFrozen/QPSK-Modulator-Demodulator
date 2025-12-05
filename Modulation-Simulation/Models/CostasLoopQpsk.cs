using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Modulation_Simulation.Models;
    public class CostasLoopQpsk
{
    private double phase;      // estimated carrier phase
    private double freq;       // frequency correction
    private readonly double alpha; // loop gains
    private readonly double beta;

    public CostasLoopQpsk(double loopBandwidth = 0.001)
    {
        // Very typical PLL coefficient choice
        beta = 0.25 * loopBandwidth * loopBandwidth;
        alpha = 2 * beta;
    }

    public Complex Process(Complex y)
    {
        // Rotate by negative estimated phase
        Complex z = y * Complex.Exp(new Complex(0, -phase));

        // Costas QPSK phase error
        double e = Math.Sign(z.Real) * z.Imaginary
                 - Math.Sign(z.Imaginary) * z.Real;

        // PLL update
        freq += beta * e;     // integrator
        phase += freq + alpha * e;  // proportional + integrator

        // keep phase in [-pi, pi]
        if (phase > Math.PI) phase -= 2 * Math.PI;
        else if (phase < -Math.PI) phase += 2 * Math.PI;

        return z; // carrier-corrected symbol
    }
}
