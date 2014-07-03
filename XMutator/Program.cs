using System;
using Mono.Options;
using Paraiba.Linq;

namespace XMutator {
    internal class Program {
        private static void Main(string[] args) {
            var csv = false;
            var help = false;
            var p = new OptionSet {
                { "c|csv", v => csv = v != null },
                { "h|?|help", v => help = v != null },
            };
            var filePaths = p.Parse(args);
            if (!filePaths.IsEmpty() && !help) {
                foreach (var filePath in filePaths) {
                    // Measure mutation scores
                    var generatedMutatns = 10;
                    var killedMutants = 3;
                    var percentage = killedMutants * 100 / generatedMutatns;
                    if (csv) {
                        Console.WriteLine(killedMutants + "," + generatedMutatns + "," + percentage);
                    } else {
                        Console.WriteLine(filePath + ": " + killedMutants + " / " + generatedMutatns
                                          + ": " + percentage + "%");
                    }
                }
            } else {
                p.WriteOptionDescriptions(Console.Out);
            }
        }
    }
}