// PAC Tools - the names and colours used in one place, so they are changed in one place.
namespace PacTools.Core
{
    /// <summary>Product names, ribbon names and the accent colour. No Revit API here.</summary>
    public static class Brand
    {
        /// <summary>The Revit ribbon tab every PAC Tools add-in adds its panels to.</summary>
        public const string TabName = "PAC Tools";

        /// <summary>The product name shown in window titles and the .addin manifest.</summary>
        public const string ProductName = "PAC Tools";

        /// <summary>Prefix for every ribbon button's internal name. Never change it once a release is out:
        /// Revit stores keyboard shortcuts and Quick Access entries against these names.</summary>
        public const string ButtonPrefix = "PACT_";

        /// <summary>Folder under %LocalAppData% and %AppData% used for logs and settings.</summary>
        public const string DataFolder = "PACTools";

        /// <summary>Accent colour (PAC blue) as #RRGGBB. Keep in step with the brush in the XAML windows.</summary>
        public const string AccentHex = "#0F5E8C";

        /// <summary>Darker shade of the accent, for pressed states and headings.</summary>
        public const string AccentDarkHex = "#0A4463";
    }
}
