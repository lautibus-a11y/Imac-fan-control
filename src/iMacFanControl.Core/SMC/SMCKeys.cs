using System.Collections.Generic;
using iMacFanControl.Core.Models;

namespace iMacFanControl.Core.SMC;

public static class SMCKeys
{
    // Ports
    public const ushort APPLESMC_DATA_PORT = 0x300;
    public const ushort APPLESMC_CMD_PORT = 0x304;

    // Status register bits
    public const byte SMC_STATUS_BUSY = 0x01;
    public const byte SMC_STATUS_WAITING = 0x02; // Awaiting input
    public const byte SMC_STATUS_READ = 0x04;    // Output data ready

    // Commands
    public const byte APPLESMC_CMD_READ = 0x10;
    public const byte APPLESMC_CMD_WRITE = 0x11;
    public const byte APPLESMC_CMD_GET_KEY_BY_INDEX = 0x12;
    public const byte APPLESMC_CMD_GET_KEY_INFO = 0x13;

    // System Keys
    public const string KEY_TOTAL_KEYS = "#KEY";
    public const string KEY_FAN_COUNT = "FNum";

    // Known Sensor Descriptions for iMac Mid 2011 (iMac12,1 / iMac12,2)
    public static readonly Dictionary<string, (string Name, SensorType Type)> KnownSensors = new()
    {
        // CPU
        { "TC0D", ("CPU Die / Core 0", SensorType.CPU) },
        { "TC0P", ("CPU Proximity", SensorType.CPU) },
        { "TC0H", ("CPU Heatsink", SensorType.CPU) },
        { "TC1D", ("CPU Core 1", SensorType.CPU) },
        { "TC2D", ("CPU Core 2", SensorType.CPU) },
        { "TC3D", ("CPU Core 3", SensorType.CPU) },

        // GPU
        { "TG0D", ("GPU Die", SensorType.GPU) },
        { "TG0P", ("GPU Proximity", SensorType.GPU) },
        { "TG0H", ("GPU Heatsink", SensorType.GPU) },
        { "TG1D", ("GPU Die Secondary", SensorType.GPU) },

        // Storage & Drives
        { "TH0P", ("HDD Bay / Hard Drive", SensorType.HDD) },
        { "TO0P", ("Optical Drive Bay", SensorType.ODD) },

        // Memory & Board
        { "Tm0P", ("Memory Controller", SensorType.Memory) },
        { "TM0P", ("Memory Proximity", SensorType.Memory) },
        { "TM0S", ("Memory Slot 1", SensorType.Memory) },
        { "TM1S", ("Memory Slot 2", SensorType.Memory) },

        // Ambient & Power
        { "TA0P", ("Ambient Air Intake", SensorType.Ambient) },
        { "TA1P", ("Ambient 2", SensorType.Ambient) },
        { "Tp0C", ("Power Supply AC/DC", SensorType.PowerSupply) },
        { "TL0P", ("LCD Panel Proximity", SensorType.Other) }
    };
}
