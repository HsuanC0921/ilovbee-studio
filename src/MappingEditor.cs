using System;
using System.Collections.Generic;
using System.Linq;

namespace iLovbeeStudio;
public sealed class MappingEditor {
    readonly Stack<Dictionary<int,byte[]>> undo=new(),redo=new();
    public Dictionary<int,byte[]> Baseline {get;private set;}=new();
    public Dictionary<int,byte[]> Draft {get;private set;}=new();
    public bool CanUndo=>undo.Count>0;
    public bool CanRedo=>redo.Count>0;
    public int ChangeCount=>Baseline.Sum(p=>Enumerable.Range(0,104).Count(s=>Changed(p.Key,s)));
    public bool Changed(int layer,int slot)=>!Baseline[layer].AsSpan(slot*4,4).SequenceEqual(Draft[layer].AsSpan(slot*4,4));
    public static Dictionary<int,byte[]> Clone(Dictionary<int,byte[]> maps)=>maps.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());
    public void Load(Dictionary<int,byte[]> maps){Validate(maps);Baseline=Clone(maps);Draft=Clone(maps);undo.Clear();redo.Clear();}
    public static void Validate(Dictionary<int,byte[]> maps){if(maps.Count!=3 || Enumerable.Range(0,3).Any(i=>!maps.TryGetValue(i,out var m) || m==null || m.Length!=416))throw new ArgumentException("必須包含完整三層按鍵設定。");}
    public void Assign(int layer,int slot,byte[] code){
        if(layer<0 || layer>2 || slot<0 || slot>=104 || slot==59 || code.Length!=4)throw new ArgumentException("此按鍵無法指派。");
        if(!Draft.ContainsKey(layer))throw new InvalidOperationException("請先讀取鍵盤。");
        if(Draft[layer].AsSpan(slot*4,4).SequenceEqual(code))return;
        undo.Push(Clone(Draft));if(undo.Count>100){var keep=undo.Take(100).Reverse().ToArray();undo.Clear();foreach(var item in keep)undo.Push(item);}redo.Clear();code.CopyTo(Draft[layer],slot*4);
    }
    public void Undo(){if(!CanUndo)return;redo.Push(Clone(Draft));Draft=undo.Pop();}
    public void Redo(){if(!CanRedo)return;undo.Push(Clone(Draft));Draft=redo.Pop();}
    public void Discard()=>Load(Baseline);
    public void Revert(int layer,int slot)=>Assign(layer,slot,Baseline[layer].AsSpan(slot*4,4).ToArray());
    public void Resume(Dictionary<int,byte[]> desired){
        Validate(desired);foreach(int layer in desired.Keys)if(!Baseline[layer].AsSpan(59*4,4).SequenceEqual(desired[layer].AsSpan(59*4,4)))throw new ArgumentException("草稿不可修改 FN 鍵。");
        Draft=Clone(desired);undo.Clear();redo.Clear();
    }
    public IEnumerable<(int Layer,int Slot,string Before,string After)> Changes(){foreach(var m in Baseline)for(int s=0;s<104;s++)if(Changed(m.Key,s))yield return(m.Key,s,KeyActions.Decode(m.Value.AsSpan(s*4,4).ToArray()),KeyActions.Decode(Draft[m.Key].AsSpan(s*4,4).ToArray()));}
}
