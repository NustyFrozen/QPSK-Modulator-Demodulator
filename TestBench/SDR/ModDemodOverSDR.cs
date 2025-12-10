using Modulation_Simulation.Models;
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
            Device Device = new Device("driver=uhd");
            int sampleRate = 1_000_000;
            var frequency = 915e6;
            var SymbolRate = sampleRate / 40;
            const float RRCAlpha = .9f;
            QPSKModulator mod = new QPSKModulator(sampleRate, SymbolRate, RRCAlpha);
            QPSKDeModulator demod = new QPSKDeModulator(sampleRate, SymbolRate, RRCAlpha);
            var rand = new Random();
            
            Device.SetSampleRate(Direction.Rx, 0, sampleRate);
            Device.SetSampleRate(Direction.Tx, 0, sampleRate);
            Device.SetFrequency(Direction.Rx, 0,
                frequency);
            Device.SetFrequency(Direction.Tx, 0,
                frequency);
            Device.SetGain(Direction.Tx, 0, 70);
            Device.SetGainMode(Direction.Rx, 0, true);
            // Device.SetGain(Direction.Rx, 0,60);
            var rxStream = Device.SetupRxStream(StreamFormat.ComplexFloat32,
                new[] { (uint)0 }, "");
            var txStream = Device.SetupTxStream(StreamFormat.ComplexFloat32,
                new[] { (uint)0 }, "");
            rxStream.Activate();
            txStream.Activate();
            var rxMtu = rxStream.MTU;
            var txMtu = txStream.MTU;
            var results = new StreamResult();
            var rxFloatBuffer = new float[rxMtu * 2];
            string data = string.Empty;
            for (int i = 0; i < Convert.ToInt32(txMtu) * SymbolRate / sampleRate; i++)
            {
                data += Convert.ToChar(rand.Next(0, 2));
            }
            var modulatedQPSKSignal = mod.Modulate(data).toFloatInterleaved();
           
            var rxBufferHandle = GCHandle.Alloc(rxFloatBuffer, GCHandleType.Pinned);
            var txBufferHandle = GCHandle.Alloc(modulatedQPSKSignal, GCHandleType.Pinned);

            var sw = new Stopwatch();
            var keepTransmission = true;
            var transmitThread = new Thread(() =>
            {
                fixed (float* bufferPtr = modulatedQPSKSignal)
                {
                    while (keepTransmission)
                    {
                        var errorCode = txStream.Write((nint)bufferPtr, (uint)txMtu, StreamFlags.None, 0, 10_000_000,
                            out results);
                        if (errorCode is not Pothosware.SoapySDR.ErrorCode.None || results is null)
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
                while (keepTransmission)
                {
                    fixed (float* bufferPtr = rxFloatBuffer)
                    {
                        sw.Restart();
                        var errorCode = rxStream.Read((nint)bufferPtr, (uint)rxMtu, 10_000_000, out results);

                        if (errorCode is not Pothosware.SoapySDR.ErrorCode.None || results is null)
                        {
                            Console.WriteLine($"RXSTREAM -->{errorCode}");
                            continue;
                        }
                    }
                    Complex[] symbolsResults;
                            symbolsResults = demod.deModulateConstellation(rx_floatBuffer_asspan.Slice(0, (int)results.NumSamples).ToArray().toComplex());
                        
                        byte[] frame = new byte[symbolsResults.Length * 2 * 4];
                        Buffer.BlockCopy(symbolsResults.toFloatInterleaved(), 0, frame, 0, frame.Length);

                        // Send single ZMQ frame without topic
                        pub.SendMoreFrame("baseband").SendFrame(frame);
                    sw.Stop();
                    double loopTimeSec = sw.Elapsed.TotalSeconds;

                    // 4. Compute remaining time until the "ideal" next send
                    double sleepSec = (results.NumSamples/sampleRate) - loopTimeSec;
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
            )
            { Priority = ThreadPriority.Highest};
            readingThread.Start();
            Thread demodulationThread = new Thread(() =>
            {
                Complex[] results;
                while (keepTransmission)
                {
                    lock (samples)
                    {
                        results = demod.deModulateConstellation(samples.ToArray().toComplex());
                        samples.Clear();
                    }
                    byte[] frame = new byte[results.Length * 2 * 4];
                    Buffer.BlockCopy(results.toFloatInterleaved(), 0, frame, 0, frame.Length);

                    // Send single ZMQ frame without topic
                    pub.SendMoreFrame("baseband").SendFrame(frame);

                }
            })
            { Priority = ThreadPriority.Highest };
            demodulationThread.Start();
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
