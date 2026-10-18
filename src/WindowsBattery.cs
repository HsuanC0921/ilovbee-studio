using System;
using System.Runtime.InteropServices;
using System.Text;

namespace iLovbeeStudio;
public sealed record WindowsBatteryReading(int Percent,DateTimeOffset? ReportedAt,string Name);
public static class WindowsBattery {
    [StructLayout(LayoutKind.Sequential)] struct Device {public int Size;public Guid Class;public uint DevInst;public IntPtr Reserved;}
    [StructLayout(LayoutKind.Sequential)] struct Property {public Guid Format;public uint Id;public Property(Guid format,uint id){Format=format;Id=id;}}
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevsW(IntPtr guid,string? enumerator,IntPtr parent,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set,uint index,ref Device device);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDevicePropertyW(IntPtr set,ref Device device,ref Property key,out uint type,byte[] buffer,uint size,out uint needed,uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    static readonly Guid DeviceProperties=new("a45c254e-df1c-4efd-8020-67d146a850e0"),BatteryProperties=new("104ea319-6ee2-4701-bd47-8ddbf425bbe5");
    static byte[]? Read(IntPtr set,ref Device device,Guid format,uint id,uint expectedType){var key=new Property(format,id);var bytes=new byte[1024];if(!SetupDiGetDevicePropertyW(set,ref device,ref key,out uint type,bytes,(uint)bytes.Length,out uint count,0)||type!=expectedType||count>bytes.Length)return null;return bytes.AsSpan(0,(int)count).ToArray();}
    public static WindowsBatteryReading? Read(){
        IntPtr set=SetupDiGetClassDevsW(IntPtr.Zero,"BTHLE",IntPtr.Zero,6);
        if(set==new IntPtr(-1))return null;
        try{WindowsBatteryReading? match=null;
            for(uint i=0;i<1024;i++){
                var device=new Device{Size=Marshal.SizeOf<Device>()};if(!SetupDiEnumDeviceInfo(set,i,ref device))break;
                var nameBytes=Read(set,ref device,DeviceProperties,14,18)??Read(set,ref device,DeviceProperties,2,18);if(nameBytes==null)continue;
                string name=Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');if(!name.Contains("B87",StringComparison.OrdinalIgnoreCase)&&!name.Contains("iLovbee",StringComparison.OrdinalIgnoreCase))continue;
                var level=Read(set,ref device,BatteryProperties,2,3);if(level==null||level.Length!=1||level[0]>100)continue;
                DateTimeOffset? at=null;var stamp=Read(set,ref device,BatteryProperties,7,16);if(stamp?.Length==8){try{at=new DateTimeOffset(DateTime.FromFileTimeUtc(BitConverter.ToInt64(stamp)));}catch(ArgumentOutOfRangeException){}}
                if(match!=null)return null;match=new(level[0],at,name.Trim());
            }return match;
        }finally{SetupDiDestroyDeviceInfoList(set);}
    }
}
