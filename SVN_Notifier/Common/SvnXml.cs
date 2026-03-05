using System.Collections.Generic;
using System.Xml.Linq;

namespace CHD.SVN_Notifier
{
	public class SvnXml
	{
		private static string _output = "";
		private static readonly Dictionary<string, object> _data = new Dictionary<string, object>();

		public static void Create(string output)
		{
			_output = output;
			_data.Clear();
		}

		public static void ParseXmlForStatus()
		{
			var doc = XDocument.Parse(_output);

			// url element directly inside <target> (equivalent to depth==2 in the old reader)
			var urlEl = doc.Root?
				.Element("target")?
				.Element("url");
			if (urlEl != null)
				_data["url"] = urlEl.Value;

			// against revision inside <target>
			var againstRevision = doc.Root?
				.Element("target")?
				.Element("against")?
				.Attribute("revision")?.Value;
			if (againstRevision != null)
				_data["revision"] = againstRevision;

			// Process each <entry> element
			foreach (var entry in doc.Descendants("entry"))
			{
				bool skipNextReposStatus = false;

				var wcStatus = entry.Element("wc-status");
				if (wcStatus != null)
				{
					var item = wcStatus.Attribute("item")?.Value;
					if (item is not null
						&& item != "normal" && item != "unversioned"
						&& item != "none" && item != "external")
					{
						_data["Modified"] = true;
					}
					skipNextReposStatus = (item == "conflicted");

					var props = wcStatus.Attribute("props")?.Value;
					if (props is not null && props != "normal" && props != "none")
						_data["Modified"] = true;
					if (props == "conflicted")
						skipNextReposStatus = true;

					// commit revision (any depth — same as original)
					var commitRevision = wcStatus.Element("commit")?.Attribute("revision")?.Value;
					if (commitRevision != null)
						_data["revision"] = commitRevision;
				}

				if (!skipNextReposStatus)
				{
					var reposStatus = entry.Element("repos-status");
					if (reposStatus != null)
					{
						var item = reposStatus.Attribute("item")?.Value;
						if (item is not null
							&& item != "normal" && item != "unversioned"
							&& item != "none" && item != "external")
						{
							_data["NeedUpdate"] = true;
						}

						var props = reposStatus.Attribute("props")?.Value;
						if (props == "modified")
							_data["NeedUpdate"] = true;
					}
				}
			}
		}

		public static bool ContainsKey(string key) => _data.ContainsKey(key);

		public static string? GetValue(string key)
			=> _data.TryGetValue(key, out var v) ? v as string : null;
	}
}
