namespace QuietRoughSeas
{
    public partial class QuietRoughSeasSystem
    {
        // Deliberately exact: prereleases, future patches and legacy releases are unsupported.
        public static bool SupportedVersion(string version) { return version == "5.0.1"; }
    }
}
