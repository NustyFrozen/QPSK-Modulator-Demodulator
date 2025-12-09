using System.Numerics;

namespace Modulation_Simulation.Models;

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
