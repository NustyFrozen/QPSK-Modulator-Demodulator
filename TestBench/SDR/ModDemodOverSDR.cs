using QPSK.Models;
using NetMQ;
using NetMQ.Sockets;
using Pothosware.SoapySDR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using QPSK;
namespace TestBench.SDR
{
    public class ModDemodOverSDR
    {
         static void SetupSoapyEnvironment()
        {
            var currentPath = Directory.GetCurrentDirectory();
            var soapyPath = Path.Combine(currentPath, @"SDR\SoapySDR");
            var libsPath = Path.Combine(soapyPath, @"Libs");
            Environment.SetEnvironmentVariable("SOAPY_SDR_PLUGIN_PATH",
                Path.Combine(currentPath, @"SDR\SoapySDR\root\SoapySDR\lib\SoapySDR\modules0.8-3\"));
            Environment.SetEnvironmentVariable("SOAPY_SDR_ROOT", Path.Combine(currentPath, @"SDR\SoapySDR\root\SoapySDR"));
            Environment.SetEnvironmentVariable("PATH",
                $"{Environment.GetEnvironmentVariable("PATH")};{soapyPath};{libsPath}");
        }

        public static unsafe void runTest(PublisherSocket pub)
        {
            SetupSoapyEnvironment();
            
            //testing on UHD 4.8 usrp b205
            Device Device = new Device("driver=uhd,send_buff_size=4096,recv_buff_size=4096");
            int sampleRate = 1_500_000;
            var frequency = 935e6;
            var SymbolRate = sampleRate / 2;
            const float RRCAlpha = .1f,symbolLoopBandwith= 0.000001f, costas=130;
            const int rrcSpan = 8;
             string TSC =
            "11001010011101100100100110101100" +
            "01110100111001011010001101101001";
            TSC += TSC ;
            string prefix_start = "MESSAGE_START", prefix_end = "MESSAGE_STOP";
            QPSKModulator mod = new QPSKModulator(sampleRate, SymbolRate, RRCAlpha,rrcSpan,tsc: TSC);
            QPSKDeModulator demod = new QPSKDeModulator(sampleRate, SymbolRate, RRCAlpha, rrcSpan,CostasLoopBandwith:costas,SymbolSyncBandwith:symbolLoopBandwith);
            QPSKDeModulator demod_data = new QPSKDeModulator(sampleRate, SymbolRate, RRCAlpha, rrcSpan, CostasLoopBandwith: costas, SymbolSyncBandwith: symbolLoopBandwith);


            Device.SetSampleRate(Direction.Rx, 0, sampleRate);
            Device.SetSampleRate(Direction.Tx, 0, sampleRate);
            Device.SetFrequency(Direction.Rx, 0,
                frequency);
            Device.SetFrequency(Direction.Tx, 0,
                frequency);
            Device.SetGain(Direction.Tx, 0, 46);
            // Device.SetGainMode(Direction.Rx, 0, true);
           
            Device.SetGain(Direction.Rx, 0,28);
            // Device.SetGain(Direction.Rx, 0,68); //with antenna
            var rxStream = Device.SetupRxStream(StreamFormat.ComplexFloat32,
                new[] { (uint)0 }, "");
            var txStream = Device.SetupTxStream(StreamFormat.ComplexFloat32,
                new[] { (uint)0 }, "");
            
            var rxMtu = rxStream.MTU;
            var txMtu = txStream.MTU;
            var tx_results = new StreamResult();
            var rx_results = new StreamResult();

            var rxFloatBuffer = new float[rxMtu *2];
            int sps = sampleRate / SymbolRate;


            int rrcLen = RRCFilter.generateCoefficents(rrcSpan, RRCAlpha, sampleRate, SymbolRate).Length;
            int extra = 2 * (rrcLen - 1);        // 2*(M-1) from the math above

            int desiredComplex = (int)txMtu;          // want this many complex samples
            int upsampledLenWithoutFilter = desiredComplex - extra;
            int nSym = upsampledLenWithoutFilter / sps;
            int nBits = nSym * 2;

            var data = string.Empty;
            Enumerable.Range(0, 1000).Select(x => data += "Hello World ").ToArray();
            var modulatedQPSKSignal = mod.ModulateTextUtf8(data, prefix_start,prefix_end);
            Console.WriteLine($"Sending (size {(data.Length + prefix_end.Length + prefix_start.Length)}Bytes per frame): {new string(data.Take(10).ToArray())}..."); //single char is 1 byte
            var rxBufferHandle = GCHandle.Alloc(rxFloatBuffer, GCHandleType.Pinned);
            var txBufferHandle = GCHandle.Alloc(modulatedQPSKSignal, GCHandleType.Pinned);

            var sw = new Stopwatch();
            var keepTransmission = true;
            var transmitThread = new Thread(() =>
            {
                txStream.Activate();
                fixed (float* bufferPtr = modulatedQPSKSignal)
                {
                    while (keepTransmission)
                    {
                        var errorCode = txStream.Write((nint)bufferPtr, (uint)modulatedQPSKSignal.Length/2, StreamFlags.None, 0, 10_000_000,
                            out tx_results);
                        if (errorCode is not Pothosware.SoapySDR.ErrorCode.None || tx_results is null)
                        {
                            Console.WriteLine($"TXSTREAM -->{errorCode}");
                        }
                    }
                }
            })
            { Priority = ThreadPriority.Highest };
            transmitThread.Start();

            var samples = new List<float>();
            var totalSamples = 0;

            var readingThread = new Thread(() =>
            {
                Stopwatch sw = new Stopwatch();
                var rx_floatBuffer_asspan = rxFloatBuffer.AsSpan();
                rxStream.Activate();
                fixed (float* bufferPtr = rxFloatBuffer)
                {
                    while (keepTransmission)
                    {

                        sw.Restart();
                        var errorCode = rxStream.Read((nint)bufferPtr, (uint)rxMtu, 10_000_000, out rx_results);

                        if (errorCode is not Pothosware.SoapySDR.ErrorCode.None || rx_results is null)
                        {
                            Console.WriteLine($"RXSTREAM -->{errorCode}");
                            continue;
                        }

                        var IncomingSamples = rx_floatBuffer_asspan.Slice(0, ((int)rx_results.NumSamples) * 2);
                        var responseMessage = demod_data.DeModulateTextUtf8(IncomingSamples, prefix_start,prefix_end);
                        if (responseMessage != string.Empty)
                        {
                           // Console.WriteLine($"Got Response--> {responseMessage}");
                            File.AppendAllText("message.txt", responseMessage);
                        }
                        var symbolsResults = demod.deModulateConstellation(IncomingSamples);
                        
                        byte[] frame = new byte[IncomingSamples.Length * 4];
                        Buffer.BlockCopy(IncomingSamples.ToArray(), 0, frame, 0, frame.Length);

                        byte[] frame2 = new byte[symbolsResults.Length * 4];
                        Buffer.BlockCopy(symbolsResults, 0, frame2, 0, frame2.Length);


                        // Send single ZMQ frame without topic
                        pub.SendMoreFrame("baseband").SendFrame(frame);
                        pub.SendMoreFrame("baseband_demodulated").SendFrame(frame2);

                        sw.Stop();
                        double loopTimeSec = sw.Elapsed.TotalSeconds;

                        // 4. Compute remaining time until the "ideal" next send
                        double sleepSec = (rx_results.NumSamples / sampleRate) - loopTimeSec;
                        if (sleepSec > 0)
                        {
                            int sleepMs = (int)(sleepSec * 1000.0);
                            if (sleepMs > 0)
                            {
                                Thread.Sleep(sleepMs);
                            }
                            double remainderSec = sleepSec - sleepMs / 1000.0;
                            if (remainderSec > 0)
                            {
                                // Short spin-wait for a tiny extra bit
                                var spin = new SpinWait();
                                var targetTicks = Stopwatch.GetTimestamp() +
                                                  (long)(remainderSec * Stopwatch.Frequency);
                                while (Stopwatch.GetTimestamp() < targetTicks)
                                    spin.SpinOnce();
                            }
                        }
                    }
                }
            }
            )
            { Priority = ThreadPriority.Highest};
            readingThread.Start();
           
            Console.ReadKey();
            keepTransmission = false;
            Thread.Sleep(200);
            rxStream.Close();
            txStream.Close();
           
        }
        static void beginRX()
        {

        }
        static void beginTX()
        {

        }
    }
}
