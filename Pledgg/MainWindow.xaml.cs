using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using System.Timers;
using System.Diagnostics;

namespace Pledgg
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static Miner CoinMiner;
        private static int CurrentDifficulty;
        private static Queue<Job> IncomingJobs = new Queue<Job>();
        private static Stratum stratum;
        private static BackgroundWorker worker;
        private static int SharesSubmitted = 0;
        private static int SharesAccepted = 0;
        private static string Server = "";
        private static int Port = 0;
        private static string Username = "";
        private static string Password = "";

        private static System.Timers.Timer KeepaliveTimer;

        private static void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            Trace.WriteLine("Keepalive - ");
            stratum.SendAUTHORIZE();
        }

        public MainWindow()
        {
            InitializeComponent();
            Start();
        }

        void Start()
        {

            Server = "ltc.f2pool.com";
            Port = 3335;
            Username = "adware.001";
            Password = "21235365876986800";


            Trace.WriteLine($"Connecting to '{Server}' on port '{Port}' with username '{Username}' and password '{Password}'");
            Trace.WriteLine("");

            CoinMiner = new Miner(null);
            stratum = new Stratum();

            // Workaround for pools that keep disconnecting if no work is submitted in a certain time period. Send regular mining.authorize commands to keep the connection open
            KeepaliveTimer = new System.Timers.Timer(45000);
            KeepaliveTimer.Elapsed += new ElapsedEventHandler(OnTimedEvent);
            KeepaliveTimer.Start();

            // Set up event handlers
            stratum.GotResponse += stratum_GotResponse;
            stratum.GotSetDifficulty += stratum_GotSetDifficulty;
            stratum.GotNotify += stratum_GotNotify;

            // Connect to the server
            stratum.ConnectToServer(Server, Port, Username, Password);

            // Start mining!!
            StartCoinMiner();

        }

        static void StartCoinMiner()
        {
            // Wait for a new job to appear in the queue
            while (IncomingJobs.Count == 0)
                Thread.Sleep(500);

            // Get the job
            Job ThisJob = IncomingJobs.Dequeue();

            if (ThisJob.CleanJobs)
                stratum.ExtraNonce2 = 0;

            // Increment ExtraNonce2
            stratum.ExtraNonce2++;

            // Calculate MerkleRoot and Target
            string MerkleRoot = Utilities.GenerateMerkleRoot(ThisJob.Coinb1, ThisJob.Coinb2, stratum.ExtraNonce1, stratum.ExtraNonce2.ToString("x8"), ThisJob.MerkleNumbers);
            string Target = Utilities.GenerateTarget(CurrentDifficulty);

            // Update the inputs on this job
            ThisJob.Target = Target;
            ThisJob.Data = ThisJob.Version + ThisJob.PreviousHash + MerkleRoot + ThisJob.NetworkTime + ThisJob.NetworkDifficulty;

            // Start a new miner in the background and pass it the job
            worker = new BackgroundWorker();
            worker.DoWork += new DoWorkEventHandler(CoinMiner.Mine);
            worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(CoinMinerCompleted);
            worker.RunWorkerAsync(ThisJob);
        }

        static void CoinMinerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // If the miner returned a result, submit it
            if (e.Result != null)
            {
                Job ThisJob = (Job)e.Result;
                SharesSubmitted++;

                stratum.SendSUBMIT(ThisJob.JobID, ThisJob.Data.Substring(68 * 2, 8), ThisJob.Answer.ToString("x8"), CurrentDifficulty);
                Trace.WriteLine("CoinFound");
            }

            // Mine again
            StartCoinMiner();
        }

        static void stratum_GotResponse(object sender, StratumEventArgs e)
        {
            StratumResponse Response = (StratumResponse)e.MiningEventArg;

            Trace.Write("Got Response to {0} - ", (string)sender);

            switch ((string)sender)
            {
                case "mining.authorize":
                    if ((bool)Response.result)
                        Trace.WriteLine("Worker authorized");
                    else
                    {
                        Trace.WriteLine("Worker rejected");
                        Environment.Exit(-1);
                    }
                    break;

                case "mining.subscribe":
                    stratum.ExtraNonce1 = (string)((object[])Response.result)[1];
                    Trace.WriteLine("Subscribed. ExtraNonce1 set to " + stratum.ExtraNonce1);
                    break;

                case "mining.submit":
                    if (Response.result != null && (bool)Response.result)
                    {
                        SharesAccepted++;
                        Trace.WriteLine($"Share accepted ({SharesAccepted} of {SharesSubmitted})");
                    }
                    else
                        Trace.WriteLine($"Share rejected. {Response.error[1]}");
                    break;
            }
        }

        static void stratum_GotSetDifficulty(object sender, StratumEventArgs e)
        {
            StratumCommand Command = (StratumCommand)e.MiningEventArg;
            CurrentDifficulty = Convert.ToInt32(Command.parameters[0]);

            Trace.WriteLine("Got Set_Difficulty " + CurrentDifficulty);
        }

        static void stratum_GotNotify(object sender, StratumEventArgs e)
        {
            Job ThisJob = new Job();
            StratumCommand Command = (StratumCommand)e.MiningEventArg;

            ThisJob.JobID = (string)Command.parameters[0];
            ThisJob.PreviousHash = (string)Command.parameters[1];
            ThisJob.Coinb1 = (string)Command.parameters[2];
            ThisJob.Coinb2 = (string)Command.parameters[3];
            Array a = (Array)Command.parameters[4];
            ThisJob.Version = (string)Command.parameters[5];
            ThisJob.NetworkDifficulty = (string)Command.parameters[6];
            ThisJob.NetworkTime = (string)Command.parameters[7];
            ThisJob.CleanJobs = (bool)Command.parameters[8];

            ThisJob.MerkleNumbers = new string[a.Length];

            int i = 0;
            foreach (string s in a)
                ThisJob.MerkleNumbers[i++] = s;

            // Cancel the existing mining threads and clear the queue if CleanJobs = true
            if (ThisJob.CleanJobs)
            {
                Trace.WriteLine("Stratum detected a new block. Stopping old threads.");

                IncomingJobs.Clear();
                CoinMiner.done = true;
            }

            // Add the new job to the queue
            IncomingJobs.Enqueue(ThisJob);
        }
    }

    public class Job
    {
            // Inputs
            public string JobID;
            public string PreviousHash;
            public string Coinb1;
            public string Coinb2;
            public string[] MerkleNumbers;
            public string Version;
            public string NetworkDifficulty;
            public string NetworkTime;
            public bool CleanJobs;

            // Intermediate
            public string Target;
            public string Data;

            // Output
            public uint Answer;
        
    }
}
