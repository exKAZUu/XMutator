using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Code2Xml.Core.Generators;
using Mono.Options;
using Paraiba.Linq;

namespace XMutator {
    internal class Program {
        // run maven test
        private static int MavenTest(string dirPath) {
            var p = new Process {
                StartInfo = {
                    FileName = Environment.GetEnvironmentVariable("ComSpec"),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = false,
                    CreateNoWindow = true
                }
            };

            Directory.SetCurrentDirectory(dirPath);
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

        private static IEnumerable<string> GetAllJavaFiles(string dirPath) {
            return Directory.GetFiles(dirPath, "*.java", SearchOption.AllDirectories);
        }

        private static void CopyDirectory(string sourceDirName, string destDirName) {
            // コピー先のディレクトリが存在しない場合は作成
            if (!Directory.Exists(destDirName)) {
                // ディレクトリ作成
                Directory.CreateDirectory(destDirName);
                // 属性コピー
                File.SetAttributes(destDirName, File.GetAttributes(sourceDirName));
            }

            // コピー元のディレクトリに存在するファイルをコピー
            foreach (string file in Directory.GetFiles(sourceDirName)) {
                File.Copy(file, Path.Combine(destDirName, Path.GetFileName(file)), true);
            }

            // コピー元のディレクトリをコピー
            foreach (string dir in Directory.GetDirectories(sourceDirName)) {
                CopyDirectory(dir, Path.Combine(destDirName, Path.GetFileName(dir)));
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
                    string projName = Path.GetFileName(dirPath);
                    CopyDirectory(dirPath, "backup\\"+projName);

                    if (MavenTest(dirPath) == 2)
                    {
                        Console.WriteLine("Not Run Tests");
                        System.Environment.Exit(0);
                    }

                    int generatedMutatns = 0;
                    int killedMutants = 0;
                    
                    var files = GetAllJavaFiles(dirPath+"\\src\\main");
                    foreach (var filePath in files)
                    {
                        string code;
                        using (var sr = new StreamReader(filePath, Encoding.GetEncoding("utf-8"))) {
                            code = sr.ReadToEnd();
                        }

                        var tree = CstGenerators.JavaUsingAntlr3.GenerateTreeFromCodeText(code);
                        var nodes = tree.Descendants().Where(e => e.Name == "statement");

                        foreach (var node in nodes) {
                            node.Replacement = "{}";
                            using (var mutant = new StreamWriter(filePath, false,
                                    Encoding.GetEncoding("utf-8"))) {
                                mutant.WriteLine(tree.Code);
                            }
                            node.Replacement = null;
                            generatedMutatns++;

                            var testRes = MavenTest(dirPath);
                            if (testRes == 0) {
                                killedMutants++;
                            }
                        }

                        using (var original = new StreamWriter(filePath, false,
                                Encoding.GetEncoding("utf-8")))
                            original.WriteLine(tree.Code);
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