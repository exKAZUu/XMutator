using System;
using Mono.Options;
using Paraiba.Linq;
using System.Collections;
using System.IO;
using System.Text;
using Code2Xml.Core.Generators;
using System.Linq;

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

        private static string[] getAllJavaFiles(string dirPath) {
            string[] files = System.IO.Directory.GetFiles(dirPath, "*.java", System.IO.SearchOption.AllDirectories);

            return files;
        }

        private static void copyDirectory(string sourceDirName, string destDirName) {

            // コピー先のディレクトリが存在しない場合は作成
            if (!Directory.Exists(destDirName))
            {
                // ディレクトリ作成
                Directory.CreateDirectory(destDirName);
                // 属性コピー
                File.SetAttributes(destDirName, File.GetAttributes(sourceDirName));
            }

            // コピー元のディレクトリに存在するファイルをコピー
            foreach (string file in Directory.GetFiles(sourceDirName))
            {
                File.Copy(file, Path.Combine(destDirName, Path.GetFileName(file)), true);
            }

            // コピー元のディレクトリをコピー
            foreach (string dir in Directory.GetDirectories(sourceDirName))
            {
                copyDirectory(dir, Path.Combine(destDirName, Path.GetFileName(dir)));
            }
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
                    int generatedMutatns = 0;
                    int killedMutants = 0;
                    copyDirectory(dirPath, "backup");

                    var files = getAllJavaFiles(dirPath);
                    foreach (var filePath in files)
                    {
                        StreamReader sr = new StreamReader(filePath, Encoding.GetEncoding("utf-8"));
                        string code = sr.ReadToEnd();
                        sr.Close();

                        var tree = CstGenerators.JavaUsingAntlr3.GenerateTreeFromCodeText(code);
                        var nodes = tree.Descendants().Where(e => e.Name == "statement");

                        foreach (var node in nodes)
                        {
                            var restoreFunc = node.Remove();
                            StreamWriter mutant = new StreamWriter(filePath, false, Encoding.GetEncoding("utf-8"));
                            mutant.WriteLine(tree.Code);
                            mutant.Close();
                            restoreFunc();
                            generatedMutatns++;

                            var testRes = mavenTest(dirPath);
                            if (testRes == 0) killedMutants++;
                        }

                        StreamWriter original = new StreamWriter(filePath, false, Encoding.GetEncoding("utf-8"));
                        original.WriteLine(tree.Code);
                        original.Close();
                    }

                    // Measure mutation scores
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