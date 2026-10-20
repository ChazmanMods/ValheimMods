using System;
using System.Collections.Generic;
using RunicAutomation.Operations;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal static class AuthorityProductionEvents
    {
        internal enum Kind { Input, Fuel, Output }
        [ThreadStatic] internal static List<Tuple<int, int, long>> Current;
        internal static void Add(Kind kind, int slot = 0, long owner = 0) => Current?.Add(Tuple.Create((int)kind, slot, owner));
        internal static byte[] Encode()
        {
            var packet = new ZPackage(); packet.Write(1); packet.Write(Current?.Count ?? 0);
            if (Current != null) foreach (var e in Current) { packet.Write(e.Item1); packet.Write(e.Item2); packet.Write(e.Item3); }
            return packet.GetArray();
        }
        internal static void Play(Component station, byte[] bytes)
        {
            if (station == null || bytes == null || bytes.Length == 0) return;
            var packet = new ZPackage(bytes);
            if (packet.ReadInt() != 1) throw new InvalidOperationException("Invalid production effects packet");
            int count = packet.ReadInt(); if (count < 0 || count > 1024) throw new InvalidOperationException("Invalid production effects count");
            var events = new List<Tuple<int, int, long>>();
            for (int n = 0; n < count; n++) events.Add(Tuple.Create(packet.ReadInt(), packet.ReadInt(), packet.ReadLong()));
            OperationPacket.End(packet);
            foreach (var e in events) Play(station, (Kind)e.Item1, e.Item2, e.Item3);
        }
        internal static void Play(Component station, Kind kind, int slot = 0, long owner = 0)
        {
            if (station is Smelter smelter)
            {
                if (kind == Kind.Input) ProductionStationEffects.InputAdded(smelter);
                else if (kind == Kind.Fuel) ProductionStationEffects.FuelAdded(smelter);
                else if (kind == Kind.Output) ProductionStationEffects.OutputProduced(smelter);
            }
            else if (station is CookingStation cooking)
            {
                if (kind == Kind.Fuel) ProductionStationEffects.FuelAdded(cooking);
                else if (slot >= 0 && slot < cooking.m_slots.Length)
                {
                    if (kind == Kind.Input) ProductionStationEffects.InputAdded(cooking, slot);
                    else if (kind == Kind.Output) ProductionStationEffects.OutputCollected(cooking, slot);
                    CookingExperience.Award(cooking, owner, kind == Kind.Input ? .4f : .6f);
                }
            }
            else if (station is Fermenter fermenter)
            {
                if (kind == Kind.Input) ProductionStationEffects.InputAdded(fermenter);
                else if (kind == Kind.Output) ProductionStationEffects.OutputCollected(fermenter);
            }
            else if (station is Fireplace fire && kind == Kind.Fuel) ProductionStationEffects.FuelAdded(fire);
            else if (station is Beehive hive && kind == Kind.Output) ProductionStationEffects.OutputCollected(hive);
        }
    }
}
