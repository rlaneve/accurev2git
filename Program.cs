﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CommandLine;
using CommandLine.Text;

namespace AccuRev2Git
{
	class Options
	{
		[Option("d", "depot", HelpText = "Name of the AccuRev depot.", Required = true)]
		public string DepotName { get; set; }

		[Option("s", "stream", HelpText = "Name of the AccuRev stream.", DefaultValue = "")]
		public string StreamName { get; set; }

		[Option("w", "workingDir", HelpText = "Working directory of [new] git repo.", Required = true)]
		public string WorkingDir { get; set; }

		[Option("t", "startingTran", HelpText = "AccuRev transaction to start from.", DefaultValue = null)]
		public int? StartingTransaction { get; set; }

		[Option("r", "resume", HelpText = "Resume from last transaction completed.", DefaultValue = false)]
		public bool Resume { get; set; }

		[Option("u", "username", HelpText = "AccuRev username.", DefaultValue = null)]
		public string AccuRevUserName { get; set; }

		[Option("p", "password", HelpText = "AccuRev password.", DefaultValue = null)]
		public string AccuRevPassword { get; set; }
	}

	class Program
	{
		private static Options _options = new Options();
		private static string _accurevUsername;
		private static List<GitUser> _gitUsers;

		public static void Main(string[] args)
		{
			if (args == null || !CommandLineParser.Default.ParseArguments(args, _options))
			{
				Console.WriteLine(HelpText.AutoBuild(_options));
				return;
			}

			var depotName = _options.DepotName;
			var streamName = _options.StreamName;
			var workingDir = _options.WorkingDir;
			var startingTran = _options.StartingTransaction;
			var resume = _options.Resume;

			accurevLogin(_options);
			loadUsers();
			loadDepotFromScratch(depotName, streamName, workingDir, startingTran, resume);
		}

		private static void accurevLogin(Options options)
		{
			Console.WriteLine("AccuRev Login...");
			_accurevUsername = options.AccuRevUserName;
			if (string.IsNullOrEmpty(_accurevUsername))
			{
				Console.Write("Username: ");
				_accurevUsername = Console.ReadLine();
			}
			var password = options.AccuRevPassword;
			if (string.IsNullOrEmpty(password))
			{
				Console.Write("Password: ");
				password = Console.ReadLine();
			}
			execAccuRev(string.Format("login -n {0} \"{1}\"", _accurevUsername, password), string.Empty);
		}

		private static void loadUsers()
		{
			if (!File.Exists("users.txt"))
				throw new ApplicationException("No \"users.txt\" file found!");

			var usersRaw = File.ReadAllLines("users.txt");
			_gitUsers = new List<GitUser>(usersRaw.Length);
			foreach (var parts in usersRaw.Where(userRaw => !string.IsNullOrEmpty(userRaw) && !userRaw.StartsWith("#")).Select(userRaw => userRaw.Split('|')))
				_gitUsers.Add(new GitUser(parts[0], parts[1], parts[2]));
		}

		static void loadDepotFromScratch(string depotName, string streamName, string workingDir, int? startingTransaction, bool resume)
		{
			var xdoc = new XDocument();

			var tempFile = string.Format("_{0}_{1}_.depot.hist.xml", depotName, streamName);
			if (File.Exists(tempFile))
			{
				if (resume)
				{
					xdoc = XDocument.Load(tempFile);
				}
				else
				{
					Console.Write("Existing history file found. Re-use it? [y|n] ");
// ReSharper disable PossibleNullReferenceException
					if (Console.ReadLine().ToUpper() == "Y")
// ReSharper restore PossibleNullReferenceException
					{
						xdoc = XDocument.Load(tempFile);
					}
				}
			}

			if (xdoc.Document == null || xdoc.Nodes().Any() == false)
			{
				Console.WriteLine("Retrieving complete history of {0} depot, {1} stream, from AccuRev server...", depotName, streamName);
				var temp = execAccuRev(string.Format("hist -p \"{0}\" -s \"{0}{1}\" -k promote -fx", depotName, (string.IsNullOrEmpty(streamName) ? "" : "_" + streamName)), workingDir);
				File.WriteAllText(tempFile, temp);
				xdoc = XDocument.Parse(temp);
			}

			var n = 0;
// ReSharper disable PossibleNullReferenceException
			var nodes = xdoc.Document.Descendants("transaction").Where(t => Int32.Parse(t.Attribute("id").Value) >= startingTransaction).OrderBy(t => Int32.Parse(t.Attribute("id").Value));
			if (resume)
			{
				Console.WriteLine("Attempting to resume from last completed transaction.");
				int lastTransaction;
				var result = execGitRaw("config --get accurev2git.lasttran", workingDir);
				if (!Int32.TryParse(result, out lastTransaction))
					throw new ApplicationException("Cannot resume - no last transaction was found in git config key 'accurev2git.lasttran'.");

				Console.WriteLine("Resuming from last completed transaction {0}.", lastTransaction);
				Console.WriteLine("Retrieving history as of {0} for {1} depot, {2} stream, from AccuRev server...", lastTransaction, depotName, streamName);
				var temp = execAccuRev(string.Format("hist -p \"{0}\" -s \"{0}{1}\" -k promote -t {2}-now -fx", depotName, (string.IsNullOrEmpty(streamName) ? "" : "_" + streamName), lastTransaction), workingDir);
				File.WriteAllText(tempFile, temp);
				xdoc = XDocument.Parse(temp);

				nodes = xdoc.Document.Descendants("transaction").Where(t => Int32.Parse(t.Attribute("id").Value) > lastTransaction).OrderBy(t => Int32.Parse(t.Attribute("id").Value));
				try
				{
					startingTransaction = nodes.Select(t => Int32.Parse(t.Attribute("id").Value)).First();
				}
				catch (Exception)
				{
					Console.WriteLine("No transactions found after last transaction of {0}.", lastTransaction);
					return;
				}
			}
			else
			{
				startingTransaction = startingTransaction ?? 0;
			}
			var nCount = nodes.Count();
			if (startingTransaction == 0)
			{
				var initialDate = long.Parse(nodes.First().Attribute("time").Value) - 60;
				var defaultGitUserName = ConfigurationManager.AppSettings.Get("DefaultGitUserName");
				var defaultGitUser = _gitUsers.SingleOrDefault(u => u.Name.Equals(defaultGitUserName, StringComparison.OrdinalIgnoreCase));
				if (defaultGitUser == null)
					 throw new ApplicationException("Cannot initialize new repository without a DefaultGitUserName specified!");
				if (Directory.Exists(Path.Combine(workingDir, ".git")))
					throw new ApplicationException(string.Format("Cannot initialize new repository; repository already exists in '{0}'.", workingDir));
				execGitRaw("init", workingDir);
				File.WriteAllText(Path.Combine(workingDir, ".gitignore"), "#empty");
				execGitRaw("add --all", workingDir);
				execGitCommit(string.Format("commit --date={0} --author={1} -m \"Initial git commit.\"", initialDate, defaultGitUser), workingDir, initialDate.ToString(), defaultGitUser);
				execGitRaw(string.Format("checkout -b \"{0}\"", streamName), workingDir, true);
			}
			foreach (var transaction in nodes)
			{
				n++;
				var transactionId = Convert.ToInt32(transaction.Attribute("id").Value);
				Console.Write("Loading transaction {0} of {1} [id={2}]\x0D", n.ToString("00000"), nCount, transactionId.ToString("00000"));
				loadTransaction(depotName, streamName, transactionId, workingDir, transaction);
			}
// ReSharper restore PossibleNullReferenceException
			Console.WriteLine();
		}

		static void loadTransaction(string depotName, string streamName, int transactionId, string workingDir, XElement transaction)
		{
// ReSharper disable PossibleNullReferenceException
			var accurevUser = transaction.Attribute("user").Value;
			var gitUser = translateUser(accurevUser);
			var unixDate = long.Parse(transaction.Attribute("time").Value);
// ReSharper restore PossibleNullReferenceException
			var issueNumNodes = transaction.Descendants("version").Descendants("issueNum");
			var issueNums = (issueNumNodes == null || issueNumNodes.Count() == 0 ? string.Empty : issueNumNodes.Select(n => n.Value).Distinct().Aggregate(string.Empty, (seed, num) => seed + ", " + num).Substring(2));
			var commentNode = transaction.Descendants("comment").FirstOrDefault();
			var comment = (commentNode == null ? string.Empty : commentNode.Value);
			comment = string.IsNullOrEmpty(comment) ? "[no original comment]" : comment;
			var commentLines = comment.Split(new[] { "\n" }, 2, StringSplitOptions.None);
			if (commentLines.Length > 1)
				comment = commentLines[0] + Environment.NewLine + Environment.NewLine + commentLines[1];
			comment += string.Format("{0}{0}[AccuRev Transaction #{1}]", Environment.NewLine, transactionId);
			if (!string.IsNullOrEmpty(issueNums))
				comment += string.Format("{0}[Issue #s: {1}]", Environment.NewLine, issueNums);
			var commentFile = string.Format(".\\_{0}_Comment.txt", depotName);
			var commentFilePath = Path.GetFullPath(commentFile);
			File.WriteAllText(commentFile, comment);
			execClean(workingDir);
			execAccuRev(string.Format("pop -R -O -v \"{0}{1}\" -L . -t {2} .", depotName, (string.IsNullOrEmpty(streamName) ? "" : "_" + streamName), transactionId), workingDir);
			execGitRaw("add --all", workingDir);
			execGitCommit(string.Format("commit --date={0} --author={1} --file=\"{2}\"", unixDate, gitUser, commentFilePath), workingDir, unixDate.ToString(), gitUser);
			execGitRaw(string.Format("config accurev2git.lasttran {0}", transactionId), workingDir);
		}

		static GitUser translateUser(string accurevUser)
		{
			return _gitUsers.SingleOrDefault(u => u.AccuRevUserName.Equals(accurevUser, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Depth-first recursive delete, with handling for descendant 
		/// directories open in Windows Explorer.
		/// See http://stackoverflow.com/a/1703799/264822
		/// </summary>
		public static void deleteDirectory(string path)
		{
			foreach (string directory in Directory.GetDirectories(path))
			{
				deleteDirectory(directory);
			}

			try
			{
				Directory.Delete(path, true);
			}
			catch (IOException)
			{
				Directory.Delete(path, true);
			}
			catch (UnauthorizedAccessException)
			{
				Directory.Delete(path, true);
			}
		}

		static void execClean(string workingDir)
		{
			foreach (var dir in Directory.GetDirectories(workingDir).Where(dir => !dir.EndsWith(".git")))
			{
				//Directory.Delete(dir, true);
				deleteDirectory(dir);
			}
			foreach (var file in Directory.GetFiles(workingDir).Where(file => !file.EndsWith(".gitignore")))
			{
				File.Delete(file);
		}
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
				process.StartInfo.EnvironmentVariables.Add("ACCUREV_PRINCIPAL", _accurevUsername);
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

		static string execGitRaw(string arguments, string workingDir, bool ignoreErrors = false)
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
			var output = process.StandardOutput.ReadToEnd();
			if (!ignoreErrors && process.StandardError.EndOfStream == false)
			{
				var errors = process.StandardError.ReadToEnd();
				throw new ApplicationException(string.Format("Git has returned an error: {0}", errors));
			}
			return output;
		}

		static void execGitCommit(string arguments, string workingDir, string commitDate, GitUser gitUser)
		{
			var gitPath = ConfigurationManager.AppSettings["GitPath"];
			var process = new Process
			{
				StartInfo = new ProcessStartInfo(gitPath, arguments)
			};
			if (!string.IsNullOrEmpty(workingDir))
				process.StartInfo.WorkingDirectory = workingDir;
			process.StartInfo.EnvironmentVariables.Add("GIT_COMMITTER_DATE", commitDate);
			process.StartInfo.EnvironmentVariables.Add("GIT_COMMITTER_NAME", gitUser.Name);
			process.StartInfo.EnvironmentVariables.Add("GIT_COMMITTER_EMAIL", gitUser.Email);
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			process.StartInfo.RedirectStandardError = true;
			process.StartInfo.RedirectStandardOutput = true;
			process.Start();
			var output = process.StandardOutput.ReadToEnd();
			if (process.StandardError.EndOfStream == false)
			{
				var errors = process.StandardError.ReadToEnd();
				throw new ApplicationException(string.Format("Git has returned an error: {0}", errors));
			}
		}

		internal class GitUser
		{
			public string Name { get; set; }
			public string Email { get; set; }
			public string AccuRevUserName { get; set; }

			public GitUser(string accuRevUserName, string name, string email)
			{
				Name = name;
				Email = email;
				AccuRevUserName = accuRevUserName;
			}

			public override string ToString()
			{
				return string.Format("\"{0} <{1}>\"", Name, Email);
			}
		}
	}
}
