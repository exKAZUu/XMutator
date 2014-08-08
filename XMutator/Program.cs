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

            //Console.WriteLine(res);

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

        private static void RemoveReadonlyAttribute(DirectoryInfo dirInfo)
        {
            //基のフォルダの属性を変更
            if ((dirInfo.Attributes & FileAttributes.ReadOnly) ==
                FileAttributes.ReadOnly)
                dirInfo.Attributes = FileAttributes.Normal;
            //フォルダ内のすべてのファイルの属性を変更
            foreach (FileInfo fi in dirInfo.GetFiles())
                if ((fi.Attributes & FileAttributes.ReadOnly) ==
                    FileAttributes.ReadOnly)
                    fi.Attributes = FileAttributes.Normal;
            //サブフォルダの属性を回帰的に変更
            foreach (DirectoryInfo di in dirInfo.GetDirectories())
                RemoveReadonlyAttribute(di);
        }

        private static void CopyDirectory(string sourceDirName, string destDirName) {
            // コピー先のディレクトリが存在しない場合は作成
            if (!Directory.Exists(destDirName)) {
                // ディレクトリ作成
                Directory.CreateDirectory(destDirName);
                // 属性コピー
                File.SetAttributes(destDirName, File.GetAttributes(sourceDirName));
            }
            else
            {
                //DirectoryInfoオブジェクトの作成
                DirectoryInfo di = new DirectoryInfo(destDirName);

                //フォルダ以下のすべてのファイル、フォルダの属性を削除
                RemoveReadonlyAttribute(di);

                //フォルダを根こそぎ削除
                di.Delete(true);

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
            var ratio = "100";
            var p = new OptionSet {
                { "c|csv", v => csv = v != null },
                { "h|?|help", v => help = v != null },
                { "r|ratio=", v => ratio = v },
            };
            
            var dirPaths = p.Parse(args);
            int temp;
            if (!int.TryParse(ratio, out temp) || ((temp < 0) || (temp > 100)))
            {
                Console.WriteLine("ratio is invalid value");
                System.Environment.Exit(0);
            }
         
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
                        var tree = CstGenerators.JavaUsingAntlr3.GenerateTreeFromCodePath(filePath);
                        var nodes = tree.Descendants().Where(e => e.Name == "statement");
                        var size = nodes.Count();

                        var index = nodes.OrderBy(j => Guid.NewGuid()).ToArray();
                        var range = size * (double.Parse(ratio) / 100.0);

                        var i = 1;
                        foreach (var node in nodes) {
                            
                            var flag = false;
                            if ((range >= 1) && Array.IndexOf(index, node, 0, (int)range) != -1)
                            {
                                flag = true;
                            }

                            if (flag)
                            {
                                if (!csv)
                                {
                                    string fileName = Path.GetFileName(filePath);
                                    Console.Write("\r" + fileName + ":[" + i + "/" + (int)range + "]");
                                }
                                node.Replacement = "{}";

                                using (var mutant = new StreamWriter(filePath, false,
                                    Encoding.GetEncoding(932)))
                                {
                                    mutant.WriteLine(tree.Code);
                                }
                                //Console.WriteLine(tree.Code);
                                node.Replacement = null;
                                generatedMutatns++;

                                var testRes = MavenTest(dirPath);
                                if (testRes == 1)
                                {
                                    killedMutants++;
                                }
                                i++;
                            }
                        }

                        using (var original = new StreamWriter(filePath, false,
                                Encoding.GetEncoding(932)))
                        original.WriteLine(tree.Code);
                        if (!csv)
                        {
                            Console.WriteLine("");
                        }
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