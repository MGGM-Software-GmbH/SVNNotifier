using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CHD.SVN_Notifier
{
	/// <summary>
	/// String comparer that uses StrCmpLogicalW — the same sort Windows Explorer uses.
	/// Hyphens and other punctuation are treated as low-weight separators, matching v2 behavior.
	/// </summary>
	internal sealed class WindowsPathComparer : IComparer<string>
	{
		public static readonly WindowsPathComparer Instance = new WindowsPathComparer();

		[DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
		private static extern int StrCmpLogicalW(string x, string y);

		public int Compare(string? x, string? y)
		{
			if (x == null && y == null) return 0;
			if (x == null) return -1;
			if (y == null) return 1;
			return StrCmpLogicalW(x, y);
		}
	}
}
