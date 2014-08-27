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
    internal enum TestResult {
        Passed,
        Failed,
        TimeOver,
    }

    internal class Program {
        // run maven test
        private static TestResult MavenTest(string dirPath, int maxMilliseconds) {
            Directory.SetCurrentDirectory(dirPath);

            using (var p = new Process {
                StartInfo = {
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardInput = false,
                    CreateNoWindow = false,
                },
            }) {
                if (!ParaibaEnvironment.OnUnixLike()) {
                    p.StartInfo.FileName = Environment.GetEnvironmentVariable("ComSpec");
                    p.StartInfo.Arguments = "/c mvn test";
                } else {
                    p.StartInfo.FileName = "mvn";
                    p.StartInfo.Arguments = "test";
                }

                p.Start();
                if (!p.WaitForExit(maxMilliseconds)) {
                    p.KillAllProcessesSpawnedBy();
                    return TestResult.TimeOver;
                }
                //Console.WriteLine(res);

                return p.ExitCode == 0 ? TestResult.Passed : TestResult.Failed;
            }
        }

        private static IEnumerable<FileInfo> GetAllPomFiles(string dirPath) {
            var pomFilePath = Path.Combine(dirPath, "pom.xml");
            if (File.Exists(pomFilePath)) {
                return GetSubmoduleDirectoryPaths(new FileInfo(pomFilePath));
            }
            return Enumerable.Empty<FileInfo>();
        }

        private static IEnumerable<string> GetAllJavaFiles(string dirPath) {
            if (Directory.Exists(dirPath)) {
                return Directory.GetFiles(dirPath, "*.java", SearchOption.AllDirectories);
            }
            return Enumerable.Empty<string>();
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

        private static IEnumerable<FileInfo> GetSubmoduleDirectoryPaths(FileInfo pomFileInfo) {
            yield return pomFileInfo;
            using (var fs = pomFileInfo.OpenRead()) {
                var doc = XDocument.Load(fs);
                var modules = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "modules");
                if (modules != null) {
                    foreach (var e in modules.Elements().Where(e => e.Name.LocalName == "module")) {
                        yield return new FileInfo(
                                Path.Combine(pomFileInfo.DirectoryName, e.Value, "pom.xml"));
                    }
                }
            }
        }

        private static string GetSourceDirectoryPath(FileInfo pomFileInfo) {
            using (var fs = pomFileInfo.OpenRead()) {
                var doc = XDocument.Load(fs);
                foreach (var build in doc.Descendants().Where(e => e.Name.LocalName == "build")) {
                    var e2 = build.Elements()
                            .FirstOrDefault(e => e.Name.LocalName == "sourceDirectory");
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
            var maxMinutes = 60;
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
                        if (!int.TryParse(v, out maxMinutes) || !(0 < maxMinutes)) {
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

                    var files = GetAllPomFiles(dirPath)
                            .Select(GetSourceDirectoryPath)
                            .SelectMany(GetAllJavaFiles)
                            .ToList();
                    var statementCount = files.Select(
                            f => CstGenerators.JavaUsingAntlr3.GenerateTreeFromCodePath(f))
                            .SelectMany(cst => cst.Descendants("statement"))
                            .Count();

                    if (statementCount < 10) {
                        Console.Error.WriteLine("Too small project (" + statementCount
                                                + "[statements]).");
                        ShowResultInCsv(statementCount);
                        Environment.Exit(-1);
                    }

                    TryMavenTest(dirPath, maxMinutes * 60 * 1000 / 10, statementCount);

                    var maxMillisecondsToTest = maxMinutes * 60 * 1000 / statementCount;
                    var millisecondsToTest = Environment.TickCount;
                    TryMavenTest(dirPath, maxMillisecondsToTest, statementCount);
                    millisecondsToTest = (Environment.TickCount - millisecondsToTest);

                    var generatedMutatns = 0;
                    var killedMutants = 0;

                    var estimatedMinutes = millisecondsToTest * statementCount / 60 / 1000;
                    if (!csv) {
                        Console.WriteLine("Statements: " + statementCount);
                        Console.WriteLine("Minutes (max: " + maxMinutes + "): " + estimatedMinutes
                                          + " (" + millisecondsToTest + "[ms] * " + statementCount
                                          + ") / ");
                    }

                    if (estimatedMinutes > maxMinutes) {
                        Console.Error.WriteLine("Too long time (" + estimatedMinutes + "[min]).");
                        ShowResultInCsv(statementCount, millisecondsToTest);
                        Environment.Exit(-2);
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

                            using (var mutant = new StreamWriter(filePath, false, encoding)) {
                                mutant.WriteLine(tree.Code);
                            }
                            //Console.WriteLine(tree.Code);
                            node.Replacement = null;
                            generatedMutatns++;

                            if (MavenTest(dirPath, maxMillisecondsToTest * 5) != TestResult.Passed) {
                                killedMutants++;
                            }
                        }

                        using (var original = new StreamWriter(filePath, false, encoding)) {
                            original.WriteLine(tree.Code);
                        }
                        if (!csv) {
                            Console.WriteLine("");
                        }
                    }

                    // Measure mutation scores
                    var percentage = killedMutants * 100 / generatedMutatns;
                    if (csv) {
                        ShowResultInCsv(statementCount, millisecondsToTest, killedMutants,
                                generatedMutatns, percentage);
                    } else {
                        Console.WriteLine(dirPath + ": " + killedMutants + " / " + generatedMutatns
                                          + ": " + percentage + "%");
                    }
                }
            } else {
                p.WriteOptionDescriptions(Console.Out);
            }
        }

        private static void TryMavenTest(string dirPath, int maxMilliseconds, int statementCount) {
            switch (MavenTest(dirPath, maxMilliseconds)) {
            case TestResult.Passed:
                break;
            case TestResult.Failed:
                Console.Error.WriteLine("Failed.");
                ShowResultInCsv(statementCount);
                Environment.Exit(-1);
                break;
            case TestResult.TimeOver:
                Console.Error.WriteLine("Timeover.");
                ShowResultInCsv(statementCount);
                Environment.Exit(-1);
                break;
            default:
                throw new ArgumentOutOfRangeException();
            }
        }

        private static void ShowResultInCsv(
                int statementCount, int millisecondsToTest = -2,
                int killedMutants = -2, int generatedMutatns = -2, int percentage = -2) {
            Console.WriteLine(killedMutants + "," + generatedMutatns + "," + percentage + ","
                              + statementCount + "," + millisecondsToTest);
        }
    }
}