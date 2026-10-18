using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace iLovbeeStudio;
public sealed record BatteryReading(int? Percent,string Source,DateTimeOffset ReadAt,string State);
public sealed class BatteryReader {
    public BatteryReading? Latest {get;private set;}
    public string LastDiagnostic {get;private set;}="";
    public static bool IsKeyboard(string name)=>name.Contains("B87",StringComparison.OrdinalIgnoreCase)||name.Contains("iLovbee",StringComparison.OrdinalIgnoreCase);
    public static int? Percent(byte[] data)=>data.Length==1 && data[0]<=100?data[0]:null;
    public async Task<BatteryReading> ReadAsync(){
        var windows=await Task.Run(WindowsBattery.Read);
        if(windows!=null){LastDiagnostic="Windows Bluetooth device battery property.";return Latest=new(windows.Percent,"Windows 藍牙",windows.ReportedAt??DateTimeOffset.Now,"最近回報");}
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var notes=new List<string>();
        try{
            var paired=await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(timeout.Token);
            var keyboards=paired.Where(d=>IsKeyboard(d.Name)).ToArray();
            if(keyboards.Length>1)return Latest=new(null,"Bluetooth",DateTimeOffset.Now,"偵測到多個鍵盤，請只連接要設定的 B87。");
            foreach(var deviceInfo in keyboards){
                using var device=await BluetoothLEDevice.FromIdAsync(deviceInfo.Id).AsTask(timeout.Token);
                if(device==null){notes.Add("Cannot access paired keyboard.");continue;}
                var services=await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery,BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                if(services.Status!=GattCommunicationStatus.Success){notes.Add("Battery service: "+services.Status);continue;}
                foreach(var service in services.Services){
                    using(service){
                        var chars=await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel,BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                        if(chars.Status!=GattCommunicationStatus.Success)continue;
                        foreach(var characteristic in chars.Characteristics){
                            if(!characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))continue;
                            var value=await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                            if(value.Status!=GattCommunicationStatus.Success || value.Value==null)continue;
                            using var reader=DataReader.FromBuffer(value.Value);var data=new byte[reader.UnconsumedBufferLength];reader.ReadBytes(data);
                            if(Percent(data) is int percent){LastDiagnostic="GATT Battery Level 0x2A19, uncached read.";return Latest=new(percent,"Bluetooth",DateTimeOffset.Now,"已更新");}
                        }
                    }
                }
                notes.Add("Keyboard has no readable standard Battery Level.");
            }
            LastDiagnostic=keyboards.Length==0?"No paired B87 BLE device found.":string.Join("; ",notes);
            return Latest=new(null,"Bluetooth",DateTimeOffset.Now,keyboards.Length==0?"請先在 Windows 配對 B87，並切換至藍牙模式。":"鍵盤未提供可讀取的電量，請確認藍牙連線。");
        }catch(OperationCanceledException){LastDiagnostic="Bluetooth battery query timed out.";return Latest=new(null,"Bluetooth",DateTimeOffset.Now,"讀取逾時，請確認藍牙連線後重試。");}
        catch(Exception e){LastDiagnostic=e.GetType().Name+": "+e.Message;return Latest=new(null,"Bluetooth",DateTimeOffset.Now,"無法讀取電量，請確認藍牙連線。");}
    }
}
