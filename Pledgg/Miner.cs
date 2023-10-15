
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace Pledgg
{ 
    class Miner
    {
        // General Variables
        public volatile bool done = false;
        public volatile uint FinalNonce = 0;
           
        Thread[] threads;

        public Miner(int? optThreadCount = null)
        {
            int threadCount = optThreadCount ?? Environment.ProcessorCount / 2;
            if (threadCount > Environment.ProcessorCount) {
                threadCount = Environment.ProcessorCount;
            }
            threads = new Thread[threadCount];
        }
       
        public void Mine(object sender, DoWorkEventArgs e)
        {
            Trace.WriteLine("New Miner. ID = " + Thread.CurrentThread.ManagedThreadId);
            Trace.WriteLine($"Starting {threads.Length} threads for new block...");

            Job ThisJob = (Job)e.Argument;
            
            // Gets the data to hash and the target from the work
        
            byte[] databyte = Utilities.ReverseByteArrayByFours(Utilities.HexStringToByteArray(ThisJob.Data));
            byte[] targetbyte = Utilities.HexStringToByteArray(ThisJob.Target);

            Trace.Write($"Job Data: {databyte.LongLength.ToString()}");
            Trace.WriteLine("");

            done = false;
            FinalNonce = 0;

            // Spin up background threads to do the hashing
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() => doScrypt(databyte, targetbyte, (uint)i, (uint)threads.Length));
                threads[i].IsBackground = false;
                threads[i].Priority = ThreadPriority.Normal;//.Lowest; // For debugging
                threads[i].Start();
            }

            // Block until all the threads finish
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i].Join();
            }

            Trace.WriteLine($"Miner ID {Thread.CurrentThread.ManagedThreadId} outputed {FinalNonce}");
            // Fill in the answer if work done
            if (FinalNonce != 0)
            {
                ThisJob.Answer = FinalNonce;
                e.Result = ThisJob;
            }
            else
                e.Result = null;

            Trace.WriteLine($"Miner ID {Thread.CurrentThread.ManagedThreadId} finished");
        }

        // Reference: https://github.com/replicon/Replicon.Cryptography.SCrypt
        public void doScrypt(byte[] Tempdata, byte[] Target, uint Nonce, uint Increment)
        {
            double Hashcount = 0;

            byte[] Databyte = new byte[80];
            Array.Copy(Tempdata, 0, Databyte, 0, 76);

            Trace.WriteLine($"New thread,Target: {Target.Length.ToString()}");
            
            DateTime StartTime = DateTime.Now;
            
            try
            {
                byte[] ScryptResult = new byte[32];

                // Loop until done is set or we meet the target
                while (!done)
                {
                    Databyte[76] = (byte)(Nonce >> 0);
                    Databyte[77] = (byte)(Nonce >> 8);
                    Databyte[78] = (byte)(Nonce >> 16);
                    Databyte[79] = (byte)(Nonce >> 24);

                    ScryptResult = Replicon.Cryptography.SCrypt.SCrypt.DeriveKey(Databyte, Databyte, 1024, 1, 1, 32);


                    Hashcount++;
                    if (meetsTarget(ScryptResult, Target))  // Did we meet the target?
                    {
                        Trace.WriteLine($"Target Met: {ScryptResult.Length} {Target.Length}");
                        if (!done) 
                            FinalNonce = Nonce; 
                        done = true; 
                        break;
                    }
                    else
                        Nonce += Increment; // If not, increment the nonce and try again
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Exeption: {ex}");
                FinalNonce = 0;
            }

            double Elapsedtime = (DateTime.Now - StartTime).TotalMilliseconds;
            Trace.WriteLine($"Thread finished - {Hashcount} hashes in {Elapsedtime} ms. Speed: {Hashcount/Elapsedtime} kHash/s");
        }

        public bool meetsTarget(byte[] hash, byte[] target)
        {
            for (int i = hash.Length - 1; i >= 0; i--)
            {
                if ((hash[i] & 0xff) > (target[i] & 0xff))
                    return false;
                if ((hash[i] & 0xff) < (target[i] & 0xff))
                    return true;
            }
            return false;
        }
    }

}
