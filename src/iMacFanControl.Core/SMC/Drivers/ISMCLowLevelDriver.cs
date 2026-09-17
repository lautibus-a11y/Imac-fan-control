namespace iMacFanControl.Core.SMC.Drivers;

public interface ISMCLowLevelDriver
{
    string DriverName { get; }
    bool IsLoaded { get; }
    bool Initialize(out string errorMessage);
    byte ReadPort8(ushort port);
    void WritePort8(ushort port, byte value);
    void Close();
}
