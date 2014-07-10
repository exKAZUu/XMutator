using System;
using Mono.Options;
using Paraiba.Linq;

namespace XMutator {
    internal class Program {
        // run maven test
        private static int mavenTest(string dirPath) {
            System.Diagnostics.Process p = new System.Diagnostics.Process();
            p.StartInfo.FileName = System.Environment.GetEnvironmentVariable("ComSpec");
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardInput = false;
            p.StartInfo.CreateNoWindow = true;
            
            System.IO.Directory.SetCurrentDirectory(dirPath);
            var cmd = "/c mvn test";

            p.StartInfo.Arguments = cmd;
            p.Start();
            var res = p.StandardOutput.ReadToEnd();
            var endCode = p.ExitCode;
            p.WaitForExit();
            p.Close();

            // initial:-1 PASS:0 FAIL:1 Not run:2 
            var resCode = -1;
            if (res.Contains("No tests to run")) {
                resCode = 2;
            } else if (endCode == 0) {
                resCode = 0;
            } else {
                resCode = 1;
            }
            
            return resCode;
        }

        private static void Main(string[] args) {
            var csv = false;
            var help = false;
            var p = new OptionSet {
                { "c|csv", v => csv = v != null },
                { "h|?|help", v => help = v != null },
            };
            var dirPaths = p.Parse(args);
            if (!dirPaths.IsEmpty() && !help) {
                foreach (var dirPath in dirPaths) {
                    var testRes = mavenTest(dirPath);
                    Console.WriteLine(testRes);
                    // Measure mutation scores
                    var generatedMutatns = 10;
                    var killedMutants = 3;
                    var percentage = killedMutants * 100 / generatedMutatns;
                    if (csv) {
                        Console.WriteLine(killedMutants + "," + generatedMutatns + "," + percentage);
                    } else {
                        Console.WriteLine(dirPath + ": " + killedMutants + " / " + generatedMutatns
                                          + ": " + percentage + "%");
                    }
                }
            } else {
                p.WriteOptionDescriptions(Console.Out);
            }
        }
    }
}