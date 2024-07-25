using System;
using System.Diagnostics;

namespace FluentDL.Services
{
    internal class TerminalSubprocess
    {
        public TerminalSubprocess()
        {
        }

        public static string GetRunCommandSync(string command, string? directory)
        {
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            if (System.IO.Directory.Exists(directory))
            {
                process.StartInfo.WorkingDirectory = directory;
            }
            else // Use the directory of the user profile
            {
                process.StartInfo.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

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

        public static void RunCommandSync(string command, string? directory)
        {
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            if (System.IO.Directory.Exists(directory))
            {
                process.StartInfo.WorkingDirectory = directory;
            }
            else // Use the directory of the user profile
            {
                process.StartInfo.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            process.StandardInput.WriteLine(command);
            process.StandardInput.Flush();
            process.StandardInput.Close();
            process.WaitForExit();
        }
    }
}