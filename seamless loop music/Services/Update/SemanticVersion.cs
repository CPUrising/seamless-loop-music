using System;

namespace seamless_loop_music.Services.Update
{
    /// <summary>
    /// Minimal SemVer parser tailored for release tag comparison.
    /// Supports "1.2.3", "v1.2.3", and optional "-prerelease" / "+build" suffixes.
    /// </summary>
    public sealed class SemanticVersion
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public string Prerelease { get; }
        public string Text { get; }

        private SemanticVersion(int major, int minor, int patch, string prerelease, string text)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease;
            Text = text;
        }

        public static SemanticVersion FromTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;
            return FromString(tag.Trim().TrimStart('v', 'V'));
        }

        public static SemanticVersion FromString(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;

            var text = version.Trim().TrimStart('v', 'V');

            int sep = text.IndexOfAny(new[] { '-', '+' });
            var core = sep >= 0 ? text.Substring(0, sep) : text;
            var pre = sep >= 0 ? text.Substring(sep + 1) : null;

            var parts = core.Split('.');
            if (parts.Length < 1 || parts.Length > 3)
                return null;

            if (!int.TryParse(parts[0], out int major) || major < 0)
                return null;

            int minor = 0;
            int patch = 0;
            if (parts.Length >= 2 && (!int.TryParse(parts[1], out minor) || minor < 0))
                return null;
            if (parts.Length >= 3 && (!int.TryParse(parts[2], out patch) || patch < 0))
                return null;

            return new SemanticVersion(major, minor, patch, pre, text);
        }

        public static bool IsNewer(SemanticVersion candidate, SemanticVersion current)
        {
            if (candidate == null)
                return false;
            if (current == null)
                return true;
            return candidate.CompareTo(current) > 0;
        }

        public int CompareTo(SemanticVersion other)
        {
            if (other == null)
                return 1;

            int cmp = Major.CompareTo(other.Major);
            if (cmp != 0) return cmp;
            cmp = Minor.CompareTo(other.Minor);
            if (cmp != 0) return cmp;
            cmp = Patch.CompareTo(other.Patch);
            if (cmp != 0) return cmp;

            // Same core version: a pre-release is older than the stable release.
            bool minePre = !string.IsNullOrEmpty(Prerelease);
            bool otherPre = !string.IsNullOrEmpty(other.Prerelease);
            if (minePre && otherPre)
                return string.CompareOrdinal(Prerelease, other.Prerelease);
            if (minePre)
                return -1;
            if (otherPre)
                return 1;
            return 0;
        }
    }
}
