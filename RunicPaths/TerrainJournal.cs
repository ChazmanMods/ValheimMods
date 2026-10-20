using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicPaths;

internal sealed class TerrainState
{
    internal static readonly FieldInfo PaintFlags = AccessTools.Field(typeof(TerrainComp), "m_modifiedPaint");
    internal static readonly FieldInfo HeightFlags = AccessTools.Field(typeof(TerrainComp), "m_modifiedHeight");
    internal static readonly FieldInfo Masks = AccessTools.Field(typeof(TerrainComp), "m_paintMask");
    internal static readonly FieldInfo Levels = AccessTools.Field(typeof(TerrainComp), "m_levelDelta");
    internal static readonly FieldInfo Smooth = AccessTools.Field(typeof(TerrainComp), "m_smoothDelta");
    internal bool[] Painted, Changed;
    internal Color[] Colors;
    internal float[] Level, Smoothing;
    internal TerrainState(TerrainComp compiler)
    {
        Painted=(bool[])((bool[])PaintFlags.GetValue(compiler)).Clone();
        Changed=(bool[])((bool[])HeightFlags.GetValue(compiler)).Clone();
        Colors=(Color[])((Color[])Masks.GetValue(compiler)).Clone();
        Level=(float[])((float[])Levels.GetValue(compiler)).Clone();
        Smoothing=(float[])((float[])Smooth.GetValue(compiler)).Clone();
    }
    internal TerrainState(TerrainState source)
    {
        Painted=(bool[])source.Painted.Clone(); Changed=(bool[])source.Changed.Clone(); Colors=(Color[])source.Colors.Clone();
        Level=(float[])source.Level.Clone(); Smoothing=(float[])source.Smoothing.Clone();
    }
    internal void Commit(TerrainComp compiler)
    {
        PaintFlags.SetValue(compiler, Painted); HeightFlags.SetValue(compiler, Changed);
        Masks.SetValue(compiler, Colors); Levels.SetValue(compiler, Level); Smooth.SetValue(compiler, Smoothing);
    }
}

// Store both baseline and last applied values. Later player changes are never rolled back.
internal sealed class TerrainJournal
{
    internal const string Key="RunicPaths.TerrainJournal.v1";
    private sealed class Entry
    {
        internal int Index;
        internal bool Paint, Height, OldPaint, OldHeight;
        internal Color OldColor, LastColor;
        internal float OldLevel, OldSmooth, LastLevel, LastSmooth;
    }
    private readonly Dictionary<int,Entry> entries=new Dictionary<int,Entry>();
    internal void RestoreSeamHeights(TerrainState state, Func<int,bool> seam)
    {
        foreach(var e in entries.Values)
            if(e.Height && seam(e.Index) && state.Changed[e.Index] &&
                Math.Abs(state.Level[e.Index]-e.LastLevel)<0.0001f && Math.Abs(state.Smoothing[e.Index]-e.LastSmooth)<0.0001f)
            {state.Changed[e.Index]=e.OldHeight;state.Level[e.Index]=e.OldLevel;state.Smoothing[e.Index]=e.OldSmooth;}
    }
    internal void Restore(TerrainState state, Func<int,bool>? protect=null)
    {
        foreach(var e in entries.Values)
        {
            int i=e.Index; if(protect!=null && protect(i)) continue;
            if(e.Paint && state.Painted[i] && state.Colors[i]==e.LastColor)
            { state.Painted[i]=e.OldPaint; state.Colors[i]=e.OldColor; }
            if(e.Height && state.Changed[i] && Math.Abs(state.Level[i]-e.LastLevel)<0.0001f && Math.Abs(state.Smoothing[i]-e.LastSmooth)<0.0001f)
            { state.Changed[i]=e.OldHeight; state.Level[i]=e.OldLevel; state.Smoothing[i]=e.OldSmooth; }
        }
    }
    internal void Record(int i, TerrainState baseline, TerrainState result, bool paint, bool height)
    {
        if(!paint && !height) return;
        entries[i]=new Entry { Index=i, Paint=paint, Height=height, OldPaint=baseline.Painted[i], OldHeight=baseline.Changed[i],
            OldColor=baseline.Colors[i], LastColor=result.Colors[i], OldLevel=baseline.Level[i], OldSmooth=baseline.Smoothing[i],
            LastLevel=result.Level[i], LastSmooth=result.Smoothing[i] };
    }
    internal byte[] Serialize()
    {
        using var output=new MemoryStream();
        using(var zip=new GZipStream(output,System.IO.Compression.CompressionLevel.Optimal,true))
        using(var writer=new BinaryWriter(zip))
        {
            writer.Write(1); writer.Write(entries.Count);
            foreach(var e in entries.Values)
            {
                writer.Write(e.Index); writer.Write(e.Paint); writer.Write(e.Height); writer.Write(e.OldPaint); writer.Write(e.OldHeight);
                foreach(var c in new[]{e.OldColor,e.LastColor}) { writer.Write(c.r);writer.Write(c.g);writer.Write(c.b);writer.Write(c.a); }
                writer.Write(e.OldLevel);writer.Write(e.OldSmooth);writer.Write(e.LastLevel);writer.Write(e.LastSmooth);
            }
        }
        return output.ToArray();
    }
    internal static TerrainJournal Load(byte[]? bytes, int size)
    {
        var result=new TerrainJournal(); if(bytes==null || bytes.Length==0) return result;
        using var stream=new MemoryStream(bytes); using var zip=new GZipStream(stream,CompressionMode.Decompress); using var reader=new BinaryReader(zip);
        if(reader.ReadInt32()!=1) throw new InvalidDataException("Unknown path terrain journal");
        int count=reader.ReadInt32(); if(count<0 || count>size) throw new InvalidDataException("Invalid path terrain journal size");
        for(int n=0;n<count;n++)
        {
            var e=new Entry { Index=reader.ReadInt32(), Paint=reader.ReadBoolean(), Height=reader.ReadBoolean(), OldPaint=reader.ReadBoolean(), OldHeight=reader.ReadBoolean() };
            e.OldColor=new Color(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
            e.LastColor=new Color(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
            e.OldLevel=reader.ReadSingle(); e.OldSmooth=reader.ReadSingle(); e.LastLevel=reader.ReadSingle(); e.LastSmooth=reader.ReadSingle();
            if(e.Index<0 || e.Index>=size || result.entries.ContainsKey(e.Index)) throw new InvalidDataException("Invalid terrain journal cell");
            result.entries.Add(e.Index,e);
        }
        return result;
    }
}



