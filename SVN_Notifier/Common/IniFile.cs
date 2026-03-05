using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CHD
{
	public class IniFile
	{
		// In-memory cache: section -> (key -> value)
		private readonly Dictionary<string, Dictionary<string, string>> _data =
			new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

		public IniFile(string fileName, bool checkExistance)
		{
			if (checkExistance && !File.Exists(fileName))
				throw new ApplicationException($"File {fileName} not found");
			FileName = fileName;
			FullFileName = new FileInfo(fileName).FullName;
			if (File.Exists(FullFileName))
				LoadFile();
		}

		public readonly string FileName;

		/// <summary>Full path to the INI file.</summary>
		public readonly string FullFileName;

		/// <summary>Active section name</summary>
		public string? Section;

		// ----------------------------------------------------------------
		// Read methods
		// ----------------------------------------------------------------

		public int ReadInteger(string section, string key, int defVal)
		{
			var s = ReadString(section, key, defVal.ToString());
			return int.TryParse(s, out var v) ? v : defVal;
		}

		public int ReadInteger(string section, string key) => ReadInteger(section, key, 0);

		public int ReadInteger(string key, int defVal) => ReadInteger(Section!, key, defVal);

		public int ReadInteger(string key) => ReadInteger(key, 0);

		public string ReadString(string section, string key, string defVal)
		{
			if (_data.TryGetValue(section, out var sec) && sec.TryGetValue(key, out var val))
				return val;
			return defVal;
		}

		public string ReadString(string section, string key) => ReadString(section, key, "");

		public string ReadString(string key) => ReadString(Section!, key);

		public long ReadLong(string section, string key, long defVal)
		{
			var s = ReadString(section, key, defVal.ToString());
			return long.TryParse(s, out var v) ? v : defVal;
		}

		public long ReadLong(string section, string key) => ReadLong(section, key, 0);

		public long ReadLong(string key, long defVal) => ReadLong(Section!, key, defVal);

		public long ReadLong(string key) => ReadLong(key, 0);

		public byte[] ReadByteArray(string section, string key)
			=> Convert.FromBase64String(ReadString(section, key));

		public byte[] ReadByteArray(string key) => ReadByteArray(Section!, key);

		public bool ReadBoolean(string section, string key, bool defVal)
		{
			var s = ReadString(section, key, defVal.ToString()).ToUpperInvariant();
			return s switch
			{
				"TRUE" or "YES" => true,
				"FALSE" or "NO" => false,
				_ => throw new ApplicationException($"Bad boolean value for '{key}' in [{section}]")
			};
		}

		public bool ReadBoolean(string section, string key) => ReadBoolean(section, key, false);

		public bool ReadBoolean(string key, bool defVal) => ReadBoolean(Section!, key, defVal);

		public bool ReadBoolean(string key) => ReadBoolean(Section!, key);

		// ----------------------------------------------------------------
		// Write methods
		// ----------------------------------------------------------------

		public void Write(string section, string key, string? value)
		{
			if (value is null)
			{
				DeleteKey(section, key);
				return;
			}
			if (!_data.TryGetValue(section, out var sec))
			{
				sec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				_data[section] = sec;
			}
			sec[key] = value;
			SaveFile();
		}

		public void Write(string section, string key, int value) => Write(section, key, value.ToString());

		public void Write(string key, int value) => Write(Section!, key, value);

		public void Write(string key, string value) => Write(Section!, key, value);

		public void Write(string section, string key, long value) => Write(section, key, value.ToString());

		public void Write(string key, long value) => Write(Section!, key, value);

		public void Write(string section, string key, byte[]? value)
		{
			if (value is null)
				Write(section, key, (string?)null);
			else
				Write(section, key, value, 0, value.Length);
		}

		public void Write(string key, byte[] value) => Write(Section!, key, value);

		public void Write(string section, string key, byte[] value, int offset, int length)
		{
			if (value is null)
				Write(section, key, (string?)null);
			else
				Write(section, key, Convert.ToBase64String(value, offset, length));
		}

		public void Write(string section, string key, bool value) => Write(section, key, value.ToString());

		public void Write(string key, bool value) => Write(Section!, key, value);

		// ----------------------------------------------------------------
		// Delete methods
		// ----------------------------------------------------------------

		public void DeleteKey(string section, string key)
		{
			if (_data.TryGetValue(section, out var sec))
				sec.Remove(key);
			SaveFile();
		}

		public void DeleteKey(string key) => DeleteKey(Section!, key);

		public void DeleteSection(string section)
		{
			_data.Remove(section);
			SaveFile();
		}

		// ----------------------------------------------------------------
		// Enumeration
		// ----------------------------------------------------------------

		public string[] GetSectionNames() => _data.Keys.ToArray();

		public string[] GetKeysAndValues(string section)
		{
			if (!_data.TryGetValue(section, out var sec))
				return Array.Empty<string>();
			return sec.Select(kv => $"{kv.Key}={kv.Value}").ToArray();
		}

		public string[] GetKeys(string section)
		{
			if (!_data.TryGetValue(section, out var sec))
				return Array.Empty<string>();
			return sec.Keys.ToArray();
		}

		// ----------------------------------------------------------------
		// Internal load / save
		// ----------------------------------------------------------------

		private void LoadFile()
		{
			_data.Clear();
			var lines = File.ReadAllLines(FullFileName);
			string? currentSection = null;

			foreach (var rawLine in lines)
			{
				var line = rawLine.Trim();
				if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#'))
					continue;

				if (line.StartsWith('[') && line.EndsWith(']'))
				{
					currentSection = line[1..^1].Trim();
					if (!_data.ContainsKey(currentSection))
						_data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				}
				else if (currentSection is not null)
				{
					var eqIdx = line.IndexOf('=');
					if (eqIdx > 0)
					{
						var k = line[..eqIdx].Trim();
						var v = line[(eqIdx + 1)..].Trim();
						_data[currentSection][k] = v;
					}
				}
			}
		}

		private void SaveFile()
		{
			var lines = new List<string>();
			foreach (var section in _data)
			{
				lines.Add($"[{section.Key}]");
				foreach (var kv in section.Value)
					lines.Add($"{kv.Key}={kv.Value}");
				lines.Add("");
			}
			File.WriteAllLines(FullFileName, lines);
		}
	}
}
