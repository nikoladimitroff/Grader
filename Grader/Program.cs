using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Ionic.Zip;
using System.Text.RegularExpressions;

using Parallel = Grader.Sequential<string>;

namespace Grader
{

	[Serializable]
	[XmlRoot("test")]
	public struct Test
	{
		[XmlElement("input")]
		public string Input;
		[XmlElement("output")]
		public string Output;
	}

	[Serializable]
	[XmlRoot("testFile")]
	public struct TestCollection
	{
		[XmlArrayItem("test")]
		[XmlArray("tests")]
		public Test[] Tests;

		public static explicit operator Test[] (TestCollection collection)
		{
			return collection.Tests;
		}
	}

	struct Results
	{
		public double PointsPerTest;
		public string FacultyNumber;
		public string Problem;
		public bool[] TestResults;
		public string Homework;

		public Results(int tests, string homework, string problem, string facultyNumber)
		{
			this.PointsPerTest = 10 / (double) tests;
			this.TestResults = new bool[tests];
			this.Homework = homework;
			this.Problem = problem;
			this.FacultyNumber = facultyNumber;
		}
	}

	class Program
	{
		const int WaitForExecutable = 5000;
		const int WaitForCompiler = 5000;

		static void Main(string[] args)
		{
			// Folder paths
			string defaultDirectory = @"E:\ItP\Homeworks";
			string cmdPath = @"C:\Windows\System32\cmd.exe";
			string testsDirectory = @"E:\ItP\Tests";
			string zipDirectory = @"E:\ItP\Archives";

			// Clean up folder
			CleanUpFolder(defaultDirectory);

			ExtractArchivesTo(defaultDirectory, zipDirectory);
			Dictionary<string, Test[]> tests = LoadTests(testsDirectory);
			List<Results> results = RunTests(defaultDirectory, cmdPath, tests);

			TextWriter report = CreateReport(results);

			string reportPath = "report.txt";
			File.WriteAllText(reportPath, report.ToString());
			Process.Start(reportPath);
		}

		private static TextWriter CreateReport(List<Results> results)
		{
			TextWriter report = new StringWriter();
			// MANUAL PADDING TO THE RIGHT
			string pattern = "{0}{1}{2}{3}{4}";
			int padding = 20;
			report.WriteLine(pattern, "Faculty".PadRight(padding), "Homework".PadRight(padding), "Problem".PadRight(padding), "Total".PadRight(padding), "Tests".PadRight(padding));
			results.Sort((x, y) => x.FacultyNumber.CompareTo(y.FacultyNumber));
			
			foreach (var result in results)
			{
				report.WriteLine(pattern,
					result.FacultyNumber.PadRight(padding),
					result.Homework.PadRight(padding),
					result.Problem.PadRight(padding),
					(result.PointsPerTest * result.TestResults.Count(x => x)).ToString().PadRight(padding),
					result.TestResults.Aggregate("=", (x, y) => x += (Convert.ToInt32(y) * result.PointsPerTest) + "+").PadRight(padding));
			}
			return report;
		}

		private static List<Results> RunTests(string defaultDirectory, string cmdPath, Dictionary<string, Test[]> tests)
		{
			string[] homeworkDirectories = Directory.GetDirectories(defaultDirectory);
			List<Results> results = new List<Results>();
			Parallel.ForEach(homeworkDirectories, directory =>
			{
				string[] folderNameParts = directory.Split('.');
				string facultyNumber = folderNameParts[1];
				string homeworkIndex = folderNameParts[0].Remove(0, folderNameParts[0].IndexOf("hw"));

				Parallel.ForEach(Directory.GetFiles(directory, "*.cpp"), (file) =>
					{
						try
						{
							string fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
							if (!tests.ContainsKey(fileName))
							{
								return;
							}

							bool result = TryCompileFile(directory, cmdPath, fileName).Result;
							results.Add(new Results(tests[fileName].Length, homeworkIndex, fileName, facultyNumber));
							if (result)
								RunTests(directory, fileName, tests[fileName], results.Last());
							else
							{
								Console.WriteLine("Could not compile {0}", directory + file);
							}
						}
						catch (Exception e)
						{
							Console.WriteLine(e);
						}
					});
			});
			return results;
		}

		private static Dictionary<string, Test[]> LoadTests(string testsDirectory)
		{
			XmlSerializer serializer = new XmlSerializer(typeof(TestCollection));

			string[] testNames = Directory.GetFiles(testsDirectory);
			Dictionary<string, Test[]> tests = new Dictionary<string, Test[]>();
			foreach (string file in testNames)
			{
				using (StreamReader reader = new StreamReader(file))
				{
					TestCollection collection = (TestCollection)serializer.Deserialize(reader); ;
					Test[] testCollection = (Test[])collection;
					string problemName = Path.GetFileNameWithoutExtension(file);
					tests[problemName] = testCollection;
				}
			}
			return tests;
		}

		private static void ExtractArchivesTo(string defaultDirectory, string zipDirectory)
		{
			string[] archives = Directory.GetFiles(zipDirectory, "*.zip");
			Parallel.ForEach(archives, (archive) =>
				{
					int hwIndex = archive.IndexOf("hw");
					if (hwIndex == -1)
						return;

					string extractionPath = archive.StartsWith("hw") ? archive : archive.Substring(hwIndex);
					try
					{
						ZipFile zip = ZipFile.Read(archive);
						string fullPath = Path.Combine(defaultDirectory, extractionPath);
						Directory.CreateDirectory(fullPath);
						zip.ExtractAll(fullPath);
					}
					catch (ZipException)
					{
						Console.WriteLine("Unable to extract {0}", archive);
					}
				});
		}

		static string[] delimiter = { Environment.NewLine };

		private static void CleanUpFolder(string defaultDirectory)
		{
			DirectoryInfo homeworkFolder = new DirectoryInfo(defaultDirectory);

			foreach (FileInfo file in homeworkFolder.GetFiles())
			{
				file.Delete();
			}
			foreach (DirectoryInfo dir in homeworkFolder.GetDirectories())
			{
				dir.Delete(true);
			}
		}

		private static void RunTests(string directory, string fileName, Test[] tests, Results results)
		{
			for (int i = 0; i < tests.Length; i++)
			{
				results.TestResults[i] = RunTest(directory, fileName, tests[i]);
			}
		}

		private static bool RunTest(string directory, string fileName, Test test)
		{
			ProcessStartInfo info = new ProcessStartInfo();
			info.FileName = Path.Combine(directory, fileName) + ".exe";
			info.UseShellExecute = false;
			info.RedirectStandardInput = true;
			info.RedirectStandardOutput = true;
			info.CreateNoWindow = true;

			Process process = new Process();
			process.StartInfo = info;
			process.Start();
			process.StandardInput.WriteLine(test.Input.Trim());
			process.WaitForExit(WaitForExecutable);
			if (!process.HasExited)
				process.Kill();

			int next = process.StandardOutput.Peek();
			if (next == -1)
			{
				// In case the process was killed
				//	Console.WriteLine(next);
			}

			string result = NormalizeString(process.StandardOutput.ReadToEnd());
			string output = NormalizeString(test.Output);

			bool isCorrect = result == output;

			if (!isCorrect)
			{
				Console.WriteLine("Test failed: {0}\nExpect: {1}\nResult: {2}", Path.Combine(directory, fileName), output.Replace("\r\n", @"\r\n"), result.Replace("\r\n", @"\r\n"));
			}

			return isCorrect;
		}

		private static string NormalizeString(string result)
		{
			result = result.Trim().ToUpperInvariant();
			result = Regex.Replace(result, @"\r\n|\n\r|\n|\r", Environment.NewLine);
			result = result.Replace(@"\t", "    ");
			result = String.Join(Environment.NewLine, result.Split(delimiter, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
			return result;
		}

		private static async Task<bool> TryCompileFile(string userDirectory, string cmdPath, string fileName)
		{
			string location = Path.Combine(userDirectory, fileName) + ".cpp";
			File.WriteAllText(location, File.ReadAllText(location).Replace(@"system", @"//"));

			ProcessStartInfo compilerInfo = new ProcessStartInfo(cmdPath, @"%comspec% /k ""E:\Program files\Visual Studio 2012\Common7\Tools\VsDevCmd.bat""");
			compilerInfo.RedirectStandardInput = true;
			compilerInfo.RedirectStandardError = true;
			compilerInfo.RedirectStandardOutput = true;
			compilerInfo.CreateNoWindow = true;
			compilerInfo.UseShellExecute = false;

			Process compiler = new Process();
			compiler.StartInfo = compilerInfo;
			compiler.Start();
			compiler.StandardInput.WriteLine("cd " + userDirectory);
			compiler.StandardInput.WriteLine("cl /EHsc " + fileName + ".cpp");
			compiler.StandardInput.WriteLine("exit");
			compiler.WaitForExit(WaitForCompiler);

			string readOutput = await compiler.StandardOutput.ReadToEndAsync(),
				readError = await compiler.StandardError.ReadToEndAsync();

			string output = readOutput + readError;
			if (output.ToUpperInvariant().Contains("ERROR"))
				return false;
			return true;
		}
	}
}
