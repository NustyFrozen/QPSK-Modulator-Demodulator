using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace QPSK.Models
{
    internal class IQ_Balancer
    {
        //taken from gnuradio
        private float _avgReal, _avgImg;
        private readonly float _ratio = 1e-05f;
        public void Process(ReadOnlySpan<float> IN,Span<float> OUT)
        {
            // return;
            for (var i = 0; i < IN.Length/2; i+=2)
            {
                _avgReal = _ratio * (IN[i] - _avgReal) + _avgReal;
                _avgImg = _ratio * (IN[i + 1] - _avgImg) + _avgImg;
                OUT[i] = IN[i] - _avgReal;
                OUT[i + 1] = IN[i+1] - _avgImg;
            }
        }
    }
}
