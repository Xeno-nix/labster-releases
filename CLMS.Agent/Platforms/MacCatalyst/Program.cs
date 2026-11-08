using UIKit;
namespace CLMS.Agent;
public static class Program
{
    /// <summary>Starts the Mac Catalyst application with its registered MAUI application delegate.</summary>
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
