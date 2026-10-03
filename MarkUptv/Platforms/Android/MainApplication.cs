using Android.App;
using Android.Runtime;

namespace MarkUptv
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();

            Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) =>
            {
                Android.Util.Log.Error("MarkUpTV", $"AndroidEnvironment Unhandled: {args.Exception}");
                args.Handled = true; 
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Android.Util.Log.Error("MarkUpTV", $"AppDomain Unhandled: {args.ExceptionObject}");
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                Android.Util.Log.Error("MarkUpTV", $"UnobservedTask: {args.Exception}");
                args.SetObserved();
            };
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
