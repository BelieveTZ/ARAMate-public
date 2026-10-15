using System.Reflection;
[assembly: AssemblyVersion(ConfidenceDesktop.AppVersion.Number)]
[assembly: AssemblyFileVersion(ConfidenceDesktop.AppVersion.Number)]
[assembly: AssemblyInformationalVersion(ConfidenceDesktop.AppVersion.Number)]
namespace ConfidenceDesktop {
    public static class AppVersion {
        public const string ProductName = "ARAMate 海斗助手";
        public static string LocalDataDirectory { get { return System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "ARAMate"); } }
        public static void MigrateLocalData() {
            string oldDirectory = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "BelieveTZ-ARAM-Companion");
            try { if (!System.IO.Directory.Exists(LocalDataDirectory) && System.IO.Directory.Exists(oldDirectory)) System.IO.Directory.Move(oldDirectory, LocalDataDirectory); }
            catch (System.IO.IOException) {} catch (System.UnauthorizedAccessException) {}
        }
        public const string Number = "2.1.6";
        public const string ReleaseDate = "2026.1016";
        public const string Display = Number;
    }
}
