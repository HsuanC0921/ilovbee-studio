using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace iLovbeeStudio;
public sealed record SavedDraft(Dictionary<int,string> Baseline,Dictionary<int,string> Desired);
/// <summary>JSON 來源產生器內容：反射式序列化在 Native AOT 不可用。</summary>
[JsonSourceGenerationOptions(WriteIndented=true)]
[JsonSerializable(typeof(DeviceProfile))]
[JsonSerializable(typeof(List<Macro>))]
[JsonSerializable(typeof(List<Scheme>))]
[JsonSerializable(typeof(Scheme))]
[JsonSerializable(typeof(List<LightLayout>))]
[JsonSerializable(typeof(SavedDraft))]
[JsonSerializable(typeof(List<PhysicalKey>))]
[JsonSerializable(typeof(string))]
public partial class AppJson : JsonSerializerContext{}
