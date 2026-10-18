using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Xaml;

namespace iLovbeeStudio;
public partial class App : Application {
    readonly Mutex instance;
    readonly bool first;
    Window? window;
    public App() {
        instance=new Mutex(true,@"Local\iLovbeeStudioWindow",out first);
        InitializeComponent();
        UnhandledException+=(_,e)=>{
            Directory.CreateDirectory(Store.Root);
            File.WriteAllText(Path.Combine(Store.Root,"last-error.txt"),e.Exception.ToString());
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        if(!first){Exit();return;}
        Directory.CreateDirectory(Store.Root);
        File.WriteAllText(Path.Combine(Store.Root,"program-path.txt"),Environment.ProcessPath??string.Empty);
        window=new MainWindow();window.Activate();
        window.Closed+=(_,_)=>instance.Dispose();
    }
}
