namespace iMacFanControl.Core.SMC;

public class SMCFan
{
    public int Index { get; set; }
    public string IdKey => $"F{Index}ID";
    public string ActualRpmKey => $"F{Index}Ac";
    public string MinRpmKey => $"F{Index}Mn";
    public string MaxRpmKey => $"F{Index}Mx";
    public string TargetRpmKey => $"F{Index}Tg";
    public string ModeKey => $"F{Index}Md";
    public string SafeRpmKey => $"F{Index}Sf";

    public SMCFan(int index)
    {
        Index = index;
    }
}
