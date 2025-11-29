using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Modulation_Simulation.Models;
public static class Band_Edge_Filter
{
    public static double[] GenerateCoefficents(double samps_per_sym, double filter_size,double bandwidth, double rolloff)
    {
        double f_edge = ((1 + RRCbeta) * symbolRate) / 2;
        var window = Window.Hamming(taps);
    }
    private static float sinc(float x)
    {
        
        if (x == 0) return 1;
        return (float)(Math.Sin(Math.PI * x) / (Math.PI * x));
    }
}

