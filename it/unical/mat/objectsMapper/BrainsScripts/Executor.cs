﻿using it.unical.mat.embasp.@base;
using it.unical.mat.embasp.languages.asp;
using it.unical.mat.embasp.platforms.desktop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ThinkEngine.Mappers;
using ThinkEngine.Planning;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ThinkEngine.it.unical.mat.objectsMapper.BrainsScripts
{
    abstract class Executor
    {
        private class MyCallBack : ICallback
        {
            Executor executor;
            public MyCallBack(Executor executor)
            {
                this.executor = executor;
            }
            public void Callback(Output o)
            {
                if (!executor.reason)
                {
                    return;
                }
                if (!o.ErrorsString.Equals(""))
                {
                    UnityEngine.Debug.LogError(o.ErrorsString + " " + o.OutputString);
                }
                Performance.WriteOnFile("solver "+executor.brain.executorName, executor.stopwatch.ElapsedMilliseconds);
                executor.stopwatch.Restart();
                executor.OutputParsing(o);
                if (!executor.brain.maintainInputFile)
                {
                    File.Delete(executor.factsPath);
                }
                executor.solverDone = true;
            }
        }
        int lastSensorIteration = -1;

        protected Brain brain;
        protected string factsPath;
        protected readonly Stopwatch stopwatch = new Stopwatch();
        internal bool reason;
        InputProgram encoding;
        private static object toLock = new object();
        internal static bool _canRead=false;
        internal static bool die = false;
        
        private static int _readingSensors=0;
        internal bool solverDone;
        private static void IncrementReadingSensors()
        {
            lock (toLock)
            {
                while (!_canRead && !die)
                {
                    Monitor.Wait(toLock);
                }
                _readingSensors++;
            }
        }
        private static void DecreaseReadingSensors()
        {
            lock (toLock)
            {
                _readingSensors--;
            }
        }
        internal static void ShutDownAll()
        {
            lock (toLock)
            {
                _canRead = false;
                die = true;
                Monitor.PulseAll(toLock);
            }
        }

        internal static bool Reading()
        {
            return _readingSensors > 0;
        }

        internal static bool CanRead(bool value)
        {
            lock (toLock)
            {
                if(_readingSensors>0 && !value)
                {
                    return false;
                }
                _canRead = value;
                if (_canRead)
                {
                    Monitor.PulseAll(toLock);
                }
                return true;
            }
        }
        internal virtual void Run()
        {
            int facts_id=0;
            if (!Directory.Exists(Path.Combine(Path.GetTempPath(), "ThinkEngineFacts", brain.brainName)))
            {
                if (!Directory.Exists(Path.Combine(Path.GetTempPath(), "ThinkEngineFacts")))
                {
                    Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ThinkEngineFacts"));
                }
                Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ThinkEngineFacts", brain.brainName));
            }
            reason = true;

            encoding = GetProgramInstance();
            List<string> filesToCheck = new List<string>();
            LookForFiles(Utility.StreamingAssetsContent, ref filesToCheck);
            foreach (string fileName in filesToCheck)
            {
                TryToAddEncoding(fileName);
            }

            if (encoding.FilesPaths.Count == 0)
            {
                Debug.LogError("Couldn't find an encoding in " + brain.AIFilesPath);
                reason=false;
            }
            Handler handler = GetHandler(out string file);
            if (!SolversChecker.CheckSolver(this, brain.SolverName))
            {
                handler.Quit();
                return;
            }
            handler.AddProgram(encoding, true);
            while (reason)
            {
                
                lock (brain.toLock)
                {
                    if (brain.reasonerMethod != null)
                    {
                        brain.solverWaiting = true;
                        while (brain.solverWaiting && !die)
                        {
                            Monitor.Wait(brain.toLock);
                        }
                        if (!reason || die)
                        {
                            KillProcess(handler);
                            return;
                        }

                    }
                }
                try
                {
                    lock (brain.toLock)
                    {
                        if (brain.missingData)
                        {
                            brain.solverWaiting = true;
                            while(brain.solverWaiting && !die) 
                            { 
                                Monitor.Wait(brain.toLock);
                            }
                        }
                    }
                    if(!reason || die)
                    {
                        KillProcess(handler);
                        return;
                    }
                    if (!SpecificFactsRetrieving(brain))
                    {
                        KillProcess(handler);
                        return;
                    }
                    if (lastSensorIteration >= SensorsManager.iteration)
                    {
                        continue;
                    }
                    lastSensorIteration=SensorsManager.iteration;
                    IncrementReadingSensors();
                    if (!_canRead || !reason || die)
                    {
                        KillProcess(handler);
                        return;
                    }
                    stopwatch.Restart();
                    SensorsManager.ReturnSensorsMappings(brain);

                    DecreaseReadingSensors();
                    if (!reason || die)
                    {
                        KillProcess(handler);
                        return;
                    }
                    string time = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string factsFileName = string.Join("_", brain.brainName, time, facts_id++, brain.brainID) + ".txt";
                    factsPath = Path.Combine(Path.GetTempPath(), "ThinkEngineFacts", brain.brainName, factsFileName);
                    using (StreamWriter fs = new StreamWriter(factsPath, true))
                    {
                        fs.Write(brain.whoAmIFact);
                        SpecificFactsWriting(brain, fs);
                        fs.Write(brain.sensorsMapping);
                    }
                    if (brain.debug)
                    {
                        Debug.Log(factsPath);
                    }
                    InputProgram facts = GetInputProgram();
                    facts.AddFilesPath(factsPath);
                    if (!brain.incremental)
                    {
                        handler = GetHandler(out string s);
                        handler.AddProgram(encoding);
                    }
                    handler.AddProgram(facts);

                    for (int i = 0; i < brain.solver_options.Count; i++)
                    {
                        handler.AddOption(new OptionDescriptor(brain.solver_options[i]));

                    }
                    if (!brain.debug)
                    {
                        foreach(OptionDescriptor option in SpecificOptions())
                        {
                            handler.AddOption(option);
                        }
                    }

                    //Debug.Log("running dlv");
                    if (!reason || die)
                    {
                        KillProcess(handler);
                        return;
                    }
                    stopwatch.Restart();
                    handler.StartAsync(new MyCallBack(this));
                    while (!solverDone)
                    {
                        if (!reason || die)
                        {
                            KillProcess(handler);
                            return;
                        }
                    }
                    solverDone = false;
                }
                catch (Exception e)
                {
                    Debug.Log("there was an exception");
                    if (!(e is ThreadAbortException))
                    {
                        UnityEngine.Debug.LogError(e);
                    }
                    else
                    {
                        KillProcess(handler);
                        return;
                    }
                }

            }
            KillProcess(handler);
        }

        private void LookForFiles(string path, ref List<string> filesToCheck)
        {
            filesToCheck.AddRange(Directory.GetFiles(path));
            string[] dir = Directory.GetDirectories(path);
            if(dir.Length == 0)
            {
                return;
            }
            foreach(string file in dir)
            {
                LookForFiles(file, ref filesToCheck);
            }
        }

        private void TryToAddEncoding(string fileName)
        {
            string actualFileName = fileName.Substring(fileName.LastIndexOf(Utility.slash) + 1);
            foreach (string prefix in brain.AIFilesPrefix)
            {
                if (actualFileName.StartsWith(prefix) && (actualFileName.EndsWith(GetCurrentFileExtension()) || actualFileName.EndsWith(".py")))
                {
                    encoding.AddFilesPath(fileName);
                    return;
                }
            }
        }

        private static void KillProcess(Handler handler)
        {
            handler.Quit();
            handler.StopProcess();
        }

        protected abstract InputProgram GetInputProgram();
        internal abstract bool TestSolver();
        protected abstract void OutputParsing(Output o);

        protected abstract Handler GetHandler(out string file, bool test=false);

        protected abstract string GetCurrentFileExtension();
        protected abstract InputProgram GetProgramInstance();
        protected abstract void SpecificFactsWriting(Brain brain, StreamWriter fs);
        protected abstract bool SpecificFactsRetrieving(Brain brain);
        protected abstract List<OptionDescriptor> SpecificOptions();
    }
}
