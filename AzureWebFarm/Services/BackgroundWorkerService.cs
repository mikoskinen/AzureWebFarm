﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AzureWebFarm.Services
{
    public class BackgroundWorkerService : IDisposable
    {
        private readonly string _executablePath;
        private readonly Dictionary<string, List<Executable>> _executables;
        private readonly ExecutableFinder _executableFinder;

        public BackgroundWorkerService(string sitesPath, string executablePath)
        {
            _executablePath = executablePath;
            _executables = new Dictionary<string, List<Executable>>();
            _executableFinder = new ExecutableFinder(sitesPath);
        }

        public void Update(string siteName)
        {
            if (!_executables.ContainsKey(siteName))
                _executables[siteName] = new List<Executable>();

            DisposeSite(siteName);

            _executables[siteName].AddRange(_executableFinder.FindExecutables(siteName));
            
            foreach (var e in _executables[siteName])
            {
                e.Copy(Path.Combine(_executablePath, siteName));
                e.Execute();
            }
        }

        private void DisposeSite(string siteName)
        {
            foreach (var e in _executables[siteName])
            {
                e.Dispose();
                _executables[siteName].Remove(e);
            }
        }

        public void Dispose()
        {
            ForEach(e => e.Dispose());
        }

        public void Ping()
        {
            ForEach(e => e.Ping());
        }

        public void Wait()
        {
            ForEach(e => e.Wait());
        }

        private void ForEach(Action<Executable> action)
        {
            foreach (var e in _executables.Keys.SelectMany(site => _executables[site]))
            {
                action(e);
            }
        }
    }

    public class ExecutableFinder
    {
        private readonly string _sitesPath;

        public ExecutableFinder(string sitesPath)
        {
            _sitesPath = sitesPath;
        }

        public IEnumerable<Executable> FindExecutables(string siteName)
        {
            var subDirs = Directory.EnumerateDirectories(Path.Combine(_sitesPath, siteName, "bin"));
            foreach (var d in subDirs)
            {
                var subDir = d.Split(Path.DirectorySeparatorChar).Last();
                var exe = new Executable(Path.Combine(_sitesPath, siteName, "bin"), subDir);
                
                if (exe.Exists())
                    yield return exe;
            }
        } 
    }

    public class Executable : IDisposable
    {
        private readonly string _basePath;
        private string _executionPath;
        private readonly string _exeName;
        private Process _process;

        public Executable(string basePath, string exeName)
        {
            _basePath = basePath;
            _exeName = exeName;
        }

        public string GetOriginalDirPath()
        {
            return Path.Combine(_basePath, _exeName);
        }

        public string GetOriginalExePath()
        {
            return Path.Combine(GetOriginalDirPath(), string.Format("{0}.exe", _exeName));
        }

        public string GetExecutionDirPath()
        {
            return Path.Combine(_executionPath, _exeName);
        }

        public string GetExecutionExePath()
        {
            return Path.Combine(GetExecutionDirPath(), string.Format("{0}.exe", _exeName));
        }

        public bool Exists()
        {
            return File.Exists(GetOriginalExePath());
        }

        public void Copy(string executionPath)
        {
            if (IsRunning())
                throw new InvalidOperationException("The executable is already running!");

            _executionPath = executionPath;

            if (!Directory.Exists(GetExecutionDirPath()))
                Directory.CreateDirectory(GetExecutionDirPath());

            foreach (var f in Directory.GetFiles(GetOriginalDirPath(), "*.*", SearchOption.AllDirectories))
            {
                File.Copy(f, f.Replace(GetOriginalDirPath(), GetExecutionDirPath()));
            }

            var webConfigPath = Path.Combine(_basePath, "..", "web.config");
            if (File.Exists(webConfigPath))
            {
                File.Copy(webConfigPath, Path.Combine(GetExecutionDirPath(), "web.config"));
            }
        }

        public void Wait()
        {
            if (_process != null)
                _process.WaitForExit(1000);
        }

        public void Execute()
        {
            if (IsRunning())
                throw new InvalidOperationException("The executable is already running!");

            if (string.IsNullOrEmpty(_executionPath))
                throw new InvalidOperationException("You must call .Copy() before .Execute()");

            var startInfo = new ProcessStartInfo(GetExecutionExePath())
            {
                WorkingDirectory = GetExecutionDirPath(),
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            _process = Process.Start(startInfo);
        }

        public bool IsRunning()
        {
            return _process != null && !_process.HasExited;
        }

        public void Ping()
        {
            if (IsRunning())
                return;

            if (_process.ExitCode != 0)
                _process.Start();
        }

        public void Dispose()
        {
            if (_process != null)
            {
                if (IsRunning())
                    _process.Kill();
                _process.Dispose();
                _process = null;
            }

            if (!string.IsNullOrEmpty(_executionPath))
            {
                for (var i = 0; i <= 5; i++)
                {
                    try
                    {
                        Directory.Delete(GetExecutionDirPath(), true);
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(TimeSpan.FromMilliseconds(200));
                        if (i == 5)
                            throw;
                    }
                    catch(UnauthorizedAccessException)
                    {
                        Thread.Sleep(TimeSpan.FromMilliseconds(200));
                        if (i == 5)
                            throw;
                    }
                }
            }
        }
    }
}
