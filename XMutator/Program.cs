using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Code2Xml.Core.Generators;
using Mono.Options;
using Paraiba.Collections.Generic;
using Paraiba.Core;
using Paraiba.Linq;
using Paraiba.Text;

namespace XMutator {
    internal class Program {
        // run maven test
        private static bool MavenTest(string dirPath) {
            Directory.SetCurrentDirectory(dirPath);

            var p = new Process {
                StartInfo = {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = false,
                    CreateNoWindow = true,
                },
            };
            if (!ParaibaEnvironment.OnUnixLike()) {
                p.StartInfo.FileName = Environment.GetEnvironmentVariable("ComSpec");
                p.StartInfo.Arguments = "/c mvn test";
            } else {
                p.StartInfo.FileName = "mvn";
                p.StartInfo.Arguments = "test";
            }

            p.Start();
            var res = p.StandardOutput.ReadToEnd();
            var endCode = p.ExitCode;
            p.WaitForExit();
            p.Close();

            //Console.WriteLine(res);

            var passed = endCode == 0;
            return passed;
        }

        private static IEnumerable<string> GetAllJavaFiles(string dirPath) {
            return Directory.GetFiles(dirPath, "*.java", SearchOption.AllDirectories);
        }

        private static void RemoveReadonlyAttribute(DirectoryInfo dirInfo) {
            //基のフォルダの属性を変更
            if ((dirInfo.Attributes & FileAttributes.ReadOnly) ==
                FileAttributes.ReadOnly) {
                dirInfo.Attributes = FileAttributes.Normal;
            }
            //フォルダ内のすべてのファイルの属性を変更
            foreach (var fi in dirInfo.GetFiles()) {
                if ((fi.Attributes & FileAttributes.ReadOnly) ==
                    FileAttributes.ReadOnly) {
                    fi.Attributes = FileAttributes.Normal;
                }
            }
            //サブフォルダの属性を回帰的に変更
            foreach (var di in dirInfo.GetDirectories()) {
                RemoveReadonlyAttribute(di);
            }
        }

        private static void CopyDirectory(string sourceDirName, string destDirName) {
            // コピー先のディレクトリが存在しない場合は作成
            if (!Directory.Exists(destDirName)) {
                // ディレクトリ作成
                Directory.CreateDirectory(destDirName);
                // 属性コピー
                File.SetAttributes(destDirName, File.GetAttributes(sourceDirName));
            } else {
                //DirectoryInfoオブジェクトの作成
                var di = new DirectoryInfo(destDirName);

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
            foreach (var file in Directory.GetFiles(sourceDirName)) {
                File.Copy(file, Path.Combine(destDirName, Path.GetFileName(file)), true);
            }

            // コピー元のディレクトリをコピー
            foreach (var dir in Directory.GetDirectories(sourceDirName)) {
                CopyDirectory(dir, Path.Combine(destDirName, Path.GetFileName(dir)));
            }
        }

        private static string GetSourceDirectoryPath(FileInfo pomFileInfo) {
            using (var fs = pomFileInfo.OpenRead()) {
                var doc = XDocument.Load(fs);
                foreach (var e in doc.Descendants("build")) {
                    var e2 = e.Element("sourceDirectory");
                    if (e2 != null) {
                        var splitter = e2.Value.Contains("/") ? '/' : '\\';
                        return Path.Combine(pomFileInfo.DirectoryName,
                                Path.Combine(e2.Value.Split(splitter)));
                    }
                }
            }
            return Path.Combine(pomFileInfo.DirectoryName, "src", "main");
        }

        private static void Main(string[] args) {
            var csv = false;
            var help = false;
            var ratio = 100;
            var maxStatementCount = 1000;
            var p = new OptionSet {
                { "c|csv", v => csv = v != null },
                { "h|?|help", v => help = v != null }, {
                    "r|ratio=", v => {
                        if (!int.TryParse(v, out ratio) || !(0 < ratio && ratio <= 100)) {
                            Console.Error.WriteLine("The given ratio is an invalid value.");
                            Environment.Exit(-1);
                        }
                    }
                }, {
                    "l|limit=", v => {
                        if (!int.TryParse(v, out maxStatementCount) || !(0 < maxStatementCount)) {
                            Console.Error.WriteLine("The given limit is an invalid value.");
                            Environment.Exit(-1);
                        }
                    }
                },
            };
            var dirPaths = p.Parse(args);

            if (!dirPaths.IsEmpty() && !help) {
                foreach (var dirPath in dirPaths) {
                    //var projName = Path.GetFileName(dirPath);
                    //CopyDirectory(dirPath, Path.Combine("backup", projName));

                    if (!MavenTest(dirPath)) {
                        Console.Error.WriteLine("Test cases have already failed.");
                        Environment.Exit(-1);
                    }

                    var generatedMutatns = 0;
                    var killedMutants = 0;

                    var files = Directory.GetFiles(dirPath, "pom.xml", SearchOption.AllDirectories)
                            .Select(pomPath => GetSourceDirectoryPath(new FileInfo(pomPath)))
                            .SelectMany(GetAllJavaFiles)
                            .ToList();
                    var statementCount = files.Select(
                            f => CstGenerators.JavaUsingAntlr3.GenerateTreeFromCodePath(f))
                            .SelectMany(cst => cst.Descendants("statement"))
                            .Count();
                    if (!csv) {
                        Console.WriteLine("Statements: " + statementCount + " / " + maxStatementCount);
                    }

                    if (statementCount > maxStatementCount) {
                        Console.Error.WriteLine("Too many statement.");
                        Environment.Exit(-1);
                    }

                    foreach (var filePath in files) {
                        Encoding encoding;
                        using (var sr = new FileStream(filePath, FileMode.Open)) {
                            var bytes = new byte[1024 * 10];
                            sr.Read(bytes, 0, bytes.Length);
                            encoding = GuessEncoding.GetEncoding(bytes);
                            if (encoding.CodePage == 65001) {
                                encoding = new UTF8Encoding(false);
                            }
                        }
                        var code = File.ReadAllText(filePath, encoding);
                        var tree = CstGenerators.JavaUsingAntlr3.GenerateTreeFromCodeText(code);
                        var nodes = tree.Descendants("statement")
                                .ToList()
                                .Shuffle();
                        var maxCount = nodes.Count * ratio / 100;

                        for (int i = 0; i < maxCount; i++) {
                            var node = nodes[i];
                            if (!csv) {
                                var fileName = Path.GetFileName(filePath);
                                Console.Write("\r" + fileName + ":[" + (i + 1) + "/" + maxCount
                                              + "]");
                            }
                            node.Replacement = "{}";

                            using (var mutant = new StreamWriter(filePath, false, encoding))
                                mutant.WriteLine(tree.Code);
                            //Console.WriteLine(tree.Code);
                            node.Replacement = null;
                            generatedMutatns++;

                            var passed = MavenTest(dirPath);
                            if (!passed) {
                                killedMutants++;
                            }
                        }

                        using (var original = new StreamWriter(filePath, false, encoding))
                            original.WriteLine(tree.Code);
                        if (!csv) {
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