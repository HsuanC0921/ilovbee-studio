using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace iLovbeeStudio;
public sealed partial class MainWindow {
    async Task ValidateUi(string folder,FrameworkElement root){
        Directory.CreateDirectory(folder);var results=new List<object>();bool passed=true;timer.Stop();
        try{
            foreach(int width in new[]{1320,1030}){
                double scale=root.XamlRoot.RasterizationScale;AppWindow.Resize(new SizeInt32((int)(width*scale),(int)(850*scale)));await Task.Delay(200);
                foreach(string name in new[]{"按鍵重設","巨集","燈效","裝置狀態","按鍵測試","設定檔"}){
                    Navigate(name);root.UpdateLayout();await Task.Delay(400);
                    bool rendered=body.ActualWidth>500 && body.Children.Count>0;
                    bool keyCount=name!="按鍵重設" || buttons.Count==87;
                    bool state=name!="按鍵重設" || applyButton?.IsEnabled==(service.Current!=null && editor.ChangeCount>0);
                    results.Add(new{Width=width,Page=name,Rendered=rendered,KeyCountValid=keyCount,ApplyStateValid=state,Battery=battery.Latest?.Percent});passed&=rendered&&keyCount&&state;
                    await Capture(root,Path.Combine(folder,$"page-{Array.IndexOf(new[]{"按鍵重設","巨集","燈效","裝置狀態","按鍵測試","設定檔"},name)}-{width}.png"));
                }
            }
            File.WriteAllText(Path.Combine(folder,"ui-check.json"),JsonSerializer.Serialize(new{Passed=passed,Checks=results},Store.Json));
        }catch(Exception e){passed=false;File.WriteAllText(Path.Combine(folder,"ui-check-error.txt"),e.ToString());}
        Environment.ExitCode=passed?0:1;Application.Current.Exit();
    }
    static async Task Capture(FrameworkElement root,string path){
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(root,(int)root.ActualWidth,(int)root.ActualHeight);var buffer=await bitmap.GetPixelsAsync();
        using var reader=DataReader.FromBuffer(buffer);var pixels=new byte[buffer.Length];reader.ReadBytes(pixels);
        var folder=await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);var file=await folder.CreateFileAsync(Path.GetFileName(path),CreationCollisionOption.ReplaceExisting);
        using var stream=await file.OpenAsync(FileAccessMode.ReadWrite);var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);await encoder.FlushAsync();
    }
}

