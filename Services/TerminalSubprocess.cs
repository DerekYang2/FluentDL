using System;
using System.Diagnostics;

namespace FluentDL.Services
{
    internal class TerminalSubprocess
    {
        private Process cmd;

        public TerminalSubprocess()
        {
            cmd = new Process();
            cmd.StartInfo.FileName = "cmd.exe";
            cmd.StartInfo.RedirectStandardInput = true;
            cmd.StartInfo.RedirectStandardOutput = true;
            cmd.StartInfo.CreateNoWindow = true;
            cmd.StartInfo.UseShellExecute = false;
            cmd.Start();
        }

        public void RunCommand(string command)
        {
            /*
            Task.Run(() =>
            {
                Debug.WriteLine("Running command: " + command);
                cmd.StandardInput.WriteLine(command);
                cmd.StandardInput.Flush();
            });*/
            Debug.WriteLine("Running command: " + command);
            cmd.StandardInput.WriteLine(command);
            cmd.StandardInput.Flush();
        }

        public static string GetRunCommandSync(string command)
        {
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            process.StandardInput.WriteLine(command);
            process.StandardInput.Flush();
            process.StandardInput.Close();
            process.WaitForExit();
            return process.StandardOutput.ReadToEnd();
        }

        public static void RunCommandSync(string command)
        {
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            process.StandardInput.WriteLine(command);
            process.StandardInput.Flush();
            process.StandardInput.Close();
            process.WaitForExit();
        }

        // Dispose method to clean up resources
        public void Dispose()
        {
            cmd.StandardInput.WriteLine("exit"); // Gracefully exit the process
            cmd.WaitForExit();
            cmd.Close();
        }
    }
}