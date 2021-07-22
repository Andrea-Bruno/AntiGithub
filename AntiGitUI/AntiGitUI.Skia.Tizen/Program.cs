using Tizen.Applications;
using Uno.UI.Runtime.Skia;

namespace AntiGitUI.Skia.Tizen
{
	class Program
	{
		static void Main(string[] args)
		{
			var host = new TizenHost(() => new AntiGitUI.App(), args);
			host.Run();
		}
	}
}
