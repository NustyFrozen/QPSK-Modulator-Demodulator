using System.Numerics;

namespace Modulation_Simulation.Models;

public class FIRFilter
    {
        readonly Complex[] taps;
        readonly Complex[] delay;
        int index;

        public FIRFilter(Complex[] taps)
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
