﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Management;
using System.ComponentModel;
using System.Runtime.InteropServices;

//ToDo: Implemnt IDisposable to stop listen from output.

namespace CodeArtEng.Diagnostics
{
    /// <summary>
    /// Execution result returned from ProcessExecutor
    /// </summary>
    public class ProcessResult : EventArgs
    {
        /// <summary>
        /// Standard output lines.
        /// </summary>
        public string[] Output { get; set; }
        /// <summary>
        /// Exit code return from process.
        /// </summary>
        public int ExitCode { get; set; }
        /// <summary>
        /// This flag is set if any stderr message is received.
        /// </summary>
        public bool ErrorDetected { get; set; }

        public ProcessResult() { Output = new string[] { }; }
    }

    /// <summary>
    /// Execute Console based application without showing the console window.
    /// </summary>
    public class ProcessExecutor
    {
        ProcessStartInfo processInfo;
        List<string> outputData;
        private bool errorDetected;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="name">Instance name</param>
        public ProcessExecutor(string name = "Unnamed")
        {
            Name = name;
            TraceLogEnabled = false;
            ShowConsoleWindow = false;
            DomainName = System.Environment.UserDomainName;
        }

        /// <summary>
        /// Instance Name
        /// </summary>
        [Category("General")]
        [Description("Name assigned to this instance")]
        public string Name { get; set; }

        /// <summary>
        /// Application EXE - Full path.
        /// </summary>
        [Category("General")]
        [Description("Application EXE - Full Path")]
        public string Application { get; set; }

        /// <summary>
        /// Arguments that passed to application.
        /// </summary>
        [Category("General")]
        [Description("Arguments that passed to application")]
        public string Arguments { get; set; }

        /// <summary>
        /// Root Directory
        /// </summary>
        [Category("General")]
        [Description("Root Directory")]
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// Enable / Disable Trace Log
        /// </summary>
        [Category("General")]
        [Description("Enable / Disable Trace Log")]
        [DefaultValue(false)]
        public bool TraceLogEnabled { get; set; }

        /// <summary>
        /// Show / Hide Console Window.
        /// </summary>
        [Category("Display")]
        [Description("Show console window.")]
        [DefaultValue(false)]
        public bool ShowConsoleWindow { get; set; }

        /// <summary>
        /// Get Process Handler
        /// </summary>
        [Browsable(false)]
        public Process ProcessHandler { get; private set; }

        /// <summary>
        /// Gets or sets a value that identifies the domain to use when starting the process. Default value is current domain.
        /// </summary>
        [Category("User Credential")]
        [Description("Gets or sets a value that identifies the domain to use when starting the process. Default value is current domain.")]
        public string DomainName { get; set; }

        /// <summary>
        /// User name to execute process as different user.
        /// </summary>
        [Category("User Credential")]
        [Description("Assign UserName to execute process as different user. Leave empty to run from current user.")]
        public string UserName { get; set; }

        /// <summary>
        /// Password for assigned user.
        /// </summary>
        [Category("User Credential")]
        [PasswordPropertyText(true)]
        [Description("Password for assigned user to execute process as different user.")]
        public string Password
        {
            get
            {
                IntPtr valuePtr = IntPtr.Zero;
                try
                {
                    valuePtr = Marshal.SecureStringToGlobalAllocUnicode(ssPwd);
                    return Marshal.PtrToStringUni(valuePtr);
                }
                finally
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);
                }
            }
            set
            {
                ssPwd = new System.Security.SecureString();
                foreach (char x in value) ssPwd.AppendChar(x);
            }
        }
        private System.Security.SecureString ssPwd = new System.Security.SecureString();

        private bool RedirectToFile;
        private string LogFile;

        /// <summary>
        /// Redirect standard output and standrad error to file. By default, standard output and
        /// standard error are stored in ProcessResult which returned at the end of Execute()
        /// </summary>
        /// <param name="logFile">Target file to store output logs.</param>
        public void RedirectOutputToFile(string logFile)
        {
            LogFile = logFile;
            RedirectToFile = true;
        }

        /// <summary>
        /// Execute process and return result in the form of ProcessResult.
        /// </summary>
        /// <remarks>No exception is expected from this application. Execution error will be reflected in ExitCode.</remarks>
        /// <param name="waitForExit">Set to true to wait until process exit, else return once process started.</param>
        /// <returns>Execution result as <see cref="ProcessResult"/></returns>
        public ProcessResult Execute(bool waitForExit = true)
        {
            ProcessResult result = new ProcessResult();
            outputData = new List<string>();
            errorDetected = false;
            try
            {
                processInfo = new ProcessStartInfo();
                processInfo.FileName = Application;
                if (Application.Contains(" ")) processInfo.FileName = "\"" + processInfo.FileName + "\"";
                processInfo.WorkingDirectory = WorkingDirectory;
                processInfo.Arguments = Arguments;
                processInfo.CreateNoWindow = !ShowConsoleWindow;
                processInfo.UseShellExecute = ShowConsoleWindow;
                processInfo.RedirectStandardOutput = !ShowConsoleWindow;
                processInfo.RedirectStandardError = !ShowConsoleWindow;
                if (!string.IsNullOrEmpty(UserName))
                {
                    processInfo.UserName = UserName;
                    processInfo.Password = ssPwd;
                    processInfo.Domain = DomainName;
                }
                Trace.WriteLine(Name + ": Executing: " + processInfo.FileName + " " + processInfo.Arguments);
                if (ProcessHandler != null) DisposeProcessHandler();
                ProcessHandler = Process.Start(processInfo);
                if (!ShowConsoleWindow)
                {
                    ProcessHandler.OutputDataReceived += new DataReceivedEventHandler(process_OutputDataReceived);
                    ProcessHandler.ErrorDataReceived += new DataReceivedEventHandler(process_ErrorDataReceived);
                    ProcessHandler.BeginOutputReadLine();
                    ProcessHandler.BeginErrorReadLine();
                }
                if (!waitForExit)
                {
                    ProcessHandler.EnableRaisingEvents = true;
                    if (ProcessHandler.EnableRaisingEvents) ProcessHandler.Exited += new EventHandler(process_Exited);
                    return null;
                }

                ProcessHandler.WaitForExit();
                result.ExitCode = ProcessHandler.ExitCode;
            }
            catch (Exception ex)
            {
                result.Output = new string[] { ex.ToString() };
                result.ErrorDetected = true;
                result.ExitCode = -999;
                return result;
            }
            ProcessHandler.OutputDataReceived -= process_OutputDataReceived;
            ProcessHandler.ErrorDataReceived -= process_ErrorDataReceived;

            result.ErrorDetected = errorDetected;
            result.Output = outputData.ToArray();
            return result;
        }

        /// <summary>
        /// Check if the specific tool exists.
        /// </summary>
        /// <returns></returns>
        public void Validate()
        {
            if (!System.IO.File.Exists(Application))
            {
                throw new System.IO.FileNotFoundException(Application);
            }
        }

        /// <summary>
        /// Get tools version.
        /// </summary>
        /// <param name="command">Command to retrieve version number, e.g. "/?", "--version"</param>
        /// <returns>Version information, null if not success.</returns>
        protected virtual Version Version(string command)
        {
            if (string.IsNullOrEmpty(command)) return null;
            Arguments = command;
            ProcessResult procResult = Execute();
            if (procResult.ExitCode != 0) return null;

            for (int x = 0; x < procResult.Output.Length; x++)
            {
                //Get First non-empty line
                if (string.IsNullOrEmpty(procResult.Output[x])) continue;

                string firstLine = procResult.Output[x];
                string[] firstLineParams = firstLine.Split(' ');

                Version verInfo;
                int tVal;
                for (int n = 0; n < firstLineParams.Length; n++)
                {
                    //Find parameter which begin with number
                    if (int.TryParse(firstLineParams[n][0].ToString(), out tVal))
                    {
                        string verString = firstLineParams[n];

                        //HACK: Trim end character "." for Visual Studio (devenv)
                        while (!int.TryParse(verString.Last().ToString(), out tVal))
                        {
                            verString = verString.Substring(0, verString.Length - 1);
                        }
                        //---- end HACK

                        try
                        {
                            verInfo = new Version(verString);
                            return verInfo;
                        }
                        catch { continue; }
                    }
                }
            }
            return null; //No output returned from command.
        }

        /// <summary>
        /// Stop the current process and all its child process.
        /// </summary>
        public void Abort()
        {
            if (ProcessHandler == null) return;
            if (ProcessHandler.HasExited) return;
            KillProcessAndChildren(ProcessHandler.Id);
            DisposeProcessHandler();
        }

        #region [ Private Functions ]

        private void DisposeProcessHandler()
        {
            ProcessHandler.Exited -= process_Exited;
            ProcessHandler.OutputDataReceived -= process_OutputDataReceived;
            ProcessHandler.ErrorDataReceived -= process_ErrorDataReceived;
            ProcessHandler.Dispose();
            ProcessHandler = null;
        }

        private static void KillProcessAndChildren(int pid)
        {
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("Select * From Win32_Process Where ParentProcessID=" + pid);
            ManagementObjectCollection moc = searcher.Get();
            foreach (ManagementObject mo in moc)
            {
                KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));
            }
            try
            {
                Process proc = Process.GetProcessById(pid);
                proc.Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
        }
        private void process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            //ToDo: Review exception : "Source array was not long enough. Check srcIndex and length, and the array's lower bounds."
            if (e.Data != null)
            {
                string data = e.Data;
                errorDetected = true;
                if (RedirectToFile) System.IO.File.AppendAllText(LogFile, data + "\r\n");
                else outputData.Add(data);
                if (TraceLogEnabled) Trace.WriteLine(Name + ": " + data);
            }
        }
        private void process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            //ToDo: Review exception : "Source array was not long enough. Check srcIndex and length, and the array's lower bounds."
            if (e.Data != null)
            {
                string data = e.Data;
                if (RedirectToFile) System.IO.File.AppendAllText(LogFile, data + "\r\n");
                else outputData.Add(data);
                if (TraceLogEnabled) Trace.WriteLine(Name + ": " + data);
            }
        }

        #endregion

        #region [Events]

        /// <summary>
        /// Event raised when process is completed.
        /// </summary>
        public event EventHandler<ProcessResult> HasExited;
        private void process_Exited(object sender, EventArgs e)
        {
            EventHandler<ProcessResult> handler = HasExited;
            if (handler != null)
            {
                ProcessResult procResult =
                  new ProcessResult()
                  {
                      ExitCode = ProcessHandler.ExitCode,
                      ErrorDetected = errorDetected
                  };

                handler(this, procResult);
                DisposeProcessHandler();
            }
        }

        #endregion
    }
}
