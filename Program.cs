using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AccuRev2Git
{
	class Program
	{
		public static void Main(string[] args)
		{
			if (args.Length == 0)
			{
				throw new ApplicationException("You must specify some arguments - this app isn't a mind reader!");
			}

			var depotName = args[0];
			var workingDir = args[1];
			var startingTran = (args.Length > 2 ? Int32.Parse(args[2]) : 0);
			if (string.IsNullOrEmpty(workingDir))
				throw new ApplicationException(string.Format("No value specified for working directory."));

			loadDepotFromScratch(depotName, workingDir, startingTran);
		}

		static void loadDepotFromScratch(string depotName, string workingDir, int startingTransaction)
		{
			var xdoc = new XDocument();

			var tempFile = string.Format("_{0}_.depot.hist.xml", depotName);
			if (File.Exists(tempFile))
			{
				Console.Write("Existing history file found. Re-use it? [y|n] ");
// ReSharper disable PossibleNullReferenceException
				if (Console.ReadLine().ToUpper() == "Y")
// ReSharper restore PossibleNullReferenceException
				{
					xdoc = XDocument.Load(tempFile);
				}
			}

			if (xdoc.Document == null || xdoc.Nodes().Any() == false)
			{
				Console.WriteLine(string.Format("Retrieving complete history of {0} depot from AccuRev server...", depotName));
				var temp = execAccuRev(string.Format("hist -p {0} -s {0}_dev -k promote -fx", depotName), workingDir);
				File.WriteAllText(tempFile, temp);
				xdoc = XDocument.Parse(temp);
			}

			var n = 0;
// ReSharper disable PossibleNullReferenceException
			var nodes = xdoc.Document.Descendants("transaction").Where(t => Int32.Parse(t.Attribute("id").Value) >= startingTransaction).OrderBy(t => Int32.Parse(t.Attribute("id").Value));
			var nCount = nodes.Count();
			if (startingTransaction == 0)
			{
				var initialDate = long.Parse(nodes.First().Attribute("time").Value) - 60;
				execGitRaw("init", workingDir);
				execGitRaw("add --all", workingDir);
				execGitCommit(string.Format("commit --date={0} --author={1} -m \"Initial git commit.\"", initialDate, "\"Ryan LaNeve <ryan.laneve@avispl.com>\""), workingDir, initialDate.ToString());
				execGitRaw("checkout -b dev", workingDir, true);
			}
			else
			{
				execClean(workingDir);
				var lastTran = xdoc.Document.Descendants("transaction").OrderByDescending(t => Int32.Parse(t.Attribute("id").Value)).Where(t => Int32.Parse(t.Attribute("id").Value) < startingTransaction).First().Attribute("id").Value;
				execAccuRev(string.Format("chstream -s {0}_time-warp -t {1}", depotName, lastTran), workingDir);
				execAccuRev(string.Format("pop -R -O ."), workingDir);
			}
			foreach (var transaction in nodes)
// ReSharper restore PossibleNullReferenceException
			{
				n++;
				var transactionId = Convert.ToInt32(transaction.Attribute("id").Value);
				Console.Write("Loading transaction {0} of {1} [id={2}]\x0D", n.ToString("00000"), nCount, transactionId.ToString("00000"));
				loadTransaction(depotName, transactionId, workingDir, transaction);
			}
			Console.WriteLine();
		}

		static void loadTransaction(string depotName, int transactionId, string workingDir, XElement transaction)
		{
			var accurevUser = transaction.Attribute("user").Value;
			var gitUser = translateUser(accurevUser);
			var unixDate = long.Parse(transaction.Attribute("time").Value);
			var datetime = convertDateTime(unixDate);
			var commentNode = transaction.Descendants("comment").FirstOrDefault();
			var comment = (commentNode == null ? string.Empty : commentNode.Value);
			comment = string.IsNullOrEmpty(comment) ? "[no original comment]" : comment;
			comment += string.Format("{0}{0}[AccuRev Transaction #{1}]", Environment.NewLine, transactionId);
			var commentFile = string.Format(".\\_{0}_Comment.txt", depotName, transactionId);
			var commentFilePath = Path.GetFullPath(commentFile);
			File.WriteAllText(commentFile, comment);
			//execClean(workingDir);
			//execAccuRev(string.Format("pop -R -O -v {0}_dev -L . -t {1} .", depotName, transactionId), workingDir);
			execAccuRev(string.Format("chstream -s {0}_time-warp -t {1}", depotName, transactionId), workingDir);
			try
			{
				execAccuRev(string.Format("update"), workingDir);
			}
			catch(Exception)
			{
				execAccuRev("chws -w \"CRM_Ryan\" -l c:\\temp", workingDir);
				execClean(workingDir);
				execAccuRev(string.Format("pop -R -O -v {0}_dev -L . -t {1} .", depotName, transactionId), workingDir);
				execAccuRev("chws -w \"CRM_Ryan\" -l .", workingDir);
			}
			execGitRaw("add --all", workingDir);
			execGitCommit(string.Format("commit --date={0} --author={1} --file={2}", unixDate, gitUser, commentFilePath), workingDir, unixDate.ToString());
		}

		static string translateUser(string accurevUser)
		{
			switch(accurevUser)
			{
				case "Ryan":
					return "\"Ryan LaNeve <ryan.laneve@avispl.com>\"";
				case "George":
					return "\"George Cox <george.cox@avispl.com>\"";
				case "Joey":
					return "\"Joey Shipley <joey.shipley@avispl.com>\"";
				case "Jeremy":
					return "\"Jeremy Schwartzberg <jeremy.schwartzberg@avispl.com>\"";
				case "Josh":
					return "\"Josh Schwartzberg <josh.schwartzberg@avispl.com>\"";
				case "Kevin":
					return "\"Kevin Rood <kevin.rood@avispl.com>\"";
				case "Erick":
					return "\"Erick Dahling <erick.dahling@avispl.com>\"";
				case "Wayne":
					return "\"Wayne Molena <wayne.molena@avispl.com>\"";
			}
			return accurevUser;
		}

		static DateTime _baseDateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		static DateTime convertDateTime(long accuRevDateTime)
		{
			return _baseDateTime.AddSeconds(accuRevDateTime).ToLocalTime();
		}

		static void execClean(string workingDir)
		{
			foreach (var dir in Directory.GetDirectories(workingDir).Where(dir => !dir.EndsWith(".git")))
				Directory.Delete(dir, true);
			foreach (var file in Directory.GetFiles(workingDir).Where(file => !file.EndsWith(".gitignore")))
				File.Delete(file);
		}

		static string execAccuRev(string arguments, string workingDir)
		{
			var accuRevPath = ConfigurationManager.AppSettings["AccuRevPath"];
			var process = new Process
			{
				StartInfo = new ProcessStartInfo(accuRevPath, arguments)
			};
			if (!string.IsNullOrEmpty(workingDir))
				process.StartInfo.WorkingDirectory = workingDir;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			process.StartInfo.RedirectStandardError = true;
			process.StartInfo.RedirectStandardOutput = true;
			if (!process.StartInfo.EnvironmentVariables.ContainsKey("ACCUREV_PRINCIPAL"))
				process.StartInfo.EnvironmentVariables.Add("ACCUREV_PRINCIPAL", "Ryan");
			process.Start();
			var result = process.StandardOutput.ReadToEnd();
			if (process.StandardError.EndOfStream == false)
			{
				var errors = process.StandardError.ReadToEnd();
				if (!errors.Contains("is defunct") && !errors.Contains("No element named") && !errors.Contains("Unable to proceed with annotate.") && !errors.Contains("Specified version not found for:"))
				{
					Debug.WriteLine(errors);
					throw new ApplicationException(string.Format("AccuRev has returned an error: {0}", errors));
				}
			}
			return result;
		}

		private static void execGitRaw(string arguments, string workingDir)
		{
			execGitRaw(arguments, workingDir, false);
		}

		static void execGitRaw(string arguments, string workingDir, bool ignoreErrors)
		{
			var gitPath = ConfigurationManager.AppSettings["GitPath"];
			var process = new Process
			{
				StartInfo = new ProcessStartInfo(gitPath, arguments)
			};
			if (!string.IsNullOrEmpty(workingDir))
				process.StartInfo.WorkingDirectory = workingDir;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			process.StartInfo.RedirectStandardError = true;
			process.StartInfo.RedirectStandardOutput = true;
			process.Start();
			process.StandardOutput.ReadToEnd();
			if (!ignoreErrors && process.StandardError.EndOfStream == false)
			{
				var errors = process.StandardError.ReadToEnd();
				throw new ApplicationException(string.Format("Git has returned an error: {0}", errors));
			}
		}

		static void execGitCommit(string arguments, string workingDir, string commitDate)
		{
			var gitPath = ConfigurationManager.AppSettings["GitPath"];
			var process = new Process
			{
				StartInfo = new ProcessStartInfo(gitPath, arguments)
			};
			if (!string.IsNullOrEmpty(workingDir))
				process.StartInfo.WorkingDirectory = workingDir;
			process.StartInfo.EnvironmentVariables.Add("GIT_COMMITTER_DATE", commitDate);
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			process.StartInfo.RedirectStandardError = true;
			process.StartInfo.RedirectStandardOutput = true;
			process.Start();
			process.StandardOutput.ReadToEnd();
			if (process.StandardError.EndOfStream == false)
			{
				var errors = process.StandardError.ReadToEnd();
				throw new ApplicationException(string.Format("Git has returned an error: {0}", errors));
			}
		}
	}
}
